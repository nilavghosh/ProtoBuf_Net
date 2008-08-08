﻿
namespace ProtoBuf.Property
{
    internal sealed class PropertyInt64ZigZag<TSource> : Property<TSource, long>
    {
        public override string DefinedType
        {
            get { return ProtoFormat.SINT64; }
        }
        public override WireType WireType { get { return WireType.Variant; } }

        public override int Serialize(TSource source, SerializationContext context)
        {
            long value = GetValue(source);
            if (IsOptional && value == DefaultValue) return 0;
            return WritePrefix(context)
                + Base128Variant.EncodeUInt64(Base128Variant.Zig(value), context);
        }

        public override long DeserializeImpl(TSource source, SerializationContext context)
        {
            return Base128Variant.Zag(Base128Variant.DecodeUInt64(context));
        }
    }
}
