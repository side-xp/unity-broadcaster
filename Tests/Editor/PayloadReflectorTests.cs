#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !BROADCASTER_MONITOR_OFF
#define BROADCASTER_MONITOR
#endif

#if BROADCASTER_MONITOR
using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Tests for the payload snapshot reflector: which members it captures, how it stringifies each value shape (primitives, strings,
    /// Unity objects, collections, nested complex values, nulls), the hard truncation caps, the <c>ToString()</c> summary line, the
    /// <c>[Event(OmitSnapshot = true)]</c> opt-out, and its promise never to throw. Pure C#, no bus involved.
    /// </summary>
    public class PayloadReflectorTests
    {

        #region Helpers

        /// <summary>Returns the captured value of a field/property by name, failing the test if it wasn't captured.</summary>
        private static string ValueOf(PayloadSnapshot snapshot, string name)
        {
            foreach (PayloadField field in snapshot.Fields)
            {
                if (field.Name == name)
                    return field.Value;
            }
            Assert.Fail($"Expected a captured member named '{name}', found none. Snapshot: {snapshot}");
            return null;
        }

        /// <summary>Whether the snapshot captured a member with the given name.</summary>
        private static bool HasField(PayloadSnapshot snapshot, string name)
        {
            foreach (PayloadField field in snapshot.Fields)
            {
                if (field.Name == name)
                    return true;
            }
            return false;
        }

        #endregion


        #region Scalars

        [Test]
        public void Capture_Primitives_StringifiedInvariantly()
        {
            Scalars value = new Scalars { Number = 42, Ratio = 1.5f, Flag = true, Letter = 'x', Choice = Choice.Second };
            PayloadSnapshot snapshot = PayloadReflector.Capture(value);

            Assert.AreEqual("42", ValueOf(snapshot, nameof(Scalars.Number)));
            Assert.AreEqual("1.5", ValueOf(snapshot, nameof(Scalars.Ratio)), "Floating point uses the invariant culture (dot, not comma).");
            Assert.AreEqual("True", ValueOf(snapshot, nameof(Scalars.Flag)));
            Assert.AreEqual("x", ValueOf(snapshot, nameof(Scalars.Letter)));
            Assert.AreEqual("Second", ValueOf(snapshot, nameof(Scalars.Choice)), "An enum renders as its name, not its numeric value.");
        }

        [Test]
        public void Capture_String_IsQuoted()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasString { Text = "hello" });
            Assert.AreEqual("\"hello\"", ValueOf(snapshot, nameof(HasString.Text)));
        }

        [Test]
        public void Capture_NullString_RendersAsNull()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasString { Text = null });
            Assert.AreEqual("null", ValueOf(snapshot, nameof(HasString.Text)));
        }

        [Test]
        public void Capture_NullReference_RendersAsNull()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasReference { Target = null });
            Assert.AreEqual("null", ValueOf(snapshot, nameof(HasReference.Target)));
        }

        [Test]
        public void Capture_LongString_TruncatedAndQuoted()
        {
            string longText = new string('a', 500);
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasString { Text = longText });

            string value = ValueOf(snapshot, nameof(HasString.Text));
            Assert.IsTrue(value.StartsWith("\""), "The value is still quoted.");
            Assert.IsTrue(value.EndsWith("…\""), "A truncated string ends with an ellipsis inside the closing quote.");
            // 128 kept chars + 1 ellipsis + 2 quotes.
            Assert.AreEqual(131, value.Length, "The value is truncated to the hard cap.");
        }

        #endregion


        #region Unity objects

        [Test]
        public void Capture_UnityObject_RendersNameAndType()
        {
            DummyAsset asset = ScriptableObject.CreateInstance<DummyAsset>();
            asset.name = "Hero";
            try
            {
                PayloadSnapshot snapshot = PayloadReflector.Capture(new HasReference { Target = asset });
                Assert.AreEqual("Hero (DummyAsset)", ValueOf(snapshot, nameof(HasReference.Target)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Capture_DestroyedUnityObject_RendersNullWithType()
        {
            DummyAsset asset = ScriptableObject.CreateInstance<DummyAsset>();
            asset.name = "Hero";
            UnityEngine.Object.DestroyImmediate(asset);

            // The field still holds the (now fake-null) reference; the reflector must not touch its members.
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasReference { Target = asset });
            Assert.AreEqual("null (DummyAsset)", ValueOf(snapshot, nameof(HasReference.Target)));
        }

        #endregion


        #region Collections

        [Test]
        public void Capture_List_RendersElementsAndCount()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasList { Values = new List<int> { 1, 2, 3 } });
            Assert.AreEqual("[1, 2, 3] (3)", ValueOf(snapshot, nameof(HasList.Values)));
        }

        [Test]
        public void Capture_EmptyList_RendersEmptyWithZeroCount()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasList { Values = new List<int>() });
            Assert.AreEqual("[] (0)", ValueOf(snapshot, nameof(HasList.Values)));
        }

        [Test]
        public void Capture_LongList_TruncatesElementsButKeepsExactCount()
        {
            List<int> values = new List<int>();
            for (int i = 0; i < 12; i++)
                values.Add(i);

            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasList { Values = values });
            // Eight elements shown, ellipsis, exact count from ICollection.
            Assert.AreEqual("[0, 1, 2, 3, 4, 5, 6, 7, …] (12)", ValueOf(snapshot, nameof(HasList.Values)));
        }

        [Test]
        public void Capture_NonCollectionEnumerable_CountsWhatItSees()
        {
            // A lazy sequence isn't an ICollection, so the count is what the reflector actually enumerated.
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasSequence());
            Assert.AreEqual("[0, 1, 2] (3)", ValueOf(snapshot, nameof(HasSequence.Numbers)));
        }

        [Test]
        public void Capture_CollectionOfComplexElements_RendersElementsShallow()
        {
            HasBareList value = new HasBareList { Points = new List<Bare> { new Bare { X = 1 }, new Bare { X = 2 } } };
            PayloadSnapshot snapshot = PayloadReflector.Capture(value);
            // A collection element with no custom ToString renders by its type name only (capture stays one level deep).
            Assert.AreEqual("[{Bare}, {Bare}] (2)", ValueOf(snapshot, nameof(HasBareList.Points)));
        }

        #endregion


        #region Nested complex values

        [Test]
        public void Capture_NestedWithoutToString_RendersTypeNameOnly()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasNested { Bare = new Bare { X = 7 } });
            Assert.AreEqual("{Bare}", ValueOf(snapshot, nameof(HasNested.Bare)), "A nested value's fields are never expanded (depth 1).");
        }

        [Test]
        public void Capture_NestedWithToString_UsesItsToString()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasVec { Position = new Vec { X = 1, Y = 2 } });
            Assert.AreEqual("(1,2)", ValueOf(snapshot, nameof(HasVec.Position)));
        }

        #endregion


        #region ToString summary

        [Test]
        public void Capture_TypeWithToStringOverride_SetsSummary()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new Described { Value = 5 });
            Assert.AreEqual("Described=5", snapshot.Summary);
        }

        [Test]
        public void Capture_StructWithoutOverride_HasNoSummary()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new Scalars { Number = 1 });
            Assert.IsNull(snapshot.Summary, "The default ValueType.ToString is noise, never captured as a summary.");
        }

        [Test]
        public void Capture_ClassWithoutOverride_HasNoSummary()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasString { Text = "x" });
            Assert.IsNull(snapshot.Summary, "The default object.ToString is noise, never captured as a summary.");
        }

        [Test]
        public void Capture_TypeWithToStringOverride_AlsoCapturesFields()
        {
            // The summary is captured *in addition* to the fields, not instead of them.
            PayloadSnapshot snapshot = PayloadReflector.Capture(new Described { Value = 5 });
            Assert.AreEqual("Described=5", snapshot.Summary);
            Assert.AreEqual("5", ValueOf(snapshot, nameof(Described.Value)));
        }

        #endregion


        #region Members captured

        [Test]
        public void Capture_ReadableProperty_IsCaptured()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasProperties { Readable = 3 });
            Assert.AreEqual("3", ValueOf(snapshot, nameof(HasProperties.Readable)));
        }

        [Test]
        public void Capture_WriteOnlyAndIndexer_AreSkipped()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasProperties { Readable = 3 });
            Assert.IsFalse(HasField(snapshot, "WriteOnly"), "A write-only property has no value to capture.");
            Assert.IsFalse(HasField(snapshot, "Item"), "An indexer is not a capturable member.");
        }

        [Test]
        public void Capture_EmptyType_HasNoFieldsOrSummary()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new Empty());
            Assert.AreEqual(0, snapshot.Fields.Count);
            Assert.IsNull(snapshot.Summary);
            Assert.AreEqual(nameof(Empty), snapshot.TypeName);
        }

        #endregion


        #region Opt-out

        [Test]
        public void Capture_OptedOut_KeepsOnlyTypeName()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new Secret { Password = 1234 });
            Assert.AreEqual(nameof(Secret), snapshot.TypeName);
            Assert.AreEqual(0, snapshot.Fields.Count, "An opted-out payload captures no field values.");
            Assert.IsNull(snapshot.Summary, "An opted-out payload captures no summary either.");
        }

        #endregion


        #region Never throws

        [Test]
        public void Capture_ThrowingGetter_RendersErrorNotThrow()
        {
            PayloadSnapshot snapshot = null;
            Assert.DoesNotThrow(() => snapshot = PayloadReflector.Capture(new ThrowingGetter()));
            Assert.AreEqual("<error>", ValueOf(snapshot, nameof(ThrowingGetter.Bad)));
        }

        [Test]
        public void Capture_ThrowingToString_RendersErrorSummaryNotThrow()
        {
            PayloadSnapshot snapshot = null;
            Assert.DoesNotThrow(() => snapshot = PayloadReflector.Capture(new ThrowingToString()));
            Assert.AreEqual("<error>", snapshot.Summary);
        }

        #endregion


        #region Snapshot rendering

        [Test]
        public void Snapshot_ToString_ListsFields()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new HasString { Text = "hi" });
            Assert.AreEqual("HasString { Text = \"hi\" }", snapshot.ToString());
        }

        [Test]
        public void Snapshot_ToString_UsesSummaryWhenPresent()
        {
            PayloadSnapshot snapshot = PayloadReflector.Capture(new Described { Value = 5 });
            Assert.AreEqual("Described (Described=5)", snapshot.ToString());
        }

        #endregion


        #region Payload types

        private enum Choice { First, Second }

        private struct Scalars
        {
            public int Number;
            public float Ratio;
            public bool Flag;
            public char Letter;
            public Choice Choice;
        }

        private class HasString
        {
            public string Text;
        }

        private class HasReference
        {
            public UnityEngine.Object Target;
        }

        private class HasList
        {
            public List<int> Values;
        }

        private class HasSequence
        {
            public IEnumerable<int> Numbers => Generate();

            private static IEnumerable<int> Generate()
            {
                yield return 0;
                yield return 1;
                yield return 2;
            }
        }

        private struct Bare
        {
            public int X;
        }

        private class HasBareList
        {
            public List<Bare> Points;
        }

        private class HasNested
        {
            public Bare Bare;
        }

        private struct Vec
        {
            public int X;
            public int Y;

            public override string ToString() => $"({X},{Y})";
        }

        private class HasVec
        {
            public Vec Position;
        }

        private struct Described
        {
            public int Value;

            public override string ToString() => $"Described={Value}";
        }

        private class HasProperties
        {
            public int Readable { get; set; }
            public int WriteOnly { set { } }
            public int this[int index] => index;
        }

        private struct Empty { }

        [Event(OmitSnapshot = true)]
        private struct Secret
        {
            public int Password;
        }

        private class ThrowingGetter
        {
            public int Bad => throw new InvalidOperationException("no");
        }

        private class ThrowingToString
        {
            public override string ToString() => throw new InvalidOperationException("no");
        }

        private class DummyAsset : ScriptableObject { }

        #endregion

    }

}
#endif
