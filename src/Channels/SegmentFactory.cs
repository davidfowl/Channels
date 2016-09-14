using Roslyn.Utilities;

namespace Channels
{
    internal class SegmentFactory
    {
        ObjectPool<MemoryBlockSegment> _pool = new ObjectPool<MemoryBlockSegment>(() => new MemoryBlockSegment(), 64);

        public MemoryBlockSegment Create(MemoryPoolBlock block)
        {
            var memoryBlockSegment = _pool.Allocate();
            memoryBlockSegment.Init(block);
            return memoryBlockSegment;
        }

        public MemoryBlockSegment Create(MemoryPoolBlock block, int start, int end)
        {
            var memoryBlockSegment = _pool.Allocate();
            memoryBlockSegment.Init(block, start, end);
            return memoryBlockSegment;
        }

        public void Return(MemoryBlockSegment segment)
        {
            _pool.Free(segment);
        }
    }
}