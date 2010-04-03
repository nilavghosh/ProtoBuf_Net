﻿#if !NO_RUNTIME
using System;
using System.Collections;
using System.Reflection;
using ProtoBuf.Serializers;
using System.Text.RegularExpressions;

namespace ProtoBuf.Meta
{
    /// <summary>
    /// Represents a type at runtime for use with protobuf, allowing the field mappings (etc) to be defined
    /// </summary>
    public class MetaType
    {
        
        private MetaType baseType;
        /// <summary>
        /// Gets the base-type for this type
        /// </summary>
        public MetaType BaseType {
            get { return baseType; }
        }

        private BasicList subTypes;
        /// <summary>
        /// Adds a known sub-type to the inheritance model
        /// </summary>
        public MetaType AddSubType(int fieldNumber, Type derivedType)
        {
            MetaType derivedMeta = model[derivedType];
            SubType subType = new SubType(fieldNumber, derivedMeta);
            ThrowIfFrozen();

            derivedMeta.SetBaseType(this); // includes ThrowIfFrozen
            if (subTypes == null) subTypes = new BasicList();
            subTypes.Add(subType);
            return this;
        }

        private void SetBaseType(MetaType baseType)
        {
            ThrowIfFrozen();
            if (baseType == null) throw new ArgumentNullException("baseType");
            if (this.baseType != null) throw new InvalidOperationException("A type can only participate in one inheritance hierarchy");
            
            MetaType type = baseType;
            while (type != null)
            {
                if (ReferenceEquals(type, this)) throw new InvalidOperationException("Cyclic inheritance is not allowed");
                type = type.baseType;
            }
            this.baseType = baseType;
        }

        private CallbackSet callbacks;
        /// <summary>
        /// Indicates whether the current type has defined callbacks 
        /// </summary>
        public bool HasCallbacks
        {
            get { return callbacks != null && callbacks.NonTrivial; }
        }

        /// <summary>
        /// Indicates whether the current type has defined subtypes
        /// </summary>
        public bool HasSubtypes
        {
            get { return subTypes != null && subTypes.Count != 0; }
        }
        
        /// <summary>
        /// Obtains the subtypes that are defined for the current type
        /// </summary>
        public SubType[] GetSubtypes()
        {
            if (!HasSubtypes) return null;
            SubType[] arr = new SubType[subTypes.Count];
            subTypes.CopyTo(arr, 0);
            return arr;
        }

        /// <summary>
        /// Returns the set of callbacks defined for this type
        /// </summary>
        public CallbackSet Callbacks
        {
            get
            {
                if (callbacks == null) callbacks = new CallbackSet(this);
                return callbacks;
            }
        }
        private readonly RuntimeTypeModel model;
        internal MetaType(RuntimeTypeModel model, Type type)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (type == null) throw new ArgumentNullException("type");
            if (type.IsPrimitive) throw new ArgumentException("Not valid for primitive types", "type");
            this.type = type;
            this.model = model;
        }
        /// <summary>
        /// Throws an exception if the type has been made immutable
        /// </summary>
        protected internal void ThrowIfFrozen()
        {
            if (serializer != null) throw new InvalidOperationException("The type cannot be changed once a serializer has been generated");
        }
        private readonly Type type;
        /// <summary>
        /// The runtime type that the meta-type represents
        /// </summary>
        public Type Type { get { return type; } }
        private IProtoSerializer serializer;
        internal IProtoSerializer Serializer {
            get {
                if (serializer == null) serializer = BuildSerializer();
                return serializer;
            }
        }
        private IProtoSerializer BuildSerializer()
        {
            fields.Trim();
            int fieldCount = fields.Count;
            int subTypeCount = subTypes == null ? 0 : subTypes.Count;
            int[] fieldNumbers = new int[fieldCount + subTypeCount];
            IProtoSerializer[] serializers = new IProtoSerializer[fieldCount + subTypeCount];
            int i = 0;
            if (subTypeCount != 0)
            {
                foreach (SubType subType in subTypes)
                {
                    fieldNumbers[i] = subType.FieldNumber;
                    serializers[i++] = subType.Serializer;
                }
            }
            if (fieldCount != 0)
            {
                foreach (ValueMember member in fields)
                {
                    fieldNumbers[i] = member.FieldNumber;
                    serializers[i++] = member.Serializer;
                }
            }
            return new TypeSerializer(type, fieldNumbers, serializers);
        }

