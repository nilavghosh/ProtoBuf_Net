﻿using NUnit.Framework;
using System.Collections.Generic;
using ProtoBuf;
using System;

namespace Examples.Dictionary
{
    [ProtoContract]
    class DataWithDictionary<T>
    {
        public DataWithDictionary()
        {
            Data = new Dictionary<int, T>();
        }
        [ProtoMember(1)]
        public IDictionary<int, T> Data { get; private set; }
    }

    [ProtoContract]
    class SimpleData : IEquatable<SimpleData>
    {
        private SimpleData() {}
        public SimpleData(int value) {
            Value = value;}

        [ProtoMember(1)]
        public int Value { get; set; }

        public bool Equals(SimpleData other)
        {
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
        public override bool Equals(object other)
        {
            return Equals(other as SimpleData);
        }
    }

    [TestFixture]
    public class DictionaryTests
    {
        [Test]
        public void TestNestedDictionaryWithStrings()
        {
            var obj = new DataWithDictionary<string>();
            obj.Data[0] = "abc";
            obj.Data[4] = "def";
            obj.Data[7] = "abc";

            var clone = Serializer.DeepClone(obj);
            Assert.AreNotSame(obj,clone);
            AssertEqual(obj.Data, clone.Data);
        }

        [Test]
        public void TestNestedDictionaryWithSimpleData()
        {
            var obj = new DataWithDictionary<SimpleData>();
            obj.Data[0] = new SimpleData(5);
            obj.Data[4] = new SimpleData(72);
            obj.Data[7] = new SimpleData(72);

            var clone = Serializer.DeepClone(obj);
            Assert.AreNotSame(obj, clone);
            AssertEqual(obj.Data, clone.Data);
        }

        [Test]
        public void RoundtripDictionary()
        {
            var lookup = new Dictionary<int,string>();
            lookup[0] = "abc";
            lookup[4] = "def";
            lookup[7] = "abc";

            var clone = Serializer.DeepClone(lookup);
            
            AssertEqual(lookup, clone);
        }
        static void AssertEqual<TKey, TValue>(
            IDictionary<TKey, TValue> expected,
            IDictionary<TKey, TValue> actual)
        {
            Assert.AreNotSame(expected, actual, "Instance");
            Assert.AreEqual(expected.Count, actual.Count, "Count");
            foreach (var pair in expected)
            {
                TValue value;
                Assert.IsTrue(actual.TryGetValue(pair.Key, out value), "Missing: " + pair.Key);
                Assert.AreEqual(pair.Value, value, "Value: " + pair.Key);
            }
        }
    }

    [TestFixture]
    public class NestedDictionaryTests {

        [Test]
        public void TestNestedConcreteConcreteDictionary()
        {
            Dictionary<string, Dictionary<string, String>> data = new Dictionary<string, Dictionary<string, string>>
            {
                { "abc", new Dictionary<string,string> {{"def","ghi"}}},
                { "jkl", new Dictionary<string,string> {{"mno","pqr"},{"stu","vwx"}}}
            };
            CheckNested(data, "original");
            var clone = Serializer.DeepClone(data);
            CheckNested(clone, "clone");
        }

        [Test]
        public void TestNestedInterfaceInterfaceDictionary()
        {
            IDictionary<string, IDictionary<string, String>> data = new Dictionary<string, IDictionary<string, string>>
            {
                { "abc", new Dictionary<string,string> {{"def","ghi"}}},
                { "jkl", new Dictionary<string,string> {{"mno","pqr"},{"stu","vwx"}}}
            };
            CheckNested(data, "original");
            var clone = Serializer.DeepClone(data);
            CheckNested(clone, "clone");
        }

        [Test]
        public void TestNestedInterfaceConcreteDictionary()
        {
            IDictionary<string, Dictionary<string, String>> data = new Dictionary<string, Dictionary<string, string>>
            {
                { "abc", new Dictionary<string,string> {{"def","ghi"}}},
                { "jkl", new Dictionary<string,string> {{"mno","pqr"},{"stu","vwx"}}}
            };
            CheckNested(data, "original");
            var clone = Serializer.DeepClone(data);
            CheckNested(clone, "clone");
        }
        [Test]
        public void TestNestedConcreteInterfaceDictionary()
        {
            Dictionary<string, IDictionary<string, String>> data = new Dictionary<string, IDictionary<string, string>>
            {
                { "abc", new Dictionary<string,string> {{"def","ghi"}}},
                { "jkl", new Dictionary<string,string> {{"mno","pqr"},{"stu","vwx"}}}
            };
            CheckNested(data, "original");
            var clone = Serializer.DeepClone(data);
            CheckNested(clone, "clone");
        }

        static void CheckNested<TInner>(IDictionary<string, TInner> data, string message)
            where TInner : IDictionary<string, string>
        {
            Assert.IsNotNull(data, message);
            Assert.AreEqual(2, data.Keys.Count, message);
            var inner = data["abc"];
            Assert.AreEqual(1, inner.Keys.Count, message);
            Assert.AreEqual(inner["def"], "ghi", message);
            inner = data["jkl"];
            Assert.AreEqual(2, inner.Keys.Count, message);
            Assert.AreEqual(inner["mno"], "pqr", message);
            Assert.AreEqual(inner["stu"], "vwx", message);

        }
    }
}
