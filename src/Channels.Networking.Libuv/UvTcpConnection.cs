using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Channels.Networking.Libuv.Interop;

namespace Channels.Networking.Libuv
{
    public class UvTcpClientConnection : UvTcpConnection
    {
        public UvTcpClientConnection(UvThread thread, UvTcpHandle handle) :
            base(thread, handle)
        {

        }

        public IWritableChannel Input => _output;
        public IReadableChannel Output => _input;
    }

    public class UvTcpServerConnection : UvTcpConnection
    {
        public UvTcpServerConnection(UvThread thread, UvTcpHandle handle) :
            base(thread, handle)
        {

        }

        public IReadableChannel Input => _input;
        public IWritableChannel Output => _output;
    }

    public abstract class UvTcpConnection
    {
        private const int EOF = -4095;

        private static readonly Action<UvStreamHandle, int, object> _readCallback = ReadCallback;
        private static readonly Func<UvStreamHandle, int, object, Uv.uv_buf_t> _allocCallback = AllocCallback;
        private static readonly Action<UvWriteReq, int, Exception, object> _writeCallback = WriteCallback;

        private readonly Queue<ReadableBuffer> _outgoing = new Queue<ReadableBuffer>(1);

        protected readonly Channel _input;
        protected readonly Channel _output;
        private readonly UvThread _thread;
        private readonly UvTcpHandle _handle;

        private TaskCompletionSource<object> _connectionCompleted;
        private Task _sendingTask;
        private WritableBuffer _inputBuffer;

        public UvTcpConnection(UvThread thread, UvTcpHandle handle)
        {
            _thread = thread;
            _handle = handle;

            _input = _thread.ChannelFactory.CreateChannel();
            _output = _thread.ChannelFactory.CreateChannel();

            StartReading();
            _sendingTask = ProcessWrites();
        }

        private async Task ProcessWrites()
        {
            var writeReq = new UvWriteReq();
            writeReq.Init(_thread.Loop);

            try
            {
                while (true)
                {
                    var buffer = await _output;

                    // Make sure we're on the libuv thread
                    await _thread;

                    if (buffer.IsEmpty && _output.WriterCompleted.IsCompleted)
                    {
                        break;
                    }

                    // Up the reference count of the buffer so that we own the disposal of it
                    var outgoingBuffer = buffer.Preserve();
                    _outgoing.Enqueue(outgoingBuffer);
                    writeReq.Write(_handle, ref outgoingBuffer, _writeCallback, this);

                    buffer.Consumed();
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

                _handle.Dispose();
            }
        }

        private static void WriteCallback(UvWriteReq req, int status, Exception ex, object state)
        {
            var connection = ((UvTcpConnection)state);
            connection._outgoing.Dequeue().Dispose();
            connection._connectionCompleted?.TrySetResult(null);
        }

        private void StartReading()
        {
            _handle.ReadStart(_allocCallback, _readCallback, this);
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

            _inputBuffer.CommitBytes(readCount);

            IOException error = null;
            if (errorDone)
            {
                Exception uvError;
                handle.Libuv.Check(status, out uvError);
                error = new IOException(uvError.Message, uvError);

                _input.CompleteWriting(error);
            }
            else if (readCount == 0 || _input.ReaderCompleted.IsCompleted)
            {
                _input.CompleteWriting();
            }
            else
            {
                var task = _inputBuffer.FlushAsync();

                if (!task.IsCompleted)
                {
                    // If there's back pressure
                    handle.ReadStop();

                    // Resume reading when task continues
                    task.ContinueWith((t, state) => ((UvTcpConnection)state).StartReading(), this);
                }
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
