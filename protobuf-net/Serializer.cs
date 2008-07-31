﻿using System;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
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
        internal static bool TryGetTag(PropertyInfo property, out int tag, out string name, out DataFormat format, out bool isRequired, out bool isGroup)
        {
            name = property.Name;
            format = DataFormat.Default;
            tag = -1;
            isRequired = isGroup = false;
            ProtoMemberAttribute pm = AttributeUtils.GetAttribute<ProtoMemberAttribute>(property);
            if (pm != null)
            {
                format = pm.DataFormat;
                if (!string.IsNullOrEmpty(pm.Name)) name = pm.Name;
                tag = pm.Tag;
                isRequired = pm.IsRequired;
                isGroup = pm.IsGroup;
                return tag > 0;
            }
#if NET_3_0
            DataMemberAttribute dm = AttributeUtils.GetAttribute<DataMemberAttribute>(property);
            if (dm != null)
            {
                if (!string.IsNullOrEmpty(dm.Name)) name = dm.Name;
                tag = dm.Order;
                isRequired = dm.IsRequired;
                return tag > 0;
            }
#endif
            
            XmlElementAttribute xe = AttributeUtils.GetAttribute<XmlElementAttribute>(property);
            if (xe != null)
            {
                if (!string.IsNullOrEmpty(xe.ElementName)) name = xe.ElementName;
                tag = xe.Order;
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
        public static T Deserialize<T>(Stream source) where T : class, new()
        {
            T instance = new T();
            try
            {
                Serializer<T>.Deserialize(instance, source);
            }
            catch (Exception ex)
            {
                ThrowInner(ex);
                throw; // if no inner (preserves stacktrace)
            }
            return instance;
        }

        /// <summary>
        /// Applies a protocol-buffer stream to an existing instance.
        /// </summary>
        /// <typeparam name="T">The type being merged.</typeparam>
        /// <param name="instance">The existing instance to be modified (cannot be null).</param>
        /// <param name="source">The binary stream to apply to the instance (cannot be null).</param>
        public static void Merge<T>(Stream source, T instance) where T : class, new()
        {
            try
            {
                Serializer<T>.Deserialize(instance, source);
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
        public static void Serialize<T>(Stream destination, T instance) where T : class, new()
        {
            try
            {
                Serializer<T>.Serialize(instance, destination);
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
        /// Writes a protocol-buffer representation of the given instance to the supplied SerializationInfo.
        /// </summary>
        /// <typeparam name="T">The type being serialized.</typeparam>
        /// <param name="instance">The existing instance to be serialized (cannot be null).</param>
        /// <param name="info">The destination SerializationInfo to write to.</param>
        public static void Serialize<T>(SerializationInfo info, T instance) where T : class, ISerializable, new()
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
        public static void Merge<T>(SerializationInfo info, T instance) where T : class, ISerializable, new()
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
        /// Create a deep clone of the supplied instance; any sub-items are also cloned.
        /// </summary>
        /// <typeparam name="T">The type being cloned.</typeparam>
        /// <param name="instance">The existing instance to be cloned.</param>
        /// <returns>A new copy, cloned from the supplied instance.</returns>
        public static T DeepClone<T>(T instance) where T : class, new()
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
            where TOldType : class, new()
            where TNewType : class, new()
        {
            return ChangeType<TOldType, TNewType>(instance, null);
        }

        /// <summary>
        /// As per the public ChangeType, but allows for workspace-sharing to reduce buffer overhead.
        /// </summary>
        internal static TNewType ChangeType<TOldType, TNewType>(TOldType instance, SerializationContext context)
            where TOldType : class, new()
            where TNewType : class, new()
        {
            if (instance == null)
            {
                return null; // GIGO
            } 

            using (MemoryStream ms = new MemoryStream())
            {
                SerializationContext tmpCtx = new SerializationContext(ms);
                if (context != null)
                {
                    tmpCtx.ReadFrom(context);
                }

                Serialize<TOldType>(ms, instance);
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

        internal static int WriteFieldToken(int tag, WireType wireType, SerializationContext context)
        {
            int prefix = (tag << 3) | ((int)wireType & 7);
            return TwosComplementSerializer.WriteToStream(prefix, context);
        }
    }
}
