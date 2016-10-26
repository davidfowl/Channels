
namespace Channels
{
    /// <summary>
    /// Represents a buffer that is completely owned by this object.
    /// </summary>
    public class OwnedBuffer : ReferenceCountedBuffer
    {
        /// <summary>
        /// Create a new instance of <see cref="OwnedBuffer"/> that spans the array provided.
        /// </summary>
        public OwnedBuffer(byte[] buffer) : base(buffer, 0, buffer.Length)
        {
        }
    }
}