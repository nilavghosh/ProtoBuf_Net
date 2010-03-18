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
        protected void ThrowIfFrozen()
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
            int count = fields.Count;
            int[] fieldNumbers = new int[count];
            IProtoSerializer[] serializers = new IProtoSerializer[count];
            int i = 0;
            foreach(ValueMember member in fields) {
                fieldNumbers[i] = member.FieldNumber;
                serializers[i++] = member.Serializer;
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
            return (fieldNumber > 0 && !ignore) ? new ValueMember(model, type, fieldNumber, member, effectiveType, itemType, defaultType, dataFormat)
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
            return Add(fieldNumber, memberName, null, null);
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
            Add(new ValueMember(model, type, fieldNumber, mi, miType, itemType, defaultType, DataFormat.Default));
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
    }
}
#endif