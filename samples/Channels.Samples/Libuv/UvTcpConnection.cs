using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;

namespace Channels.Samples
{
    public class UvTcpConnection
    {
        private const int EOF = -4095;

        private static readonly Action<UvStreamHandle, int, object> _readCallback = ReadCallback;
        private static readonly Func<UvStreamHandle, int, object, Libuv.uv_buf_t> _allocCallback = AllocCallback;
        private static readonly Action<UvWriteReq2, int, Exception, object> _writeCallback = WriteCallback;

        private readonly MemoryPoolChannel _input;
        private readonly MemoryPoolChannel _output;
        private readonly UvTcpListener _listener;
        private readonly Queue<ReadableBuffer> _outgoings = new Queue<ReadableBuffer>(1);

        private Task _sendingTask;
        private WritableBuffer _buffer;

        public IReadableChannel Input => _input;
        public IWritableChannel Output => _output;

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

            try
            {
                while (true)
                {
                    await _output;

                    var buffer = _output.BeginRead();

                    if (buffer.IsEmpty && _output.Completion.IsCompleted)
                    {
                        break;
                    }

                    var cloned = buffer.Clone();
                    _outgoings.Enqueue(cloned);
                    writeReq.Write(handle, cloned, _writeCallback, this);

                    _output.EndRead(buffer);
                }
            }
            finally
            {
                _output.CompleteReading();

                writeReq.Dispose();

                handle.Dispose();
            }
        }

        private static void WriteCallback(UvWriteReq2 req, int status, Exception ex, object state)
        {
            ((UvTcpConnection)state)._outgoings.Dequeue().Dispose();
        }

        private void ProcessReads(UvTcpHandle handle)
        {
            handle.ReadStart(_allocCallback, _readCallback, this);
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
