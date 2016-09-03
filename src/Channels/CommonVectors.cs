using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Channels
{
    // Move to text library?
    public static class CommonVectors
    {
        public static Vector<byte> CR = new Vector<byte>((byte)'\r');
        public static Vector<byte> LF = new Vector<byte>((byte)'\n');
        public static Vector<byte> Colon = new Vector<byte>((byte)':');
        public static Vector<byte> Space = new Vector<byte>((byte)' ');
        public static Vector<byte> Tab = new Vector<byte>((byte)'\t');
        public static Vector<byte> QuestionMark = new Vector<byte>((byte)'?');
        public static Vector<byte> Percentage = new Vector<byte>((byte)'%');
    }
}
