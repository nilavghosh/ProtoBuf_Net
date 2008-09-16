﻿using System;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Globalization;
#if NET_3_0 || REMOTING
using System.Runtime.Serialization;
#endif

namespace ProtoBuf
{
    /// <summary>
    /// Provides protocol-buffer serialization capability for concrete, attributed types. 
    /// </summary>
    /// <remarks>
    /// Protocol-buffer serialization is a compact binary format, designed to take
    /// advantage of sparse data and knowledge of specific data types; it is also
    /// extensible, allowing a type to be deserialized / merged even if some data is
    /// not recognised.
    /// </remarks>
    public static class Serializer
    {
        internal static void VerifyBytesWritten(int expected, int actual)
        {
            if (actual != expected)
            {
                throw new ProtoException(string.Format(
                    "Wrote {0} bytes, but expected to write {1}.", actual, expected));
            }
        }

        private static readonly Type[] emptyTypes = new Type[0];
        internal static bool IsEntityType(Type type)
        {
            return type.IsClass && !type.IsAbstract
                    && type != typeof(string) && !type.IsArray
                    && type.GetConstructor(emptyTypes) != null
                    && (AttributeUtils.GetAttribute<ProtoContractAttribute>(type) != null
#if NET_3_0
                        || AttributeUtils.GetAttribute<DataContractAttribute>(type) != null
#endif
                        || AttributeUtils.GetAttribute<XmlTypeAttribute>(type) != null);
        }

        /// <summary>
        /// Supports various different property metadata patterns:
        /// [ProtoMember] is the most specific, allowing the data-format to be set.
        /// [DataMember], [XmlElement] are supported for compatibility.
        /// In any event, there must be a unique positive Tag/Order.
        /// </summary>
        internal static bool TryGetTag(MemberInfo member, out int tag, out string name, out DataFormat format, out bool isRequired)
        {
            return TryGetTag(member, out tag, out name, false, out format, out isRequired);
        }

        internal static IEnumerable<PropertyInfo> GetProtoProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }

        internal static bool TryGetTag(MemberInfo member, out int tag, out string name, bool callerIsTagInference, out DataFormat format, out bool isRequired)
        {
            name = member.Name;
            format = DataFormat.Default;
            tag = -1;
            isRequired = false;
            // check against the property
            ProtoMemberAttribute pm = AttributeUtils.GetAttribute<ProtoMemberAttribute>(member);
            if (pm == null)
            { // check also against the type 
                pm = AttributeUtils.GetAttribute<ProtoPartialMemberAttribute>(member.ReflectedType,
                    delegate(ProtoPartialMemberAttribute ppma) { return ppma.MemberName == member.Name; });
            }
            if (pm != null)
            {
                format = pm.DataFormat;
                if (!string.IsNullOrEmpty(pm.Name)) name = pm.Name;
                tag = pm.Tag;
                isRequired = pm.IsRequired;
                return tag > 0;
            }
#if NET_3_0
            DataMemberAttribute dm = AttributeUtils.GetAttribute<DataMemberAttribute>(member);
            if (dm != null)
            {
                if (!string.IsNullOrEmpty(dm.Name)) name = dm.Name;
                tag = dm.Order;
                if(!callerIsTagInference) // avoid infinite recursion
                {
                    ProtoContractAttribute pca = AttributeUtils.GetAttribute<ProtoContractAttribute>(member.DeclaringType);
                    if (pca != null && pca.InferTagFromName)
                    {
                        // since the type has inference enabled, identify the members for the
                        // type and give each an order based on the Order and Name, then find
                        // where the current property comes in the list. This will be repeated
                        // once (or more) per property during initialization, but not during
                        // core runtime - so it is not a perfomance bottleneck (so not worth
                        // complicating the implementation by caching it anywhere).

                        // find all properties under consideration
                        List<KeyValuePair<string, int>> members = new List<KeyValuePair<string,int>>();
                        string tmpName; // use this also to cache the "out" name (not usable from lambda)
                        foreach(PropertyInfo prop in GetProtoProperties(member.DeclaringType))
                        {
                            int tmpTag;
                            DataFormat tmpFormat;
                            bool tmpIsReq;
                            if(TryGetTag(prop, out tmpTag, out tmpName, true, out tmpFormat, out tmpIsReq))
                            {
                                members.Add(new KeyValuePair<string,int>(tmpName, tmpTag));
                            }
                        }
                        // sort by "Order, Name", where "Name" includes any renaming (i.e. not MemberInfo.Name)
                        members.Sort(delegate(KeyValuePair<string, int> x, KeyValuePair<string, int> y)
                        {
                            int result = x.Value.CompareTo(y.Value);
                            if (result == 0) result = string.CompareOrdinal(x.Key, y.Key);
                            return result;
                        });
                        // find the current item
                        tmpName = name;
                        tag = 1 + members.FindIndex(delegate(KeyValuePair<string, int> x)
                        {
                            return x.Key == tmpName;
                        });
                    }
                }
                isRequired = dm.IsRequired;
                return callerIsTagInference || tag > 0;
            }
#endif
            
            XmlElementAttribute xe = AttributeUtils.GetAttribute<XmlElementAttribute>(member);
            if (xe != null)
            {
                if (!string.IsNullOrEmpty(xe.ElementName)) name = xe.ElementName;
                tag = xe.Order;
                return tag > 0;
            }

            XmlArrayAttribute xa = AttributeUtils.GetAttribute<XmlArrayAttribute>(member);
            if (xa != null)
            {
                if (!string.IsNullOrEmpty(xa.ElementName)) name = xa.ElementName;
                tag = xa.Order;
                return tag > 0;
            }

            return false;
        }

