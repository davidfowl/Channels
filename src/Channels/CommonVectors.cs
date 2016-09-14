using System.Numerics;

namespace Channels
{
    // Move to text library?
    internal class CommonVectors
    {
        private static Vector<byte>[] _vectorCache = new Vector<byte>[0x7F];

        static CommonVectors()
        {
            for (byte i = 0; i < 0x7f; i++)
            {
                _vectorCache[i] = new Vector<byte>(i);
            }
        }

        public static Vector<byte> GetVector(byte vectorByte)
        {
            if (vectorByte < _vectorCache.Length)
            {
                return _vectorCache[vectorByte];
            }

            return new Vector<byte>(vectorByte);
        }
    }
}
