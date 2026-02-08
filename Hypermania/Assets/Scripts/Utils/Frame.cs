using System;
using System.Buffers.Binary;
using MemoryPack;

namespace Utils
{
    [MemoryPackable]
    public partial struct Frame : IComparable<Frame>, IEquatable<Frame>, IFormattable, ISerializable
    {
        public int No;
        public static readonly Frame NullFrame = new Frame { No = -1 };
        public static readonly Frame FirstFrame = new Frame { No = 0 };
        public static readonly Frame Infinity = new Frame { No = int.MaxValue };

        public Frame(int No) => this.No = No;

        public int CompareTo(Frame other) => No.CompareTo(other.No);

        public bool Equals(Frame other) => No == other.No;

        public override bool Equals(object obj) => obj is Frame other && Equals(other);

        public override readonly int GetHashCode() => No.GetHashCode();

        public static Frame operator +(Frame left, int right) => new Frame { No = left.No + right };

        public static Frame operator -(Frame left, int right) => new Frame { No = left.No - right };

        public static int operator -(Frame left, Frame right) => left.No - right.No;

        public static bool operator ==(Frame left, Frame right) => left.Equals(right);

        public static bool operator !=(Frame left, Frame right) => !left.Equals(right);

        public static bool operator <(Frame a, Frame b) => a.CompareTo(b) < 0;

        public static bool operator >(Frame a, Frame b) => a.CompareTo(b) > 0;

        public static bool operator <=(Frame a, Frame b) => a.CompareTo(b) <= 0;

        public static bool operator >=(Frame a, Frame b) => a.CompareTo(b) >= 0;

        public static Frame Max(Frame a, Frame b) => a.No < b.No ? b : a;

        public static Frame Min(Frame a, Frame b) => a.No > b.No ? b : a;

        public override string ToString() => No.ToString();

        public string ToString(string format, IFormatProvider formatProvider) => No.ToString(format, formatProvider);

        public int Deserialize(ReadOnlySpan<byte> inBytes)
        {
            No = BinaryPrimitives.ReadInt32LittleEndian(inBytes);
            return sizeof(int);
        }

        public int Serialize(Span<byte> outBytes)
        {
            BinaryPrimitives.WriteInt32LittleEndian(outBytes, No);
            return sizeof(int);
        }

        public int SerdeSize()
        {
            return sizeof(int);
        }
    }
}
