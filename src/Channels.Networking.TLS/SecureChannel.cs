using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    public class SecureChannel : IChannel
    {
        private IChannel _lowerChannel;
        private Channel _outputChannel;
        private Channel _inputChannel;
        private ISecureContext _contextToDispose;
        private TaskCompletionSource<ApplicationProtocols.ProtocolIds> _handShakeCompleted = new TaskCompletionSource<ApplicationProtocols.ProtocolIds>();
        
        public SecureChannel(IChannel inChannel, ChannelFactory channelFactory)
        {
            _lowerChannel = inChannel;
            _inputChannel = channelFactory.CreateChannel();
            _outputChannel = channelFactory.CreateChannel();
        }

        public IReadableChannel Input => _outputChannel;
        public IWritableChannel Output => _inputChannel;

        public Task<ApplicationProtocols.ProtocolIds> HandShakeAsync() => _handShakeCompleted.Task;

        internal async void StartReading<T>(T securityContext) where T : ISecureContext
        {
            _contextToDispose = securityContext;
            try
            {
                //If it is a client we need to start by sending a client hello straight away
                var output = _lowerChannel.Output.Alloc();
                securityContext.ProcessContextMessage(output);
                if (output.BytesWritten == 0)
                {
                    output.Commit();
                }
                else
                {
                    await output.FlushAsync();
                }
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
                        while (SecureContextExtensions.CheckForFrameType(ref buffer, out messageBuffer, out frameType))
                        {
                            //We have app data or tokens at this point so slice out the message
                            //If we have app data, we will slice it out and process it
                            if (frameType == TlsFrameType.AppData)
                            {
                                var decryptedData = _outputChannel.Alloc();
                                securityContext.Decrypt(messageBuffer, decryptedData);
                                await decryptedData.FlushAsync();
                            }
                            else
                            {
                                try
                                {
                                    //Must be a token or a change cipher message
                                    output = _lowerChannel.Output.Alloc();
                                    securityContext.ProcessContextMessage(messageBuffer, output);
                                    if (output.BytesWritten == 0)
                                    {
                                        output.Commit();
                                    }
                                    else
                                    {
                                        await output.FlushAsync();
                                    }
                                    if (securityContext.ReadyToSend)
                                    {
                                        StartWriting(securityContext);
                                        _handShakeCompleted.SetResult(securityContext.NegotiatedProtocol);
                                    }
                                }
                                catch(Exception ex)
                                {
                                    _handShakeCompleted.SetException(ex);
                                    throw;
                                }
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
        
        private async void StartWriting<T>(T securityContext) where T : ISecureContext
        {
            var maxBlockSize = (SecurityContext.BlockSize - securityContext.HeaderSize - securityContext.TrailerSize);
            try
            {
                while (true)
                {
                    var result = await _inputChannel.ReadAsync();
                    var buffer = result.Buffer;
                    if (buffer.IsEmpty && _inputChannel.Reading.IsCompleted)
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
                            securityContext.Encrypt(messageBuffer, outputBuffer);
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
