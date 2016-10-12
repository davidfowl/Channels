using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace Channels.Tests.Performance
{
    [Config(typeof(DefaultConfig))]
    public class MemoryPoolBenchmark
    {
        public const int InnerLoopCount = 10000;

        const int BlockSize = 64;
        public IBufferPool _memoryPool;
        public IBufferPool _localMemoryPool;

        [Setup]
        public void Setup()
        {
            _memoryPool = new MemoryPool();
            _memoryPool.Lease(BlockSize).Dispose();

            _localMemoryPool = new LocalMemoryPool();
            _localMemoryPool.Lease(BlockSize).Dispose();
        }

        [Cleanup]
        public void Cleanup()
        {
            _memoryPool?.Dispose();
            _localMemoryPool?.Dispose();
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = InnerLoopCount)]
        public void MemoryPool()
        {
            WriteParallel(_memoryPool);
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public void LocalMemoryPool()
        {
            WriteParallel(_localMemoryPool);
        }

        private static void WriteParallel(IBufferPool pool)
        {
            Action<int> action = (i) => Write(pool);
            int DoP = Environment.ProcessorCount * 4;

            Parallel.For(0, InnerLoopCount, 
                new ParallelOptions() { MaxDegreeOfParallelism = DoP }, 
                action);
        }

        private static void Write(IBufferPool pool)
        {
            var buffer = pool.Lease(BlockSize);
            var span = buffer.Data.Span;
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = 0xff;
            }
            buffer.Dispose();
        }
    }
}
