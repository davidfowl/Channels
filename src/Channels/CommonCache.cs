using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Channels
{
    // Move to text library?
    internal class CommonCache
    {
        // Initalized by Channel outside of a hot path 
        internal static readonly Task CompletedTask = Task.FromResult(0);
        private static readonly byte[] _vectorCache = InitCommonVectors();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte[] InitCommonVectors()
        {
            var vectorCache = new byte[0x7F * Vector<byte>.Count];

            var index = 0;
            for (byte i = 0; i < 0x7f; i++)
            {
                for (var v = 0; v < Vector<byte>.Count; v++)
                {
                    vectorCache[index] = i;
                    index++;
                }
            }

            return vectorCache;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<byte> GetVector(byte vectorByte)
        {
            if (vectorByte < 0x7F)
            {
                return new Vector<byte>(_vectorCache, vectorByte*Vector<byte>.Count);
            }

            return GetUncachedVector(vectorByte);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Vector<byte> GetUncachedVector(byte vectorByte)
        {
            return new Vector<byte>(vectorByte);
        }
    }
}
