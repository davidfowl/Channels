using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Channels.Samples.Libuv.Interop;

namespace Channels.Samples.Libuv
{
    public class UvTcpConnection
    {
        private const int EOF = -4095;

        private static readonly Action<UvStreamHandle, int, object> _readCallback = ReadCallback;
        private static readonly Func<UvStreamHandle, int, object, Uv.uv_buf_t> _allocCallback = AllocCallback;
        private static readonly Action<UvWriteReq, int, Exception, object> _writeCallback = WriteCallback;

        private readonly MemoryPoolChannel _input;
        private readonly MemoryPoolChannel _output;
        private readonly Queue<ReadableBuffer> _outgoing = new Queue<ReadableBuffer>(1);

        private TaskCompletionSource<object> _connectionCompleted;
        private Task _sendingTask;
        private WritableBuffer _inputBuffer;

        public IReadableChannel Input => _input;
        public IWritableChannel Output => _output;

        public UvTcpConnection(ChannelFactory channelFactory, UvLoopHandle loop, UvTcpHandle handle)
        {
            _input = channelFactory.CreateChannel();
            _output = channelFactory.CreateChannel();

            ProcessReads(handle);
            _sendingTask = ProcessWrites(loop, handle);
        }

        private async Task ProcessWrites(UvLoopHandle loop, UvTcpHandle handle)
        {
            var writeReq = new UvWriteReq();
            writeReq.Init(loop);

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

                    // Up the reference count of the buffer so that we own the disposal of it
                    var cloned = buffer.Clone();
                    _outgoing.Enqueue(cloned);
                    writeReq.Write(handle, ref cloned, _writeCallback, this);

                    _output.EndRead(buffer);
                }
            }
            catch (Exception ex)
            {
                _output.CompleteReading(ex);
            }
            finally
            {
                _output.CompleteReading();

                // There's pending writes happening
                if (_outgoing.Count > 0)
                {
                    _connectionCompleted = new TaskCompletionSource<object>();

                    await _connectionCompleted.Task;
                }

                writeReq.Dispose();

                handle.Dispose();
            }
        }

        private static void WriteCallback(UvWriteReq req, int status, Exception ex, object state)
        {
            var connection = ((UvTcpConnection)state);
            connection._outgoing.Dequeue().Dispose();
            connection._connectionCompleted?.TrySetResult(null);
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

            if (!normalRead)
            {
                handle.ReadStop();
            }

            _inputBuffer.UpdateWritten(readCount);

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
                // TODO: If this task is incomplete then pause readin from the stream
                _input.WriteAsync(_inputBuffer);
            }
        }

        private static Uv.uv_buf_t AllocCallback(UvStreamHandle handle, int status, object state)
        {
            return ((UvTcpConnection)state).OnAlloc(handle, status);
        }

        private Uv.uv_buf_t OnAlloc(UvStreamHandle handle, int status)
        {
            _inputBuffer = _input.Alloc(2048);
            return handle.Libuv.buf_init(_inputBuffer.Memory.BufferPtr, _inputBuffer.Memory.Length);
        }
    }
}
