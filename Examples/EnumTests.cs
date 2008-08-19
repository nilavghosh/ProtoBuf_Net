﻿using ProtoBuf;
using System.IO;
using System;
using NUnit.Framework;
using System.ComponentModel;

namespace Examples.DesignIdeas
{
    /// <summary>
    /// would like to be able to specify custom values for enums;
    /// implementation note: some kind of map: Dictionary<TValue, long>?
    /// note: how to handle -ves? (ArgumentOutOfRangeException?)
    /// note: how to handle flags? (NotSupportedException? at least for now?
    ///             could later use a bitmap sweep?)
    /// </summary>
    [ProtoContract(Name="blah")]
    enum SomeEnum
    {
        [ProtoEnum(Name="FOO")]
        ChangeName = 3,

        [ProtoEnum(Value = 19)]
        ChangeValue = 5,

        [ProtoEnum(Name="BAR", Value=92)]
        ChangeBoth = 7,
        
        LeaveAlone = 22,


        Default = 2
    }
    [ProtoContract]
    class EnumFoo
    {
        public EnumFoo() { Bar = SomeEnum.Default; }
        [ProtoMember(1), DefaultValue(SomeEnum.Default)]
        public SomeEnum Bar { get; set; }
    }



    public enum HasConflictingKeys
    {
        Foo = 0,
        Bar = 0
    }
    public enum HasConflictingValues
    {
        [ProtoEnum(Value=2)]
        Foo = 0,
        [ProtoEnum(Value = 2)]
        Bar = 1
    }
    [ProtoContract]
    class TypeDuffKeys
    {
        [ProtoMember(1)]
        public HasConflictingKeys Value {get;set;}
    }
    [ProtoContract]
    class TypeDuffValues
    {
        [ProtoMember(1)]
        public HasConflictingValues Value {get;set;}
    }

    [TestFixture]
    public class EnumTests
    {

        [Test]
        public void EnumGeneration()
        {
            string proto = Serializer.GetProto<EnumFoo>();
            Assert.AreEqual(@"package Examples.DesignIdeas;

message EnumFoo {
   optional blah Bar = 1 [default = Default];
}
enum SomeEnum {
   Default = 2;
   FOO = 3;
   ChangeValue = 19;
   LeaveAlone = 22;
   BAR = 92;
}
", proto);
        }

        [Test, ExpectedException(typeof(ProtoException))]
        public void TestConflictingKeys()
        {
            Serializer.Serialize(Stream.Null, new TypeDuffKeys { Value = HasConflictingKeys.Foo });
        }

        [Test, ExpectedException(typeof(ProtoException))]
        public void TestConflictingValues()
        {
            Serializer.Serialize(Stream.Null, new TypeDuffValues { Value = HasConflictingValues.Foo });
        }

        [Test]
        public void TestEnumNameValueMapped()
        {
            CheckValue(SomeEnum.ChangeBoth, 0x08, 92);
        }
        [Test]
        public void TestEnumNameMapped() {
            CheckValue(SomeEnum.ChangeName, 0x08, 03);
        }
        [Test]
        public void TestEnumValueMapped() {
            CheckValue(SomeEnum.ChangeValue, 0x08, 19);
        }
        [Test]
        public void TestEnumNoMap() {
            CheckValue(SomeEnum.LeaveAlone, 0x08, 22);
        }

        static void CheckValue(SomeEnum val, params byte[] expected)
        {
            EnumFoo foo = new EnumFoo { Bar = val };
            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, foo);
                ms.Position = 0;
                byte[] buffer = ms.ToArray();
                Assert.IsTrue(Program.ArraysEqual(buffer, expected), "Byte mismatch");

                EnumFoo clone = Serializer.Deserialize<EnumFoo>(ms);
                Assert.AreEqual(val, clone.Bar);
            }
        }
    }
}
