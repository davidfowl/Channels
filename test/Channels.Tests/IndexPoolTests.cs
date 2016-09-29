using Channels.Networking.Sockets.Internal;
using System;
using System.Diagnostics;
using Xunit;

namespace Channels.Tests
{
    public class IndexPoolTests
    {

        [Fact]
        public void TrivialPoolUsage_PutBack()
        {
            var pool = new IndexPool(true, 512);
            Assert.Equal(512, pool.Capacity);
            Assert.Equal(512, pool.CountRemaining());
            Assert.Equal(0, pool.CountTaken());

            for (int i = 0; i < 512; i++)
            {
                Assert.Equal(i, pool.TryTake());
                Assert.Equal(i + 1, pool.CountTaken());
                Assert.Equal(512 - (i + 1), pool.CountRemaining());
            }
            Assert.Equal(-1, pool.TryTake());
            for (int i = 0; i < 512; i++)
            {
                pool.PutBack(i);
                Assert.Equal(1, pool.CountRemaining());
                Assert.Equal(511, pool.CountTaken());

                Assert.Equal(i, pool.TryTake());
                Assert.Equal(0, pool.CountRemaining());
                Assert.Equal(512, pool.CountTaken());
            }
            Assert.Equal(-1, pool.TryTake());

            for (int i = 511; i >= 0; i--)
            {
                pool.PutBack(i);
                Assert.Equal(i, pool.CountTaken());
                Assert.Equal(512 - i, pool.CountRemaining());
            }

            Assert.Equal(0, pool.CountTaken());
            Assert.Equal(512, pool.CountRemaining());
        }

        [Fact]
        public void TrivialIndexPoolUsage_TryPutBack()
        {
            var pool = new IndexPool(false, 512);
            Assert.Equal(-1, pool.TryTake());
            Assert.Equal(0, pool.TryPutBack());
            Assert.Equal(1, pool.TryPutBack());
            Assert.Equal(0, pool.TryTake());
            Assert.Equal(1, pool.TryTake());
            Assert.Equal(-1, pool.TryTake());
            Assert.Equal(-1, pool.TryTake());

            // now saturate it:
            for (int i = 0; i < 512; i++)
            {
                Assert.Equal(i, pool.TryPutBack());
            }
            Assert.Equal(-1, pool.TryPutBack());

            // and drain it
            for (int i = 0; i < 512; i++)
            {
                Assert.Equal(i, pool.TryTake());
            }
            Assert.Equal(-1, pool.TryTake());

        }

        [Fact]
        public void TrivialObjectPoolUsage_TryPutBack()
        {
            var pool = new ObjectPool<string>(512);
            Assert.Equal(null, pool.TryTake());
            pool.PutBack("abc");
            pool.PutBack("def");
            Assert.Equal("abc", pool.TryTake());
            Assert.Equal("def", pool.TryTake());
            Assert.Equal(null, pool.TryTake());
            Assert.Equal(null, pool.TryTake());

            // now saturate it:
            for (int i = 0; i < 512; i++)
            {
                pool.PutBack(i.ToString());
            }
            pool.PutBack("meh");

            // and drain it
            for (int i = 0; i < 512; i++)
            {
                Assert.Equal(i.ToString(), pool.TryTake());
            }
            Assert.Equal(null, pool.TryTake());

        }

        [Fact]
        public void LinkedListIndexPool_CanTakeAndPutRandomly()
        {
            var pool = new IndexPool(true, 4096);
            Assert.Equal(4096, pool.Capacity);
            var reservations = new int[pool.Capacity];
            for (int i = 0; i < reservations.Length; i++)
            {
                reservations[i] = -1;
            }

            Assert.Equal(0, pool.CountTaken());
            var rand = new Random(123456);

            int taken = 0;
            var watch = Stopwatch.StartNew();

            const int take = 1000000;
            for (int i = 0; i < take; i++)
            {
                var worker = rand.Next(reservations.Length);
                // note we're storing page as off-by-one to help us here
                var res = reservations[worker];
                if (res >= 0)
                {
                    // release it
                    pool.PutBack(res);
                    reservations[worker] = -1;
                    taken--;
                }
                else
                {
                    // take a new one
                    res = pool.TryTake();
                    if (res < 0) throw new InvalidOperationException($"Unable to take; currently {pool.CountRemaining()} free");
                    int ix = Array.IndexOf(reservations, res);
                    if (ix >= 0) throw new InvalidOperationException($"Handed out a duplicate: {res} (exists at {ix}");
                    reservations[worker] = res;
                    taken++;
                }
            }
            watch.Stop();
            int takenFromPool = pool.CountTaken();
            Assert.Equal(taken, takenFromPool);

            for (int i = 0; i < reservations.Length; i++)
            {
                if (reservations[i] >= 0) pool.PutBack(reservations[i]);
            }
            Console.WriteLine($"took {take}; {watch.ElapsedMilliseconds}ms");
            Assert.Equal(0, pool.CountTaken());
        }

        [Fact]
        public void MicroBufferBasicUsage()
        {
            var pool = new MicroBufferPool(8, 512);
            ArraySegment<byte> a, b, c;
            Assert.True(pool.TryTake(out a));
            Assert.Equal(0, a.Offset);
            Assert.Equal(8, a.Count);
            Assert.True(pool.TryTake(out b));
            Assert.Equal(8, b.Offset);
            Assert.Equal(8, b.Count);
            Assert.True(pool.TryTake(out c));
            Assert.Equal(16, c.Offset);
            Assert.Equal(8, c.Count);

            Assert.True(pool.TryPutBack(b));
            Assert.True(pool.TryTake(out b));
            Assert.Equal(8, b.Offset);
            Assert.Equal(8, b.Count);

            Assert.False(pool.TryPutBack(new ArraySegment<byte>(b.Array, 3, b.Count)));
            Assert.False(pool.TryPutBack(new ArraySegment<byte>(b.Array, b.Offset, 3)));
            Assert.True(pool.TryPutBack(new ArraySegment<byte>(b.Array, b.Offset, b.Count)));
            Assert.True(pool.TryPutBack(a));
            Assert.True(pool.TryPutBack(c));

            Assert.False(pool.TryPutBack(default(ArraySegment<byte>)));
            Assert.False(pool.TryPutBack(new ArraySegment<byte>(new byte[pool.BytesPerItem])));

            for (int i = 0; i < 512; i++)
            {
                Assert.True(pool.TryTake(out a));
                Assert.Equal(i * pool.BytesPerItem, a.Offset);
                Assert.Equal(pool.BytesPerItem, a.Count);
            }
            var arr = a.Array;
            Assert.False(pool.TryTake(out a));
            Assert.Equal(0, pool.CountRemaining());
            for (int i = 0; i < 512; i++)
            {
                Assert.True(pool.TryPutBack(new ArraySegment<byte>(arr, i * pool.BytesPerItem, pool.BytesPerItem)));
            }
            Assert.Equal(512, pool.CountRemaining());


        }
    }

    
}
