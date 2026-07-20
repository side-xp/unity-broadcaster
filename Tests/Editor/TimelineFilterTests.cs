using NUnit.Framework;

using SideXP.Broadcaster.EditorOnly;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Filter tests: the Timeline window shows a dispatch only when it passes the filter's kind, type-name and owner axes (all combined with
    /// AND). Spans are produced through a bus + <see cref="SpanRecorder"/> so the matching runs against real spans and their listener owners.
    /// </summary>
    public class TimelineFilterTests
    {

        // Emits one signal and one cue on a fresh bus and returns the recorder holding both spans (signal first, cue second).
        private static SpanRecorder RecordSignalAndCue()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bus.Emit(new PingSignal());
            bus.Cue(new FlashCue()).GetAwaiter().GetResult();
            return recorder;
        }

        [Test]
        public void FreshFilter_MatchesEverything()
        {
            SpanRecorder recorder = RecordSignalAndCue();
            TimelineFilter filter = new TimelineFilter();

            foreach (RecordedSpan recorded in recorder.Spans)
                Assert.IsTrue(filter.Matches(recorded.Span));
        }

        [Test]
        public void KindFilter_HidesDisabledKinds()
        {
            SpanRecorder recorder = RecordSignalAndCue();
            DispatchSpan signal = recorder.Spans[0].Span;
            DispatchSpan cue = recorder.Spans[1].Span;
            TimelineFilter filter = new TimelineFilter();

            filter.SetKindEnabled(EventKind.Signal, false);

            Assert.IsFalse(filter.Matches(signal), "The disabled kind is hidden.");
            Assert.IsTrue(filter.Matches(cue), "Other kinds stay visible.");
        }

        [Test]
        public void TypeQuery_IsCaseInsensitiveSubstringOnTypeName()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bus.Emit(new PingSignal());
            bus.Emit(new PongSignal());
            DispatchSpan ping = recorder.Spans[0].Span;
            DispatchSpan pong = recorder.Spans[1].Span;
            TimelineFilter filter = new TimelineFilter();

            filter.TypeQuery = "ping";

            Assert.IsTrue(filter.Matches(ping));
            Assert.IsFalse(filter.Matches(pong));
        }

        [Test]
        public void EmptyTypeQuery_MatchesEveryType()
        {
            SpanRecorder recorder = RecordSignalAndCue();
            TimelineFilter filter = new TimelineFilter { TypeQuery = null };

            foreach (RecordedSpan recorded in recorder.Spans)
                Assert.IsTrue(filter.Matches(recorded.Span));
        }

        [Test]
        public void OwnerFilter_MatchesOnlyDispatchesInvolvingThatOwner()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            object a = new object();
            object b = new object();
            bus.Subscribe<PingSignal>(a, _ => { });
            bus.Subscribe<PongSignal>(b, _ => { });
            bus.Emit(new PingSignal()); // listener owned by a
            bus.Emit(new PongSignal()); // listener owned by b
            DispatchSpan ownedByA = recorder.Spans[0].Span;
            DispatchSpan ownedByB = recorder.Spans[1].Span;
            TimelineFilter filter = new TimelineFilter();

            filter.Owner = a;

            Assert.IsTrue(filter.Matches(ownedByA), "A dispatch that invoked the owner's callback matches.");
            Assert.IsFalse(filter.Matches(ownedByB), "A dispatch that didn't involve the owner is hidden.");
        }

        [Test]
        public void NullOwner_MatchesEveryOwner()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bus.Subscribe<PingSignal>(new object(), _ => { });
            bus.Emit(new PingSignal());
            TimelineFilter filter = new TimelineFilter { Owner = null };

            Assert.IsTrue(filter.Matches(recorder.Spans[0].Span));
        }

        [Test]
        public void Matches_NullSpan_IsFalse()
        {
            TimelineFilter filter = new TimelineFilter();
            Assert.IsFalse(filter.Matches((DispatchSpan)null));
        }

        [Test]
        public void Changed_RaisedWhenSettingsChange()
        {
            TimelineFilter filter = new TimelineFilter();
            int changes = 0;
            filter.Changed += () => changes++;

            filter.SetKindEnabled(EventKind.Command, false);
            filter.TypeQuery = "move";
            filter.Owner = new object();

            Assert.AreEqual(3, changes);
            filter.SetKindEnabled(EventKind.Command, false); // no-op, already disabled
            Assert.AreEqual(3, changes, "Setting a value to what it already is raises nothing.");
        }

    }

}
