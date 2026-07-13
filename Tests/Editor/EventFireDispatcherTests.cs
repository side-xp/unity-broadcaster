using System.Reflection;

using NUnit.Framework;

using SideXP.Broadcaster.EditorOnly;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Dispatcher tests: the window fires a drafted event onto a bus through the constrained verbs (reached by reflection), and gets back a
    /// result it can render. Covers the synchronous kinds end-to-end and that a cue starts its performers; each test uses a fresh bus.
    /// </summary>
    public class EventFireDispatcherTests
    {

        private static EventEntry Entry<T>()
        {
            Assert.IsTrue(EventCatalog.TryClassify(typeof(T), out EventEntry entry), $"{typeof(T).Name} should classify.");
            return entry;
        }

        [Test]
        public void Fire_Signal_DeliversDraftedPayloadToListener()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int received = -1;
            bus.Subscribe<PingSignal>(owner, s => received = s.Value);

            EventDraft draft = new EventDraft(typeof(PingSignal));
            draft.SetValue(typeof(PingSignal).GetField(nameof(PingSignal.Value)), 7);
            FireResult result = EventFireDispatcher.Fire(bus, Entry<PingSignal>(), draft.Instance);

            Assert.IsFalse(result.Faulted);
            Assert.AreEqual(7, received);
        }

        [Test]
        public void Fire_VoidCommand_WithHandler_Acknowledges()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool handled = false;
            bus.Obey<MoveCommand>(owner, _ => handled = true);

            FireResult result = EventFireDispatcher.Fire(bus, Entry<MoveCommand>(), new EventDraft(typeof(MoveCommand)).Instance);

            Assert.IsTrue(handled);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(true, result.Value);
        }

        [Test]
        public void Fire_VoidCommand_NoHandler_ReportsNotPerformed()
        {
            EventBus bus = new EventBus();

            // The void Order logs a dev-build error when unhandled; that's expected here.
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("No handler is registered for command"));
            FireResult result = EventFireDispatcher.Fire(bus, Entry<MoveCommand>(), new EventDraft(typeof(MoveCommand)).Instance);

            Assert.IsFalse(result.Faulted);
            Assert.AreEqual(false, result.Value);
        }

        [Test]
        public void Fire_ValuedCommand_ReturnsHandlerOutcome()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Obey<DoubleCommand, int>(owner, cmd => cmd.Value * 2);

            EventDraft draft = new EventDraft(typeof(DoubleCommand));
            draft.SetValue(typeof(DoubleCommand).GetField(nameof(DoubleCommand.Value)), 21);
            FireResult result = EventFireDispatcher.Fire(bus, Entry<DoubleCommand>(), draft.Instance);

            Assert.IsFalse(result.Faulted);
            Assert.AreEqual(42, result.Value);
        }

        [Test]
        public void Fire_Request_ReturnsAnswer()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Answer<SumRequest, int>(owner, req => req.A + req.B);

            EventDraft draft = new EventDraft(typeof(SumRequest));
            draft.SetValue(typeof(SumRequest).GetField(nameof(SumRequest.A)), 2);
            draft.SetValue(typeof(SumRequest).GetField(nameof(SumRequest.B)), 3);
            FireResult result = EventFireDispatcher.Fire(bus, Entry<SumRequest>(), draft.Instance);

            Assert.IsFalse(result.Faulted);
            Assert.AreEqual(5, result.Value);
        }

        [Test]
        public void Fire_Request_NoHandler_FaultsGracefully()
        {
            EventBus bus = new EventBus();

            // Ask throws by design when unanswered; the dispatcher catches it and reports a fault instead of propagating.
            FireResult result = EventFireDispatcher.Fire(bus, Entry<SumRequest>(), new EventDraft(typeof(SumRequest)).Instance);

            Assert.IsTrue(result.Faulted);
            Assert.IsNotNull(result.Exception);
        }

        [Test]
        public void Fire_Cue_StartsPerformersAndReturnsCompletion()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool performed = false;
            bus.Perform<FlashCue>(owner, _ => performed = true);

            FireResult result = EventFireDispatcher.Fire(bus, Entry<FlashCue>(), new EventDraft(typeof(FlashCue)).Instance);

            // An instant performer runs synchronously on send, and the cue hands back a completion for the window to await.
            Assert.IsTrue(performed);
            Assert.IsFalse(result.Faulted);
            Assert.IsNotNull(result.Completion);
        }

    }

}