        /// <summary>
        /// Creates a new instance from a protocol-buffer stream
        /// </summary>
        /// <typeparam name="T">The type to be created.</typeparam>
        /// <param name="source">The binary stream to apply to the new instance (cannot be null).</param>
        /// <returns>A new, initialized instance.</returns>
        public static T Deserialize<T>(Stream source)
        {
            T instance = default(T);
            try
            {
                SerializerProxy<T>.Default.Deserialize(ref instance, source);
            }
            catch (Exception ex)
            {
                ThrowInner(ex);
                throw; // if no inner (preserves stacktrace)
            }
            return instance;
        }


        internal static Exception ThrowNoEncoder(DataFormat format, Type valueType)
        {
            throw new ProtoException(string.Format(
                "No suitable {0} {1} encoding found.",
                format, valueType.Name));
        }

        /// <summary>
        /// Applies a protocol-buffer stream to an existing instance.
        /// </summary>
        /// <typeparam name="T">The type being merged.</typeparam>
        /// <param name="instance">The existing instance to be modified (can be null).</param>
        /// <param name="source">The binary stream to apply to the instance (cannot be null).</param>
        /// <returns>The updated instance; this may be different to the instance argument if
        /// either the original instance was null, or the stream defines a known sub-type of the
        /// original instance.</returns>
        public static T Merge<T>(Stream source, T instance)
        {
            try
            {
                SerializerProxy<T>.Default.Deserialize(ref instance, source);
                return instance;
            }
            catch (Exception ex)
            {
                ThrowInner(ex);
                throw; // if no inner (preserves stacktrace)
            }
        }

        /// <summary>
        /// Writes a protocol-buffer representation of the given instance to the supplied stream.
        /// </summary>
        /// <typeparam name="T">The type being serialized.</typeparam>
        /// <param name="instance">The existing instance to be serialized (cannot be null).</param>
        /// <param name="destination">The destination stream to write to.</param>
        public static void Serialize<T>(Stream destination, T instance)
        {
            try
            {
                SerializerProxy<T>.Default.Serialize(instance, destination);
            }
            catch (Exception ex)
            {
                ThrowInner(ex);
                throw; // if no inner (preserves stacktrace)
            }
        }

        private const string ProtoBinaryField = "proto";

#if REMOTING

        /// <summary>
        /// Creates a new IFormatter that uses protocol-buffer [de]serialization.
        /// </summary>
        /// <typeparam name="T">The type of object to be [de]deserialized by the formatter.</typeparam>
        /// <returns>A new IFormatter to be used during [de]serialization.</returns>
        public static IFormatter CreateFormatter<T>()
        {
            return new Formatter<T>();
        }

        /// <summary>
        /// Writes a protocol-buffer representation of the given instance to the supplied SerializationInfo.
        /// </summary>
        /// <typeparam name="T">The type being serialized.</typeparam>
        /// <param name="instance">The existing instance to be serialized (cannot be null).</param>
        /// <param name="info">The destination SerializationInfo to write to.</param>
        public static void Serialize<T>(SerializationInfo info, T instance) where T : ISerializable
        {
            // note: also tried byte[]... it doesn't perform hugely well with either (compared to regular serialization)
            if (info == null) throw new ArgumentNullException("info");
            if (instance == null) throw new ArgumentNullException("instance");
            using (MemoryStream ms = new MemoryStream())
            {
                Serialize<T>(ms, instance);
                info.AddValue(ProtoBinaryField, ms.ToArray());
                //string s = Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
                //info.AddValue(ProtoBinaryField, s);
            }
        }

        
        /// <summary>
        /// Applies a protocol-buffer from a SerializationInfo to an existing instance.
        /// </summary>
        /// <typeparam name="T">The type being merged.</typeparam>
        /// <param name="instance">The existing instance to be modified (cannot be null).</param>
        /// <param name="info">The SerializationInfo containing the data to apply to the instance (cannot be null).</param>
        public static void Merge<T>(SerializationInfo info, T instance) where T : ISerializable
        {
            // note: also tried byte[]... it doesn't perform hugely well with either (compared to regular serialization)
            if (info == null) throw new ArgumentNullException("info");
            if (instance == null) throw new ArgumentNullException("instance");
            //string s = info.GetString(ProtoBinaryField);
            //using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(s)))
            byte[] buffer = (byte[])info.GetValue(ProtoBinaryField, typeof(byte[]));
            using (MemoryStream ms = new MemoryStream(buffer))
            {
                Merge<T>(ms, instance);
            }
        }

#endif

