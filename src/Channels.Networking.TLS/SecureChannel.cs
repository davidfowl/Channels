using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    public class SecureChannel<T> : ISecureChannel where T :ISecureContext
    {
        private IChannel _lowerChannel;
        private Channel _outputChannel;
        private Channel _inputChannel;
        private readonly T _contextToDispose;
        private TaskCompletionSource<ApplicationProtocols.ProtocolIds> _handShakeCompleted;
        
        internal SecureChannel(IChannel inChannel, ChannelFactory channelFactory, T secureContext)
        {
            _contextToDispose = secureContext;
            _lowerChannel = inChannel;
            _inputChannel = channelFactory.CreateChannel();
            _outputChannel = channelFactory.CreateChannel();
            StartWriting();
        }

        public IReadableChannel Input => _outputChannel;
        public IWritableChannel Output => _inputChannel;

        public Task<ApplicationProtocols.ProtocolIds> HandShakeAsync()
        {
            return _handShakeCompleted?.Task ?? DoHandShake();
        }

        private async Task<ApplicationProtocols.ProtocolIds> DoHandShake()
        {
            if(!_contextToDispose.IsServer)
            {
                //If it is a client we need to start by sending a client hello straight away
                var output = _lowerChannel.Output.Alloc();
                _contextToDispose.ProcessContextMessage(output);
                await output.FlushAsync();
            }
            try
            {
                while (true)
                {
                    var result = await _lowerChannel.Input.ReadAsync();
                    var buffer = result.Buffer;
                    try
                    {
                        if (buffer.IsEmpty && result.IsCompleted)
                        {
                            new InvalidOperationException("Connection closed before the handshake completed");
                        }
                        ReadableBuffer messageBuffer;
                        TlsFrameType frameType;
                        while (SecureContextExtensions.TryGetFrameType(ref buffer, out messageBuffer, out frameType))
                        {
                            if (frameType != TlsFrameType.Handshake && frameType != TlsFrameType.ChangeCipherSpec)
                            {
                                throw new InvalidOperationException("Received a token that was invalid during the handshake");
                            }
                            var output = _lowerChannel.Output.Alloc();
                            _contextToDispose.ProcessContextMessage(messageBuffer, output);
                            if (output.BytesWritten == 0)
                            {
                                output.Commit();
                            }
                            else
                            {
                                await output.FlushAsync();
                            }
                            if (_contextToDispose.ReadyToSend)
                            {
                                StartReading();
                                _handShakeCompleted = new TaskCompletionSource<ApplicationProtocols.ProtocolIds>();
                                _handShakeCompleted.SetResult(_contextToDispose.NegotiatedProtocol);
                                return await _handShakeCompleted.Task;
                            }
                        }
                    }
                    finally
                    {
                        _lowerChannel.Input.Advance(buffer.Start, buffer.End);
                    }
                }
            }
            catch(Exception ex)
            {
                Dispose();
                _handShakeCompleted = new TaskCompletionSource<ApplicationProtocols.ProtocolIds>();
                _handShakeCompleted.SetException(ex);
                return await _handShakeCompleted.Task;
            }

        }

        private async void StartReading()
        {
            try
            {
                while (true)
                {
                    var result = await _lowerChannel.Input.ReadAsync();
                    var buffer = result.Buffer;
                    try
                    {
                        if (buffer.IsEmpty && result.IsCompleted)
                        {
                            break;
                        }
                        ReadableBuffer messageBuffer;
                        TlsFrameType frameType;
                        while (SecureContextExtensions.TryGetFrameType(ref buffer, out messageBuffer, out frameType))
                        {
                            //We have app data or tokens at this point so slice out the message
                            //If we have app data, we will slice it out and process it
                            if (frameType == TlsFrameType.AppData)
                            {
                                var decryptedData = _outputChannel.Alloc();
                                _contextToDispose.Decrypt(messageBuffer, decryptedData);
                                await decryptedData.FlushAsync();
                            }
                            else
                            {
                                throw new InvalidOperationException("Invalid frame type during the connection was sent");
                            }
                        }
                    }
                    finally
                    {
                        _lowerChannel.Input.Advance(buffer.Start, buffer.End);
                    }
                }
            }
            finally
            {
                //Close down the lower channel
                _lowerChannel.Input.Complete();
                _lowerChannel.Output.Complete();
                //Tell the upper consumer that we aren't sending any more data
                _outputChannel.CompleteWriter();
                _outputChannel.CompleteReader();
                _inputChannel.CompleteReader();
                _inputChannel.CompleteWriter();
            }
        }
        
        private async void StartWriting()
        {
            var maxBlockSize = (SecurityContext.BlockSize - _contextToDispose.HeaderSize - _contextToDispose.TrailerSize);
            try
            {
                while (true)
                {
                    var result = await _inputChannel.ReadAsync();
                    await HandShakeAsync();
                    var buffer = result.Buffer;
                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        break;
                    }
                    try
                    {
                        while (buffer.Length > 0)
                        {
                            ReadableBuffer messageBuffer;
                            if (buffer.Length <= maxBlockSize)
                            {
                                messageBuffer = buffer;
                                buffer = buffer.Slice(buffer.End);
                            }
                            else
                            {
                                messageBuffer = buffer.Slice(0, maxBlockSize);
                                buffer = buffer.Slice(maxBlockSize);
                            }
                            var outputBuffer = _lowerChannel.Output.Alloc();
                            _contextToDispose.Encrypt(messageBuffer, outputBuffer);
                            await outputBuffer.FlushAsync();
                        }
                    }
                    finally
                    {
                        _inputChannel.Advance(buffer.End);
                    }
                }
            }
            finally
            {
                ///Close down the lower channel
                _lowerChannel.Input.Complete();
                _lowerChannel.Output.Complete();
                //Tell the upper consumer that we aren't sending any more data
                _outputChannel.CompleteWriter();
                _outputChannel.CompleteReader();
                _inputChannel.CompleteReader();
                _inputChannel.CompleteWriter();
            }
        }

        public void Dispose()
        {
            _lowerChannel.Dispose();
            _contextToDispose?.Dispose();

        }
    }
}
