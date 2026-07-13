using System.Reflection;

using NUnit.Framework;

using SideXP.Broadcaster.EditorOnly;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Emit-box draft tests: the editor-side field editor exposes a type's public fields, edits them in place (including on a struct
    /// payload, which must survive as a boxed instance), and resets. Pure logic, no window.
    /// </summary>
    public class EventDraftTests
    {

        [Test]
        public void Draft_ExposesPublicInstanceFields()
        {
            EventDraft draft = new EventDraft(typeof(SumRequest));

            Assert.AreEqual(2, draft.Fields.Count);
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, System.Array.ConvertAll(new[] { draft.Fields[0], draft.Fields[1] }, f => f.Name));
        }

        [Test]
        public void Draft_EmptyPayload_HasNoFields()
        {
            EventDraft draft = new EventDraft(typeof(EmptySignal));
            Assert.IsTrue(draft.CanInstantiate);
            Assert.AreEqual(0, draft.Fields.Count);
        }

        [Test]
        public void Draft_SetValueOnStruct_RoundTrips()
        {
            EventDraft draft = new EventDraft(typeof(PingSignal));
            FieldInfo value = typeof(PingSignal).GetField(nameof(PingSignal.Value));

            draft.SetValue(value, 123);

            Assert.AreEqual(123, draft.GetValue(value));
            // The mutation lands on the very instance the window fires, not a copy.
            Assert.AreEqual(123, ((PingSignal)draft.Instance).Value);
        }

        [Test]
        public void Draft_Reset_RestoresDefaults()
        {
            EventDraft draft = new EventDraft(typeof(PingSignal));
            FieldInfo value = typeof(PingSignal).GetField(nameof(PingSignal.Value));

            draft.SetValue(value, 99);
            draft.Reset();

            Assert.AreEqual(0, ((PingSignal)draft.Instance).Value);
        }

        [Test]
        public void Draft_ClassWithParameterlessCtor_CanInstantiate()
        {
            EventDraft draft = new EventDraft(typeof(BaseSignal));
            Assert.IsTrue(draft.CanInstantiate);
            Assert.IsNotNull(draft.Instance);
        }

        [Test]
        public void Draft_ClassWithoutParameterlessCtor_CannotInstantiate()
        {
            EventDraft draft = new EventDraft(typeof(NoDefaultCtorCommand));

            Assert.IsFalse(draft.CanInstantiate);
            Assert.IsNull(draft.Instance);
            // Setting a field on a non-instantiable draft is a safe no-op.
            Assert.DoesNotThrow(() => draft.SetValue(typeof(NoDefaultCtorCommand).GetField(nameof(NoDefaultCtorCommand.Steps)), 3));
        }

    }

}
