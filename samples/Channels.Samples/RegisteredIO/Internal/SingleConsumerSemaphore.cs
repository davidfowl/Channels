// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Channels.Samples.Internal
{
    public class SingleConsumerSemaphore : ICriticalNotifyCompletion
    {
        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private Action _awaitableState;

        private int _available;

        public SingleConsumerSemaphore(int initialCount)
        {
            _available = initialCount;
            _awaitableState = initialCount == 0 ? _awaitableIsNotCompleted : _awaitableIsCompleted;
        }

        public void OnCompleted(Action continuation)
        {
            var awaitableState = Interlocked.CompareExchange(
                   ref _awaitableState,
                   continuation,
                   _awaitableIsNotCompleted);

            if (ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                return;
            }
            else if (ReferenceEquals(awaitableState, _awaitableIsCompleted))
            {
                // Dispatch here to avoid stack diving
                Task.Run(continuation);
            }
            else
            {
                throw new InvalidOperationException("Concurrent awaits are not supported.");
            }
        }

        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        public bool IsCompleted => ReferenceEquals(_awaitableState, _awaitableIsCompleted);

        public bool TryAcquire(out int remaining)
        {
            var count = Volatile.Read(ref _available);
            while (count > 0)
            {
                var prev = Interlocked.CompareExchange(ref _available, count - 1, count);
                if (prev == count)
                {
                    if (count == 0)
                    {
                        Interlocked.CompareExchange(ref _awaitableState, _awaitableIsNotCompleted, _awaitableIsCompleted);
                        break;
                    }
                    if (count == 1) 
                    {
                        Interlocked.CompareExchange(ref _awaitableState, _awaitableIsNotCompleted, _awaitableIsCompleted);
                    }
                    remaining = count - 1;
                    return true;
                }
                count = prev;
            }

            remaining = 0;
            return false;
        }

        public int GetResult() => _available;

        private void Complete()
        {
            var awaitableState = Interlocked.Exchange(ref _awaitableState, _awaitableIsCompleted);

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                Task.Run(awaitableState);
            }
        }

        public void Release()
        {
            var count = Volatile.Read(ref _available);
            while (true)
            {
                var prev = Interlocked.CompareExchange(ref _available, count + 1, count);
                if (prev == count)
                {
                    if (count == 0)
                    {
                        Complete();
                    }
                    break;
                }
                count = prev;
            }
        }

        public SingleConsumerSemaphore GetAwaiter() => this;
    }
}
