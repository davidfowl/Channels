using System.IO;

namespace Channels.Networking.TLS
{
    public class SecureChannel : IChannel
    {
        private IChannel _lowerChannel;
        private Channel _outputChannel;
        private Channel _inputChannel;
        private ISecureContext _contextToDispose;

        public SecureChannel(IChannel inChannel, ChannelFactory channelFactory)
        {
            _lowerChannel = inChannel;
            _inputChannel = channelFactory.CreateChannel();
            _outputChannel = channelFactory.CreateChannel();
        }

        public IReadableChannel Input => _outputChannel;
        public IWritableChannel Output => _inputChannel;

        internal async void StartReading<T>(T securityContext) where T : ISecureContext
        {
            _contextToDispose = securityContext;
            try
            {
                //If it is a client we need to start by sending a client hello straight away
                var output = _lowerChannel.Output.Alloc();
                securityContext.ProcessContextMessage(output);
                if(output.BytesWritten == 0) output.Commit(); else await output.FlushAsync();
                
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
                        while (true)
                        {
                            ReadableBuffer messageBuffer;
                            var frameType = SecureContextExtensions.CheckForFrameType(ref buffer, out messageBuffer);

                            //Incomplete frame so we check if the channel has completed, if not we wait for moar bytes!
                            if (frameType == TlsFrameType.Incomplete)
                            {
                                if (result.IsCompleted)
                                {
                                    throw new EndOfStreamException("There was a termination of the channel mid message");
                                }
                                break;
                            }

                            // IF we have an invalid frame type we need to throw an exception and terminate, 
                            // no mucking around for a secure connection
                            if (frameType == TlsFrameType.Invalid)
                            {
                                throw new EndOfStreamException("We have recieved an invalid tls frame");
                            }

                            //We have app data or tokens at this point so slice out the message
                            //If we have app data, we will slice it out and process it
                            if (frameType == TlsFrameType.AppData)
                            {
                                var decryptedData = _outputChannel.Alloc();
                                securityContext.Decrypt(messageBuffer, decryptedData);

                                await decryptedData.FlushAsync();
                            }

                            if (frameType == TlsFrameType.Handshake || frameType == TlsFrameType.ChangeCipherSpec)
                            {
                                try
                                {
                                    output = _lowerChannel.Output.Alloc();
                                    securityContext.ProcessContextMessage(messageBuffer, output);
                                    if (output.BytesWritten == 0) output.Commit(); else await output.FlushAsync();

                                    if (securityContext.ReadyToSend)
                                    {
                                        StartWriting(securityContext);
                                    }
                                }
                                catch
                                {
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
                //Tell the upper consumer that we aren't sending any more data
                _outputChannel.CompleteWriter();
                _outputChannel.CompleteReader();
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
                //Tell the upper channel we aren't processing anything
                _inputChannel.CompleteReader();
                _inputChannel.CompleteWriter();
                //Tell the lower channel we are not going to write any more
                _lowerChannel.Output.Complete();
            }
        }

        public void Dispose()
        {
            _lowerChannel.Dispose();
            _contextToDispose?.Dispose();

        }
    }
}
