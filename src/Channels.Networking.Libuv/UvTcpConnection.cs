using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Channels.Networking.Libuv.Interop;

namespace Channels.Networking.Libuv
{
    public class UvTcpConnection
    {
        private const int EOF = -4095;

        private static readonly Action<UvStreamHandle, int, object> _readCallback = ReadCallback;
        private static readonly Func<UvStreamHandle, int, object, Uv.uv_buf_t> _allocCallback = AllocCallback;
        private static readonly Action<UvWriteReq, int, Exception, object> _writeCallback = WriteCallback;

        private readonly Queue<ReadableBuffer> _outgoing = new Queue<ReadableBuffer>(1);

        protected readonly Channel _outputChannel;
        protected readonly Channel _inputChannel;
        private readonly UvThread _thread;
        private readonly UvTcpHandle _handle;

        private TaskCompletionSource<object> _drainWrites;
        private Task _sendingTask;
        private WritableBuffer _inputBuffer;

        public UvTcpConnection(UvThread thread, UvTcpHandle handle)
        {
            _thread = thread;
            _handle = handle;

            _outputChannel = _thread.ChannelFactory.CreateChannel();
            _inputChannel = _thread.ChannelFactory.CreateChannel();

            StartReading();
            _sendingTask = ProcessWrites();
        }

        public IWritableChannel Output => _inputChannel.Output;

        public IReadableChannel Input => _outputChannel.Input;

        private async Task ProcessWrites()
        {
            try
            {
                while (true)
                {
                    var buffer = await _inputChannel.Input.ReadAsync();

                    try
                    {
                        // Make sure we're on the libuv thread
                        await _thread;

                        if (buffer.IsEmpty && _inputChannel.Input.Completion.IsCompleted)
                        {
                            break;
                        }

                        if (!buffer.IsEmpty)
                        {
                            var writeReq = _thread.WriteReqPool.Allocate();
                            writeReq.Write(_handle, ref buffer, _writeCallback, this);

                            // Preserve this buffer for disposal after the write completes
                            _outgoing.Enqueue(buffer.Preserve());
                        }
                    }
                    finally
                    {
                        buffer.Consumed();
                    }
                }
            }
            catch (Exception ex)
            {
                _inputChannel.Input.CompleteReading(ex);
            }
            finally
            {
                _inputChannel.Input.CompleteReading();

                // Drain the pending writes
                if (_outgoing.Count > 0)
                {
                    _drainWrites = new TaskCompletionSource<object>();

                    await _drainWrites.Task;
                }

                _handle.Dispose();

                // We'll never call the callback after disposing the handle
                _outputChannel.Output.CompleteWriting();
            }
        }

        private static void WriteCallback(UvWriteReq req, int status, Exception ex, object state)
        {
            var connection = ((UvTcpConnection)state);

            var buffer = connection._outgoing.Dequeue();

            // Dispose the preserved buffer
            buffer.Dispose();

            // Return the WriteReq
            connection._thread.WriteReqPool.Return(req);

            if (connection._drainWrites != null)
            {
                if (connection._outgoing.Count == 0)
                {
                    connection._drainWrites.TrySetResult(null);
                }
            }
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
                _inputBuffer.FlushAsync();
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

                _outputChannel.Output.CompleteWriting(error);
            }
            else if (readCount == 0 || _outputChannel.Input.Completion.IsCompleted)
            {
                _outputChannel.Output.CompleteWriting();
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

        private unsafe Uv.uv_buf_t OnAlloc(UvStreamHandle handle, int status)
        {
            _inputBuffer = _outputChannel.Output.Alloc(2048);
            return handle.Libuv.buf_init((IntPtr)_inputBuffer.Memory.UnsafePointer, _inputBuffer.Memory.Length);
        }
    }
}
