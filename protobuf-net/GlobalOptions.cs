﻿using System;
using System.Collections.Generic;
using System.Text;
using ProtoBuf.Property;

namespace ProtoBuf
{
    public static partial class Serializer
    {
        /// <summary>
        /// Global switches that change the behavior of protobuf-net
        /// </summary>
        public static class GlobalOptions
        {
            #if NET_3_0

            private static bool inferTagFromName;
            /// <summary>
            /// Global default for that
            /// enables/disables automatic tag generation based on the existing name / order
            /// of the defined members. See <seealso cref="ProtoContractAttribute.InferTagFromName"/>
            /// for usage and <b>important warning</b> / explanation.
            /// You must set the global default before attempting to serialize/deserialize any
            /// impacted type.
            /// </summary>
            public static bool InferTagFromName
            {
                get { return inferTagFromName; }
                set { inferTagFromName = value; }
            }

            #endif
        }

        internal static bool CanSerialize(Type type)
        {
            if(type == null) throw new ArgumentNullException("type");
            if(type.IsValueType) return false;

            // serialize as item?
            if (IsEntityType(type)) return true;

            // serialize as list?
            bool enumOnly;
            Type itemType = PropertyFactory.GetListType(type, out enumOnly);
            if (itemType != null
                && (!enumOnly || Serializer.HasAddMethod(type, itemType))
                && Serializer.IsEntityType(itemType)) return true;
            return false;
        }

        internal static bool HasAddMethod(Type list, Type item)
        {
            return list.GetMethod("Add", new Type[] { item }) != null;
        }
    }
}