        [Flags]
        enum AttributeFamily
        {
            None = 0, ProtoBuf = 1, DataContractSerialier = 2, XmlSerializer = 4
        }
        internal void ApplyDefaultBehaviour()
        {
            AttributeFamily family = AttributeFamily.None;
            foreach (Attribute attrib in type.GetCustomAttributes(true))
            {
                switch (attrib.GetType().FullName)
                {
                    case "ProtoBuf.ProtoContractAttribute": family |= AttributeFamily.ProtoBuf; break;
                    case "System.Xml.Serialization.XmlTypeAttribute": family |= AttributeFamily.XmlSerializer; break;
                    case "System.Runtime.Serialization.DataContractAttribute": family |= AttributeFamily.DataContractSerialier; break;
                }
            }
            if(family ==  AttributeFamily.None) return; // and you'd like me to do what, exactly?
            
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ValueMember vm = ApplyDefaultBehaviour(family, member);
                if (vm != null)
                {
                    Add(vm);
                }
            }
        }
        private static bool HasFamily(AttributeFamily value, AttributeFamily required)
        {
            return (value & required) == required;
        }
        private ValueMember ApplyDefaultBehaviour(AttributeFamily family, MemberInfo member)
        {
            if (member == null || family == AttributeFamily.None) return null; // nix
            Type effectiveType;
            switch (member.MemberType)
            {
                case MemberTypes.Field:
                    effectiveType = ((FieldInfo)member).FieldType; break;
                case MemberTypes.Property:
                    effectiveType = ((PropertyInfo)member).PropertyType; break;
                default:
                    return null; // nothing doing
            }

            int fieldNumber = 0;
            string name = null;
            bool isRequired = false;
            Type itemType = null;
            Type defaultType = null;
            ResolveListTypes(effectiveType, ref itemType, ref defaultType);
            object[] attribs = member.GetCustomAttributes(true);
            Attribute attrib;
            DataFormat dataFormat = DataFormat.Default;
            bool ignore = false;
            object defaultValue = null;
            // implicit zero default
            switch (Type.GetTypeCode(effectiveType))
            {
                case TypeCode.Boolean: defaultValue = false; break;
                case TypeCode.Decimal: defaultValue = (decimal)0; break;
                case TypeCode.Single: defaultValue = (float)0; break;
                case TypeCode.Double: defaultValue = (double)0; break;
                case TypeCode.Byte: defaultValue = (byte)0;  break;
                case TypeCode.Char: defaultValue = (char)0; break;
                case TypeCode.Int16: defaultValue = (short)0; break;
                case TypeCode.Int32: defaultValue = (int)0; break;
                case TypeCode.Int64: defaultValue = (long)0; break;
                case TypeCode.SByte: defaultValue = (sbyte)0; break;
                case TypeCode.UInt16: defaultValue = (ushort)0; break;
                case TypeCode.UInt32: defaultValue = (uint)0; break;
                case TypeCode.UInt64: defaultValue = (ulong)0; break;
                default:
                    if (effectiveType == typeof(TimeSpan)) defaultValue = TimeSpan.Zero;
                    break;
            }
            if (!ignore && HasFamily(family, AttributeFamily.ProtoBuf))
            {
                attrib = GetAttribute(attribs, "ProtoBuf.ProtoMemberAttribute");
                GetIgnore(ref ignore, attrib, attribs, "ProtoBuf.ProtoIgnoreAttribute");
                if (!ignore)
                {
                    GetFieldNumber(ref fieldNumber, attrib, "Tag");
                    GetFieldName(ref name, attrib, "Name");
                    GetFieldRequired(ref isRequired, attrib, "IsRequired");
                    GetDataFormat(ref dataFormat, attrib, "DataFormat");
                }
                
            }
            if (!ignore && HasFamily(family, AttributeFamily.DataContractSerialier))
            {
                attrib = GetAttribute(attribs, "System.Runtime.Serialization.DataMemberAttribute");
                GetFieldNumber(ref fieldNumber, attrib, "Order");
                GetFieldName(ref name, attrib, "Name");
                GetFieldRequired(ref isRequired, attrib, "IsRequired");
            }
            if (!ignore && HasFamily(family, AttributeFamily.XmlSerializer))
            {
                attrib = GetAttribute(attribs, "System.Xml.Serialization.XmlElementAttribute");
                GetIgnore(ref ignore, attrib, attribs, "ProtoBuf.XmlIgnoreAttribute");
                if (!ignore)
                {
                    GetFieldNumber(ref fieldNumber, attrib, "Order");
                    GetFieldName(ref name, attrib, "ElementName");
                }
                attrib = GetAttribute(attribs, "System.Xml.Serialization.XmlArrayAttribute");
                GetIgnore(ref ignore, attrib, attribs, "ProtoBuf.XmlIgnoreAttribute");
                if (!ignore)
                {
                    GetFieldNumber(ref fieldNumber, attrib, "Order");
                    GetFieldName(ref name, attrib, "ElementName");
                }
            }
            if (!ignore && (attrib = GetAttribute(attribs, "System.ComponentModel.DefaultValueAttribute")) != null)
            {
                defaultValue = GetMemberValue(attrib, "Value");
            }
            return (fieldNumber > 0 && !ignore) ? new ValueMember(model, type, fieldNumber, member, effectiveType, itemType, defaultType, dataFormat, defaultValue)
                    : null;
        }

        private static void GetDataFormat(ref DataFormat value, Attribute attrib, string memberName)
        {
            if ((attrib == null) || (value != DataFormat.Default)) return;
            object obj = GetMemberValue(attrib, memberName);
            if (obj != null) value = (DataFormat)obj;
        }

        private static void GetIgnore(ref bool ignore, Attribute attrib, object[] attribs, string fullName)
        {
            if (ignore || attrib == null) return;
            ignore = GetAttribute(attribs, fullName) != null;
            return;
        }

        private static void GetFieldRequired(ref bool value, Attribute attrib, string memberName)
        {
            if (attrib == null || value) return;
            object obj = GetMemberValue(attrib, memberName);
            if (obj != null) value = (bool)obj;
        }

        private static void GetFieldNumber(ref int value, Attribute attrib, string memberName)
        {
            if (attrib == null || value > 0) return;
            object obj = GetMemberValue(attrib, memberName);
            if (obj != null) value = (int)obj;
        }
        private static void GetFieldName(ref string name, Attribute attrib, string memberName)
        {
            if (attrib == null || !Helpers.IsNullOrEmpty(name)) return;
            object obj = GetMemberValue(attrib, memberName);
            if (obj != null) name = (string)obj;
        }

        private static object GetMemberValue(Attribute attrib, string memberName)
        {
            MemberInfo[] members = attrib.GetType().GetMember(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (members.Length != 1) return null;
            switch (members[0].MemberType)
            {
                case MemberTypes.Property:
                    return ((PropertyInfo)members[0]).GetValue(attrib, null);
                case MemberTypes.Field:
                    return ((FieldInfo)members[0]).GetValue(attrib);
                case MemberTypes.Method:
                    return ((MethodInfo)members[0]).Invoke(attrib, null);
            }
            return null;
        }

        private static Attribute GetAttribute(object[] attribs, string fullName)
        {
            for (int i = 0; i < attribs.Length; i++)
            {
                Attribute attrib = attribs[i] as Attribute;
                if (attrib != null && attrib.GetType().FullName == fullName) return attrib;
            }
            return null;
        }
        /// <summary>
        /// Adds a member (by name) to the MetaType
        /// </summary>        
        public MetaType Add(int fieldNumber, string memberName)
        {
            return Add(fieldNumber, memberName, null, null, null);
        }
        /// <summary>
        /// Adds a member (by name) to the MetaType
        /// </summary>        
        public MetaType Add(int fieldNumber, string memberName, object defaultValue)
        {
            return Add(fieldNumber, memberName, null, null, defaultValue);
        }

        private static void ResolveListTypes(Type type, ref Type itemType, ref Type defaultType) {
            if (type == null) return;
            // handle arrays
            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1)
                {
                    throw new NotSupportedException("Multi-dimension arrays are supported");
                }
                itemType = type.GetElementType();
                if (itemType == typeof(byte)) {
                    defaultType = itemType = null;
                } else {
                    defaultType = type.MakeArrayType();
                }
            }
            // handle lists
            if (itemType == null) { itemType = ListDecorator.GetItemType(type); }

            // check for nested data (not allowed)
            if (itemType != null)
            {
                Type nestedItemType = null, nestedDefaultType = null;
                ResolveListTypes(itemType, ref nestedItemType, ref nestedDefaultType);
                if (nestedItemType != null)
                {
                    throw new NotSupportedException("Nested or jagged lists and arrays are not supported");
                }
            }

            if (itemType != null && defaultType == null)
            {
                if (type.IsClass && !type.IsAbstract && type.GetConstructor(Helpers.EmptyTypes) != null)
                {
                    defaultType = type;
                }
                if (defaultType == null)
                {
                    if (type.IsInterface)
                    {
                        defaultType = typeof(System.Collections.Generic.List<>).MakeGenericType(itemType);
                    }
                }
                // verify that the default type is appropriate
                if (defaultType != null && !type.IsAssignableFrom(defaultType)) defaultType = null;
            }
        }
        /// <summary>
        /// Adds a member (by name) to the MetaType, including an itemType and defaultType for representing lists
        /// </summary>
        public MetaType Add(int fieldNumber, string memberName, Type itemType, Type defaultType)
        {
            return Add(fieldNumber, memberName, itemType, defaultType, null);
        }
        private MetaType Add(int fieldNumber, string memberName, Type itemType, Type defaultType, object defaultValue)
        {
            MemberInfo[] members = type.GetMember(memberName);
            if(members == null || members.Length != 1) throw new ArgumentException("Unable to determine member: " + memberName, "memberName");
            MemberInfo mi = members[0];
            Type miType;
            switch (mi.MemberType)
            {
                case MemberTypes.Field:
                    miType = ((FieldInfo)mi).FieldType; break;
                case MemberTypes.Property:
                    miType = ((PropertyInfo)mi).PropertyType; break;
                default:
                    throw new NotSupportedException();
            }

            ResolveListTypes(miType, ref itemType, ref defaultType);
            Add(new ValueMember(model, type, fieldNumber, mi, miType, itemType, defaultType, DataFormat.Default, defaultValue));
            return this;
        } 
        private void Add(ValueMember member) {
            lock (fields)
            {
                ThrowIfFrozen();
                fields.Add(member);
            }
        }
        /// <summary>
        /// Returns the ValueMember that matchs a given field number, or null if not found
        /// </summary>
        public ValueMember this[int fieldNumber]
        {
            get
            {
                foreach (ValueMember member in fields)
                {
                    if (member.FieldNumber == fieldNumber) return member;
                }
                return null;
            }
        }
        private readonly BasicList fields = new BasicList();

        //IEnumerable GetFields() { return fields; }

#if FEAT_COMPILER && !FX11
        internal void CompileInPlace()
        {
            serializer = CompiledSerializer.Wrap(Serializer);
        }
#endif

        internal bool IsDefined(int fieldNumber)
        {
            foreach (ValueMember field in fields)
            {
                if (field.FieldNumber == fieldNumber) return true;
            }
            return false;
        }

        internal int GetKey(bool demand, bool getBaseKey)
        {
            return model.GetKey(type, demand, getBaseKey);
        }
    }
}
#endif