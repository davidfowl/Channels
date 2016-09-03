using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Channels.Networking.Libuv.Internal
{
    // Lock free linked list that for multi producers and a single consumer
    internal class LockFreeWorkQueue<T>
    {
        private Node Head;

        public void Add(T value)
        {
            Node node = new Node(), oldHead;
            node.Value = value;

            do
            {
                oldHead = Head;
                node.Next = Head;
                node.Count = 1 + (oldHead?.Count ?? 0);
            } while (Interlocked.CompareExchange(ref Head, node, oldHead) != oldHead);
        }


        public Enumerable GetAndClear()
        {
            // swap out the head
            var node = Interlocked.Exchange(ref Head, null);

            // we now have a detatched head, but we're backwards
            // note: 0/1 are a trivial case
            if (node == null || node.Count == 1)
            {
                return new Enumerable(node);
            }
            // otherwise, we need to reverse the linked-list
            // note: use the iterative method to avoid a stack-dive
            Node prev = null;
            int count = 1; // rebuild the counts
            while (node != null)
            {
                var next = node.Next;
                node.Next = prev;
                node.Count = count++;
                prev = node;
                node = next;
            }
            return new Enumerable(prev);
        }

        internal class Node // need internal for Enumerator / Enumerable
        {
            public T Value;
            public Node Next;
            public int Count;
        }
        public struct Enumerable : IEnumerable<T>
        {
            private Node node;
            public int Count => node?.Count ?? 0;
            internal Enumerable(Node node)
            {
                this.node = node;
            }
            public Enumerator GetEnumerator() => new Enumerator(node);
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        }
        public struct Enumerator : IEnumerator<T>
        {
            void IDisposable.Dispose() { }
            private Node next;
            private T current;
            internal Enumerator(Node node)
            {
                this.current = default(T);
                this.next = node;
            }
            object IEnumerator.Current => current;
            public T Current => current;
            public bool MoveNext()
            {
                if (next == null)
                {
                    current = default(T);
                    return false;
                }
                current = next.Value;
                next = next.Next;
                return true;
            }
            public void Reset() { throw new NotSupportedException(); }
        }
    }
}