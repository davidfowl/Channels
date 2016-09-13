using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Channels
{
    // Move to text library?
    internal class CommonVectors
    {
        public static Vector<byte> CR = new Vector<byte>((byte)'\r');
        public static Vector<byte> LF = new Vector<byte>((byte)'\n');
        public static Vector<byte> Colon = new Vector<byte>((byte)':');
        public static Vector<byte> Space = new Vector<byte>((byte)' ');
        public static Vector<byte> Tab = new Vector<byte>((byte)'\t');
        public static Vector<byte> QuestionMark = new Vector<byte>((byte)'?');
        public static Vector<byte> Percentage = new Vector<byte>((byte)'%');

        private static Dictionary<byte, Vector<byte>> _vectorCache = new Dictionary<byte, Vector<byte>>();

        static CommonVectors()
        {
            for (byte i = 0; i < 0x7f; i++)
            {
                _vectorCache[i] = new Vector<byte>(i);
            }
        }

        public static Vector<byte> GetVector(byte vectorByte)
        {
            if (vectorByte == (byte)'\n')
            {
                return CR;
            }

            if (vectorByte == (byte)'\n')
            {
                return LF;
            }

            if (vectorByte == (byte)':')
            {
                return Colon;
            }

            if (vectorByte == (byte)' ')
            {
                return Space;
            }

            if (vectorByte == (byte)'\t')
            {
                return Tab;
            }

            if (vectorByte == (byte)'?')
            {
                return QuestionMark;
            }

            if (vectorByte == (byte)'%')
            {
                return Percentage;
            }

            Vector<byte> vec;
            if (_vectorCache.TryGetValue(vectorByte, out vec))
            {
                return vec;
            }
            return new Vector<byte>(vectorByte);
        }
    }
}
