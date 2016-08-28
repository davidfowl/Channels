using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Channels.Networking.Libuv.Interop;
using Channels.Networking.Libuv.Internal;

namespace Channels.Networking.Libuv
{
    // This class needs a bunch of work to make sure it's thread safe
    public class UvThread : ICriticalNotifyCompletion, IDisposable
    {
        internal static Action<Action<IntPtr>, IntPtr> _queueCloseCallback = QueueCloseHandle;

        private readonly Thread _thread = new Thread(OnStart);
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim();
        private readonly LockFreeWorkQueue<Work> _workQueue = new LockFreeWorkQueue<Work>();

        private bool _stopping;
        private UvAsyncHandle _postHandle;

        public UvThread()
        {

        }

        public Uv Uv { get; private set; }

        public UvLoopHandle Loop { get; private set; }

        public ChannelFactory ChannelFactory { get; private set; } = new ChannelFactory();

        public void Post(Action<object> callback, object state)
        {
            if (_stopping)
            {
                return;
            }

            EnsureStarted();

            var work = new Work
            {
                Callback = callback,
                State = state
            };

            _workQueue.Add(work);

            _postHandle.Send();
        }

        // Awaiter impl
        public bool IsCompleted => Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId;

        public UvThread GetAwaiter() => this;

        public void GetResult()
        {

        }

        private static void OnStart(object state)
        {
            ((UvThread)state).RunLoop();
        }

        private void RunLoop()
        {
            Uv = new Uv();

            Loop = new UvLoopHandle();
            Loop.Init(Uv);

            _postHandle = new UvAsyncHandle();
            _postHandle.Init(Loop, OnPost, _queueCloseCallback);

            _running.Set();

            Uv.run(Loop, 0);
        }

        private void OnPost()
        {
            foreach (var work in _workQueue.GetAndClear())
            {
                work.Callback(work.State);
            }

            if (_stopping)
            {
                _postHandle.Unreference();
            }
        }

        private void EnsureStarted()
        {
            if (!_running.IsSet)
            {
                _thread.Start(this);

                _running.Wait();
            }
        }

        private void Stop()
        {
            if (!_stopping)
            {
                _stopping = true;

                _postHandle.Send();

                _thread.Join();

                // REVIEW: Can you restart the thread?
            }
        }

        private static void QueueCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
            throw new InvalidOperationException();
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void OnCompleted(Action continuation)
        {
            Post(state => ((Action)state)(), continuation);
        }

        public void Dispose()
        {
            Stop();
        }

        private struct Work
        {
            public object State;
            public Action<object> Callback;
        }
    }
}
