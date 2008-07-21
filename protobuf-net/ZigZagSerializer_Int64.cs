﻿
namespace ProtoBuf
{
    partial class ZigZagSerializer : ISerializer<int>, ISerializer<long>
    {
        private static ulong WrapMsb(long value)
        {
            unchecked
            {
                return (ulong)((value << 1) ^ (value >> 63));
            }
        }

        public int Serialize(long value, SerializationContext context)
        {
            return WriteToStream(value, context);
        }
        public long Deserialize(long value, SerializationContext context)
        {
            return ReadInt64(context);
        }


        string ISerializer<long>.DefinedType { get { return ProtoFormat.SINT64; } }
        public int GetLength(long value, SerializationContext context)
        {
            return GetLength(value);
        }
        public static int GetLength(int value)
        {
            return TwosComplementSerializer.GetLength(WrapMsb(value));
        }
        public static int GetLength(long value)
        {
            return TwosComplementSerializer.GetLength(WrapMsb(value));
        }
        public static long ReadInt64(SerializationContext context)
        {
            long val = TwosComplementSerializer.ReadInt64(context);
            val = (-(val & 0x01)) ^ ((val >> 1) & ~Base128Variant.INT64_MSB);
            return val;
        }

        internal static int WriteToStream(long value, SerializationContext context)
        {
            ulong toWrite = WrapMsb(value);
            return TwosComplementSerializer.WriteToStream(toWrite, context);
        }
    }
}
