using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Channels.Networking.Libuv.Internal
{
    // Lock free linked list that for multi producers and a single consumer
    internal class LockFreeWorkQueue<T>
    {
        private Node Head;

        public void Add(T value)
        {
            var node = new Node();
            node.Value = value;

            while (true)
            {
                var oldHead = Head;
                node.Next = Head;
                node.Count = 1 + (oldHead?.Count ?? 0);

                if (Interlocked.CompareExchange(ref Head, node, oldHead) == oldHead)
                {
                    break;
                }
            }
        }

        public T[] GetAndClear()
        {
            var node = Interlocked.Exchange(ref Head, null);

            if (node == null)
            {
                return EmptyArray<T>.Instance;
            }

            // TODO: Don't allocate here
            var values = new T[node.Count];
            int at = node.Count - 1;

            while (node != null)
            {
                values[at--] = node.Value;
                node = node.Next;
            }

            return values;
        }

        private class Node
        {
            public T Value;
            public Node Next;
            public int Count;
        }

        private static class EmptyArray<TArray>
        {
            public static TArray[] Instance = new TArray[0];
        }
    }
}
