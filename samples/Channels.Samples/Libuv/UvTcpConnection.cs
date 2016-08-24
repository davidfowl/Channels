using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;

namespace Channels.Samples
{
    public class UvTcpConnection
    {
        private const int EOF = -4095;

        public IReadableChannel Input => _input;
        public IWritableChannel Output => _output;

        private MemoryPoolChannel _input;
        private MemoryPoolChannel _output;
        private WritableBuffer _buffer;
        private UvTcpListener _listener;
        private Task _sendingTask;

        public UvTcpConnection(UvTcpListener listener, UvTcpHandle handle)
        {
            _listener = listener;

            _input = listener.ChannelFactory.CreateChannel();
            _output = listener.ChannelFactory.CreateChannel();

            ProcessReads(handle);
            _sendingTask = ProcessWrites(handle);
        }

        private async Task ProcessWrites(UvTcpHandle handle)
        {
            var writeReq = new UvWriteReq2(_listener.Log);
            writeReq.Init(_listener.Loop);

            while (true)
            {
                await _output;

                var buffer = _output.BeginRead();

                if (buffer.IsEmpty && _output.Completion.IsCompleted)
                {
                    break;
                }

                var cloned = buffer.Clone();
                writeReq.Write(handle, cloned, WriteCallback, cloned);

                _output.EndRead(buffer);
            }

            _output.CompleteReading();

            handle.Dispose();
        }

        private static void WriteCallback(UvWriteReq2 handle, int status, Exception ex, object state)
        {
            ((ReadableBuffer)state).Dispose();
        }

        private void ProcessReads(UvTcpHandle handle)
        {
            handle.ReadStart(AllocCallback, ReadCallback, this);
        }

        private static void ReadCallback(UvStreamHandle handle, int status, object state)
        {
            ((UvTcpConnection)state).OnRead(handle, status);
        }

        private void OnRead(UvStreamHandle handle, int status)
        {
            if (status == 0)
            {
                // A zero status does not indicate an error or connection end. It indicates
                // there is no data to be read right now.
                // See the note at http://docs.libuv.org/en/v1.x/stream.html#c.uv_read_cb.
                // We need to clean up whatever was allocated by OnAlloc.
                return;
            }

            var normalRead = status > 0;
            var normalDone = status == EOF;
            var errorDone = !(normalDone || normalRead);
            var readCount = normalRead ? status : 0;

            if (normalRead)
            {
                // Log.ConnectionRead(ConnectionId, readCount);
            }
            else
            {
                handle.ReadStop();
            }

            _buffer.UpdateWritten(readCount);

            IOException error = null;
            if (errorDone)
            {
                Exception uvError;
                handle.Libuv.Check(status, out uvError);
                error = new IOException(uvError.Message, uvError);

                _input.CompleteWriting(error);
            }
            else if (readCount == 0)
            {
                _input.CompleteWriting();
            }
            else
            {
                _input.WriteAsync(_buffer);
            }
        }

        private static Libuv.uv_buf_t AllocCallback(UvStreamHandle handle, int status, object state)
        {
            return ((UvTcpConnection)state).OnAlloc(handle, status);
        }

        private Libuv.uv_buf_t OnAlloc(UvStreamHandle handle, int status)
        {
            _buffer = _input.Alloc(2048);
            return handle.Libuv.buf_init(_buffer.Memory.BufferPtr, _buffer.Memory.Length);
        }
    }
}
