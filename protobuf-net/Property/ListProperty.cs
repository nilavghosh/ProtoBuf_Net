﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProtoBuf
{
    internal sealed class ListProperty<TEntity, TList, TValue> : PropertyBase<TEntity, TList>, IGroupProperty<TEntity>
        where TEntity : class, new()
        where TList : IList<TValue>
    {
        public ListProperty(PropertyInfo property)
            : base(property)
        {
            serializer = GetSerializer<TValue>(property);
        }

        private readonly ISerializer<TValue> serializer;

        public override string DefinedType { get { return serializer.DefinedType; } }
        public override WireType WireType { get { return serializer.WireType; } }
        public override Type PropertyType { get { return typeof(TValue); } }
        public override bool IsRepeated { get { return true; } }

        public override int Serialize(TEntity instance, SerializationContext context)
        { // write all items in a contiguous block
            TList list = GetValue(instance);
            int total = 0;
            if (list != null && list.Count > 0)
            {
                foreach (TValue value in list)
                {
                    total += Serialize(value, serializer, context);
                }
            }

            return total;
        }
        public override int GetLength(TEntity instance, SerializationContext context)
        {
            TList list = GetValue(instance);
            int total = 0;
            if (list != null && list.Count > 0)
            {
                foreach (TValue value in list)
                {
                    total += GetLength(value, serializer, context);
                }
            }
            return total;
        }
        private void AddItem(TEntity instance, TValue value)
        {
            TList list = GetValue(instance);
            bool set = list == null;
            if (set) list = (TList)Activator.CreateInstance(typeof(TList));
            list.Add(value);
            if (set) SetValue(instance, list);
        }
        public override void Deserialize(TEntity instance, SerializationContext context)
        {   // read a single item
            TValue value = serializer.Deserialize(default(TValue), context);
            AddItem(instance, value);
            Trace(true, value, context);
        }

        public void DeserializeGroup(TEntity instance, SerializationContext context)
        {
            // the list could be of anything... need to check if the serializer
            // supports group usage (i.e. entities)
            IGroupSerializer<TValue> groupSerializer = serializer as IGroupSerializer<TValue>;
            if (groupSerializer == null)
            {
                throw new ProtoException("Cannot treat property as a group: " + Name);
            }
            // read a single item
            TValue value = groupSerializer.DeserializeGroup(default(TValue), context);
            AddItem(instance, value);
            Trace(true, value, context);
        }
    }
}
