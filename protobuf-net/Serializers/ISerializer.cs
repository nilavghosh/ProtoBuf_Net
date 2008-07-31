﻿
namespace ProtoBuf
{
    internal interface ISerializer<TValue>
    {
        TValue Deserialize(TValue value, SerializationContext context);
        int Serialize(TValue value, SerializationContext context);
        int GetLength(TValue value, SerializationContext context);
        
        WireType WireType { get; }
        string DefinedType { get; }
    }

    /// <summary>
    /// Additional support for serializers that can handle grouped (rather than length-prefixed) data (entities)
    /// </summary>
    internal interface IGroupSerializer<TValue> : ISerializer<TValue>
    {
        TValue DeserializeGroup(TValue value, SerializationContext context);
        int SerializeGroup(TValue value, SerializationContext context);
        int GetLengthGroup(TValue value, SerializationContext context);
    }
}
