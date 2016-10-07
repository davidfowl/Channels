namespace Channels
{
    public struct ChannelReadResult
    {
        public ChannelReadResult(ReadableBuffer buffer, bool isCompleted)
        {
            Buffer = buffer;
            IsCompleted = isCompleted;
        }

        public ReadableBuffer Buffer { get; }

        public bool IsCompleted { get; }
    }
}