﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using ProtoBuf;
using System.IO;
using Examples.SimpleStream;
using DAL;

namespace Examples
{
    class Program
    {
        static void Main() {
            SimpleStreamDemo demo = new SimpleStreamDemo();
            const int COUNT = 1000000;
            demo.PerfTestSimple(COUNT, true);
            demo.PerfTestString(COUNT, true);
            demo.PerfTestEmbedded(COUNT, true);
            //demo.PerfTestEnum(COUNT, true);
            const int NWIND_COUNT = 500;
            DAL.Database db = DAL.NWindTests.LoadDatabaseFromFile();
            Console.WriteLine("Using groups: {0}", DAL.Database.MASTER_GROUP);
            SimpleStreamDemo.LoadTestItem(db, NWIND_COUNT, NWIND_COUNT, false, false, false, true, false, null);

            DatabaseCompat compat = Serializer.ChangeType<Database, DatabaseCompat>(db);
            SimpleStreamDemo.LoadTestItem(compat, NWIND_COUNT, NWIND_COUNT, true, false, true, true, false, null);

            DatabaseCompatRem compatRem = Serializer.ChangeType<Database, DatabaseCompatRem>(db);
            SimpleStreamDemo.LoadTestItem(compatRem, NWIND_COUNT, NWIND_COUNT, true, false, true, true, false, null);
            
        }

        public static string GetByteString(byte[] buffer)
        {
            if (buffer == null) return "[null]";
            if (buffer.Length == 0) return "[empty]";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < buffer.Length; i++)
            {
                sb.Append(buffer[i].ToString("X2")).Append(' ');
            }
            sb.Length -= 1;
            return sb.ToString();
        }
        public static string GetByteString<T>(T item) where T : class,new()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, item);
                byte[] actual = ms.ToArray();
                return GetByteString(actual);
            }
        }
        public static bool CheckBytes<T>(T item, params byte[] expected) where T : class, new()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, item);
                byte[] actual = ms.ToArray();
                bool equal = Program.ArraysEqual(actual, expected);
                if (!equal)
                {
                    string exp = GetByteString(expected), act = GetByteString(actual);
                    Console.WriteLine("Expected: {0}", exp);
                    Console.WriteLine("Actual: {0}", act);
                }
                return equal;
            }
        }
        public static T Build<T>(params byte[] raw) where T : class, new()
        {
            using (MemoryStream ms = new MemoryStream(raw))
            {
                return Serializer.Deserialize<T>(ms);
            }
        }
        public static bool ArraysEqual(byte[] actual, byte[] expected)
        {
            if (ReferenceEquals(actual, expected)) return true;
            if (actual == null || expected == null) return false;
            if (actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i]) return false;
            }
            return true;
        }

    }
}
