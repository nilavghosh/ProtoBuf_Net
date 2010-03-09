﻿#if !NO_RUNTIME
using System;
using System.Reflection;



namespace ProtoBuf.Serializers
{
    sealed class PropertyDecorator : ProtoDecoratorBase
    {
        public override Type ExpectedType { get { return property.DeclaringType; } }
        private readonly PropertyInfo property;
        public override bool RequiresOldValue { get { return true; } }
        public override bool ReturnsValue { get { return false; } }
        private readonly bool readOptionsWriteValue, readOptionsRefCheck;
        public PropertyDecorator(PropertyInfo property, IProtoSerializer tail) : base(tail)
        {
            this.property = property;
            SanityCheck(property, tail, out readOptionsWriteValue, out readOptionsRefCheck, true);
        }
        private static void SanityCheck(PropertyInfo property, IProtoSerializer tail, out bool writeValue, out bool refCheck, bool nonPublic) {
            if(property == null) throw new ArgumentNullException("property");
            
            writeValue = tail.ReturnsValue && property.CanWrite && property.GetSetMethod(nonPublic) != null;
            if (!property.CanRead || property.GetGetMethod(nonPublic) == null) throw new InvalidOperationException("Cannot serialize property without a get accessor");
            refCheck = writeValue && tail.RequiresOldValue && !tail.ExpectedType.IsValueType;
            if (!tail.RequiresOldValue && !writeValue)
            { // so we can't save the value, and the tail doesn't use it either... not helpful
                throw new InvalidOperationException("Cannot apply changes to property");
            }

            if (!writeValue && tail.ExpectedType.IsValueType)
            { // can't write the value, so the struct value will be lost
                throw new InvalidOperationException("Cannot apply changes to property");
            }
        }
        public override void Write(object value, ProtoWriter dest)
        {
            Helpers.DebugAssert(value != null);
            value = property.GetValue(value, null);
            if(value != null) Tail.Write(value, dest);
        }
        public override object Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value != null);

            object oldVal = Tail.RequiresOldValue ? property.GetValue(value, null) : null;
            object newVal = Tail.Read(oldVal, source);
            if (readOptionsRefCheck)
            {
                if (oldVal == null)
                {
                    property.SetValue(value, newVal, null);
                }
            }
            else if (readOptionsWriteValue)
            {
                property.SetValue(value, newVal, null);
            }
            return null;
        }
#if FEAT_COMPILER
        protected override void EmitWrite(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.LoadAddress(valueFrom, ExpectedType);
            ctx.LoadValue(property);
            ctx.WriteNullCheckedTail(property.PropertyType, Tail, null);
        }
        protected override void EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {

            bool writeValue, refCheck;
            SanityCheck(property, Tail, out writeValue, out refCheck, ctx.NonPublic);
            if (ExpectedType.IsValueType && valueFrom == null)
            {
                throw new InvalidOperationException("Attempt to mutate struct on the head of the stack; changes would be lost");
            }

            ctx.LoadAddress(valueFrom, ExpectedType); // stack is: old-addr
            if (writeValue && Tail.RequiresOldValue)
            { // need to read and write
                ctx.CopyValue();
            }
            // stack is: [old-addr]|old-addr
            if (Tail.RequiresOldValue)
            {
                ctx.LoadValue(property); // stack is: [old-addr]|old-value
            }
            using (Compiler.Local oldAddr = refCheck ? new Compiler.Local(ctx, typeof(object)) : null)
            {
                if (refCheck)
                {
                    ctx.CopyValue(); // stack is: old-addr|old-value|old-value
                    ctx.StoreValue(oldAddr); // stack is old-addr|old-value
                }
                ctx.ReadNullCheckedTail(property.PropertyType, Tail, null); // stack is [old-addr]|[new-value]
                if (refCheck)
                {
                    ctx.LoadValue(oldAddr); // stack is old-addr|new-value|old-value
                    Compiler.CodeLabel noChange = ctx.DefineLabel(), allDone = ctx.DefineLabel();
                    ctx.BranchIfTrue(noChange, true); // stack is old-addr|new-value
                    ctx.StoreValue(property); // stack is clean
                    ctx.Branch(allDone, true);
                    ctx.MarkLabel(noChange);
                    ctx.DiscardValue(); // stack is old-addr
                    ctx.DiscardValue(); // stack is clean
                    ctx.MarkLabel(allDone);

                }
                else if (writeValue)
                {   // stack is old-addr|new-value
                    ctx.StoreValue(property);
                }
                else
                { // don't want return value; drop it if anything there
                    // stack is [new-value]
                    if (Tail.ReturnsValue) { ctx.DiscardValue(); }
                }
            }
        }
#endif
    }
}
#endif