        /// <summary>
        /// Writes a protocol-buffer representation of the given instance to the supplied XmlWriter.
        /// </summary>
        /// <typeparam name="T">The type being serialized.</typeparam>
        /// <param name="instance">The existing instance to be serialized (cannot be null).</param>
        /// <param name="writer">The destination XmlWriter to write to.</param>
        public static void Serialize<T>(System.Xml.XmlWriter writer, T instance) where T : IXmlSerializable
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (instance == null) throw new ArgumentNullException("instance");

            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, instance);
                writer.WriteBase64(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }
        /// <summary>
        /// Applies a protocol-buffer from an XmlReader to an existing instance.
        /// </summary>
        /// <typeparam name="T">The type being merged.</typeparam>
        /// <param name="instance">The existing instance to be modified (cannot be null).</param>
        /// <param name="reader">The XmlReader containing the data to apply to the instance (cannot be null).</param>
        public static void Merge<T>(System.Xml.XmlReader reader, T instance) where T : IXmlSerializable
        {
            if (reader == null) throw new ArgumentNullException("reader");
            if (instance == null) throw new ArgumentNullException("instance");
        
            const int LEN = 4096;
            byte[] buffer = new byte[LEN];
            int read;
            using (MemoryStream ms = new MemoryStream())
            {
                while ((read = reader.ReadElementContentAsBase64(buffer, 0, LEN)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                ms.Position = 0;
                Serializer.Merge(ms, instance);
            }
        }

        /// <summary>
        /// Create a deep clone of the supplied instance; any sub-items are also cloned.
        /// </summary>
        /// <typeparam name="T">The type being cloned.</typeparam>
        /// <param name="instance">The existing instance to be cloned.</param>
        /// <returns>A new copy, cloned from the supplied instance.</returns>
        public static T DeepClone<T>(T instance)
        {
            return ChangeType<T, T>(instance);
        }

        /// <summary>
        /// Serializes a given instance and deserializes it as a different type;
        /// this can be used to translate between wire-compatible objects (where
        /// two .NET types represent the same data), or to promote/demote a type
        /// through an inheritance hierarchy.
        /// </summary>
        /// <remarks>No assumption of compatibility is made between the types.</remarks>
        /// <typeparam name="TOldType">The type of the object being copied.</typeparam>
        /// <typeparam name="TNewType">The type of the new object to be created.</typeparam>
        /// <param name="instance">The existing instance to use as a template.</param>
        /// <returns>A new instane of type TNewType, with the data from TOldType.</returns>
        public static TNewType ChangeType<TOldType, TNewType>(TOldType instance)
        {
            return ChangeType<TOldType, TNewType>(instance, null);
        }

        /// <summary>
        /// As per the public ChangeType, but allows for workspace-sharing to reduce buffer overhead.
        /// </summary>
        internal static TNewType ChangeType<TOldType, TNewType>(TOldType instance, SerializationContext context)
        {
            if (instance == null)
            {
                return default(TNewType); // GIGO
            } 

            using (MemoryStream ms = new MemoryStream())
            {
                SerializationContext tmpCtx = new SerializationContext(ms, context);
                Serialize<TOldType>(ms, instance);
                tmpCtx.Flush();

                ms.Position = 0;
                TNewType result = Deserialize<TNewType>(ms);
                if (context != null)
                {
                    context.ReadFrom(tmpCtx);
                }
                return result;
            }
        }
#if !CF
        /// <summary>
        /// Suggest a .proto definition for the given type
        /// </summary>
        /// <typeparam name="T">The type to generate a .proto definition for</typeparam>
        /// <returns>The .proto definition as a string</returns>
        public static string GetProto<T>() where T : class, new()
        {
            try
            {
                return Serializer<T>.GetProto();
            }
            catch (Exception ex)
            {
                ThrowInner(ex);
                throw; // if no inner (preserves stacktrace)
            }
        }
#endif

        static void ThrowInner(Exception exception)
        {
            if (exception != null && exception.InnerException != null)
            {

                if (exception != null && exception.InnerException != null
                    && (exception is TargetInvocationException
#if !CF
                    || exception is TypeInitializationException
#endif
                    ))
                {
                    ThrowInner(exception.InnerException);
                    throw exception.InnerException;
                }
            }
        }

        internal static string GetDefinedTypeName<T>()
        {
            string name = typeof(T).Name;
            ProtoContractAttribute pc = AttributeUtils.GetAttribute<ProtoContractAttribute>(typeof(T));
            if (pc != null)
            {
                if (!string.IsNullOrEmpty(pc.Name)) name = pc.Name;
                return name;
            }
#if NET_3_0
            DataContractAttribute dc = AttributeUtils.GetAttribute<DataContractAttribute>(typeof(T));
            if (dc != null)
            {
                if (!string.IsNullOrEmpty(dc.Name)) name = dc.Name;
                return name;
            }
#endif

            XmlTypeAttribute xt = AttributeUtils.GetAttribute<XmlTypeAttribute>(typeof(T));
            if (xt != null)
            {
                if (!string.IsNullOrEmpty(xt.TypeName)) name = xt.TypeName;
                return name;
            }

            return name;
        }
        
        internal static int GetPrefixLength(int tag)
        {
            if ((tag & ~0x0000000F) == 0) return 1; // 4 bits
            if ((tag & ~0x000007FF) == 0) return 2; // 11 bits
            if ((tag & ~0x0003FFFF) == 0) return 3; // 18 bits
            if ((tag & ~0x01FFFFFF) == 0) return 4; // 25 bits
            return 5;            
        }

        internal static void ParseFieldToken(uint token, out WireType wireType, out int tag)
        {
            wireType = (WireType)(token & 7);
            tag = (int)(token >> 3);
            if (tag <= 0)
            {
                throw new ProtoException("Invalid tag: " + tag.ToString());
            }
        }

        internal static void SkipData(SerializationContext context, int fieldTag, WireType wireType)
        {

            switch (wireType)
            {
                case WireType.Variant:
                    context.ReadRawVariant();
                    break;
                case WireType.Fixed32:
                    context.ReadBlock(4);
                    break;
                case WireType.Fixed64:
                    context.ReadBlock(8);
                    break;
                case WireType.String:
                    int len = context.DecodeInt32();
                    context.WriteTo(Stream.Null, len);
                    break;
                case WireType.EndGroup:
                    throw new ProtoException("End-group not expected at this location");
                case WireType.StartGroup:
                    context.StartGroup(fieldTag); // will be ended internally
                    Serializer<UnknownType>.Build();
                    UnknownType ut = UnknownType.Default;
                    Serializer<UnknownType>.Deserialize(ref ut, context);
                    break;
                default:
                    throw new ProtoException("Unknown wire-type " + wireType.ToString());
            }
        }

        internal static int WriteFieldToken(int tag, WireType wireType, SerializationContext context)
        {
            uint prefix = (uint)((tag << 3) | ((int)wireType & 7));
            return context.EncodeUInt32(prefix);
        }

        internal struct ProtoEnumValue<TEnum>
        {
            private readonly TEnum enumValue;
            private readonly uint wireValue;
            private readonly string name;
            public TEnum EnumValue { get { return enumValue; } }
            public uint WireValue { get { return wireValue; } }
            public string Name { get { return name; } }
            public ProtoEnumValue(TEnum enumValue, uint wireValue, string name)
            {
                this.enumValue = enumValue;
                this.wireValue = wireValue;
                this.name = name;
            }
        }

        internal static IEnumerable<ProtoEnumValue<TEnum>> GetEnumValues<TEnum>()
        {
            List<ProtoEnumValue<TEnum>> list = new List<ProtoEnumValue<TEnum>>();
            foreach (FieldInfo enumField in typeof(TEnum).GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                if (!enumField.IsLiteral)
                {
                    continue;
                }
                
                TEnum key = (TEnum)enumField.GetValue(null);
                ProtoEnumAttribute ea = AttributeUtils.GetAttribute<ProtoEnumAttribute>(enumField);
                uint value;
                string name = (ea == null || string.IsNullOrEmpty(ea.Name)) ? enumField.Name : ea.Name;

                if (ea == null || !ea.HasValue())
                {
                    value = (uint)Convert.ChangeType(key, typeof(uint), CultureInfo.InvariantCulture);
                }
                else
                {
                    value = (uint)ea.Value;
                }

                list.Add(new ProtoEnumValue<TEnum>(key, value, name));
            }
            list.Sort(delegate(ProtoEnumValue<TEnum> x, ProtoEnumValue<TEnum> y)
            {
                return x.WireValue.CompareTo(y.WireValue);
            });
            return list;
        }
    }
}
