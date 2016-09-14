using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Channels.Networking.Sockets.Internal
{
    /// <summary>
    /// Very lightweight awaitable gate - intended for use in high-volume single-producer/single-consumer
    /// scenario, in particular targeting the bridge between async IO operations
    /// and the async method that is pumping the read/write queue. A key consideration is that
    /// no objects (in particular Task/TaskCompletionSource) are allocated even in the await case. Instead,
    /// a custom awaiter is provided that resets the state (making it incomplete) when GetResult is called.
    /// </summary>
    internal class Signal : INotifyCompletion
    {
        private readonly ContinuationMode _continuationMode;
        // note; interlocked access
        private Action _continuation;
        private volatile bool _isCompleted;

        public Signal(ContinuationMode continuationMode = ContinuationMode.Synchronous)
        {
            _continuationMode = continuationMode;
        }

        public bool IsCompleted => _isCompleted;

        private object SyncLock => this;

        public Signal GetAwaiter() => this;

        public void GetResult() // wipes in the process
        {
            _isCompleted = false;
        }

        public void OnCompleted(Action continuation)
        {
            if (continuation != null)
            {
                bool execute;
                if (!(execute = _isCompleted))
                {
                    lock (SyncLock)
                    {   // double-checked
                        if (!(execute = _isCompleted))
                        {
                            _continuation += continuation;
                        }
                    }
                }
                if (execute)
                {
                    continuation.Invoke();
                }
            }
        }

        public void Reset()
        {
            _isCompleted = false;
            _continuation = null;
        }

        public void Set()
        {
            Action continuation;
            lock (SyncLock)
            {
                continuation = _continuation;
                _continuation = null;
                _isCompleted = true;
            }
            if (continuation != null)
            {
                switch (_continuationMode)
                {
                    case ContinuationMode.Synchronous:
                        continuation.Invoke();
                        break;
                    case ContinuationMode.ThreadPool:
                        ThreadPool.QueueUserWorkItem(state => ((Action)state).Invoke(), continuation);
                        break;
                }
            }
        }

        // we can ourselves be an awaiter, why not?
        internal Signal WaitAsync() => this;
    }

}
