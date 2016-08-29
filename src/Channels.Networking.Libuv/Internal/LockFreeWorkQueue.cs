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

        public void Enqueue(T value)
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

        public void Dequeue(T[] values)
        {
            var node = Interlocked.Exchange(ref Head, null);

            int at = Math.Min(node.Count, values.Length) - 1;

            while (node != null)
            {
                values[at--] = node.Value;
                node = node.Next;
            }
        }

        private class Node
        {
            public T Value;
            public Node Next;
            public int Count;
        }
    }
}
