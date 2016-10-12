using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class ConcurrentPoolFacts
    {
        const int Count = 4096;
        const int NumberOfTasks = 2;
        const int SingleTaskBlockCount = Count / NumberOfTasks;

        [Fact]
        public async Task WorkWithConcurrentAccess()
        {
            var pool = new Poolable.ConcurrentPool<Poolable>();

            for (var i = 0; i < Count; i++)
            {
                var poolable = new Poolable();
                pool.RegisterForPooling(poolable);
                pool.Enqueue(poolable);
            }

            var @wait = new ManualResetEventSlim();

            var tasks = new Task<Poolable[]>[NumberOfTasks];
            for (var i = 0; i < NumberOfTasks; i++)
            {
                tasks[i] = Lease(wait, pool);
            }

            var all = Task.WhenAll(tasks);
            @wait.Set();

            var results = await all;
            var allBuffers = results.Aggregate(new HashSet<Poolable>(), (h1, h2) =>
            {
                h1.UnionWith(h2);
                return h1;
            });

            Assert.Equal(Count, allBuffers.Count);
        }

        /// <summary>
        /// Just leases <see cref="SingleTaskBlockCount"/> items from <paramref name="pool"/> and returns it.
        /// </summary>
        static Task<Poolable[]> Lease(ManualResetEventSlim wait, Poolable.ConcurrentPool<Poolable> pool)
        {
            return Task.Run(() =>
            {
                var items = new Poolable[SingleTaskBlockCount];
                @wait.Wait();
                for (var i = 0; i < SingleTaskBlockCount; i++)
                {
                    Poolable item;
                    Assert.True(pool.TryDequeue(out item));
                    items[i] = item;
                }
                return items;
            });
        }
    }
}