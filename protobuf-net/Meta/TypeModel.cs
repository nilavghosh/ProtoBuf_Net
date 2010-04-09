﻿using System;
using System.IO;

namespace ProtoBuf.Meta
{
    /// <summary>
    /// Provides protobuf serialization support for a number of types
    /// </summary>
    public abstract class TypeModel
    {
        public void Serialize(Stream dest, object value)
        {
            if (value == null) throw new ArgumentNullException("value");
            int key = GetKey(value.GetType());
            using (ProtoWriter writer = new ProtoWriter(dest, this))
            {
                Serialize(key, value, writer);
                writer.Close();
            }
        }
        public object DeserializeWithLengthPrefix(Stream source, object value, Type type, PrefixStyle style, int fieldNumber)
        {
            return DeserializeWithLengthPrefix(source, value, type, style, fieldNumber, null);
        }
        public object DeserializeWithLengthPrefix(Stream source, object value, Type type, PrefixStyle style, int expectedField, Serializer.TypeResolver resolver)
        {

            bool skip;
            int len;
            do
            {
                int actualField;
                bool expectPrefix = expectedField > 0 || resolver != null;
                len = ProtoReader.ReadLengthPrefix(source, expectPrefix, style, out actualField);
                if (len < 0) return value;

                if (expectedField == 0 && type == null && resolver != null)
                {
                    type = resolver(actualField);
                    skip = type == null;
                }
                else { skip = expectedField != actualField; }

                if (skip)
                {
                    if (len == int.MaxValue) throw new InvalidOperationException();
                    ProtoReader.Seek(source, len, null);
                }
            } while (skip);

            int key = GetKey(type);
            if (key < 0) throw new InvalidOperationException();
            using (ProtoReader reader = new ProtoReader(source, this, len))
            {
                return Deserialize(key, value, reader);
            }

        }
        public void SerializeWithLengthPrefix(Stream dest, object value, Type type, PrefixStyle style, int fieldNumber)
        {
            if (type == null)
            {
                if(value == null) throw new ArgumentNullException("value");
                type = value.GetType();
            }
            int key = GetKey(type);
            using (ProtoWriter writer = new ProtoWriter(dest, this))
            {
                switch (style)
                {
                    case PrefixStyle.None:
                        Serialize(key, value, writer);
                        break;
                    case PrefixStyle.Base128:
                    case PrefixStyle.Fixed32:
                    case PrefixStyle.Fixed32BigEndian:
                        ProtoWriter.WriteObject(value, key, writer, style, fieldNumber);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("style");
                }
                writer.Close();
            }
        }
        public object Deserialize(Stream source, object value, Type type)
        {
            if (type == null)
            {
                if (value == null)
                {
                    throw new ArgumentNullException("type");
                } else {
                    type = value.GetType();
                }
            }
#if !NO_GENERICS
            type = Nullable.GetUnderlyingType(type) ?? type;
#endif
            int key = GetKey(type);
            using (ProtoReader reader = new ProtoReader(source, this))
            {
                return Deserialize(key, value, reader);
            }
        }
#if !NO_RUNTIME
        public static RuntimeTypeModel Create()
        {
            return new RuntimeTypeModel(false);
        }
#endif
        protected internal abstract int GetKey(Type type);
        protected internal abstract void Serialize(int key, object value, ProtoWriter dest);
        protected internal abstract object Deserialize(int key, object value, ProtoReader source);
        
        //internal ProtoSerializer Create(IProtoSerializer head)
        //{
        //    return new RuntimeSerializer(head, this);
        //}
        //internal ProtoSerializer Compile

        protected internal enum CallbackType
        {
            BeforeSerialize, AfterSerialize, BeforeDeserialize, AfterDeserialize
        }

        public object DeepClone(object value)
        {
            if (value == null) return null;
            int key = GetKey(value.GetType());
            
            using (MemoryStream ms = new MemoryStream())
            {
                using(ProtoWriter writer = new ProtoWriter(ms, this))
                {
                    Serialize(key, value, writer);
                    writer.Close();
                }
                ms.Position = 0;
                using (ProtoReader reader = new ProtoReader(ms, this))
                {
                    return Deserialize(key, null, reader);
                }
            }
        }
    }

}
