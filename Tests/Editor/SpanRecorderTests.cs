using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using SideXP.Broadcaster.EditorOnly;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Recorder tests: the Timeline window's retention lives in a <see cref="SpanRecorder"/> fed only by a bus's monitor hooks. These prove
    /// the retention logic — capture on begin, bounded eviction, pause, attach/detach, clear, frame range, violation capture and the
    /// emitter-stack opt-in — with the rendering left to the manual gate. Each test uses a fresh <see cref="EventBus"/>, and a durative cue's
    /// in-flight span is driven through a test-owned <see cref="AwaitableCompletionSource"/> so the whole suite stays synchronous.
    /// </summary>
    public class SpanRecorderTests
    {

        [Test]
        public void Emit_RecordsCompletedSpanWithListener()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bus.Subscribe<PingSignal>(new object(), _ => { });

            bus.Emit(new PingSignal { Value = 7 });

            Assert.AreEqual(1, recorder.Spans.Count);
            DispatchSpan span = recorder.Spans[0].Span;
            Assert.AreEqual(EventKind.Signal, span.Kind);
            Assert.AreEqual(typeof(PingSignal), span.EventType);
            Assert.IsTrue(span.IsComplete, "A synchronous emit opens and closes its span within the call.");
            Assert.AreEqual(1, span.Listeners.Count);
        }

        [Test]
        public void Emit_WithNoListeners_StillRecordsSpan()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);

            bus.Emit(new PingSignal());

            Assert.AreEqual(1, recorder.Spans.Count, "\"Nothing happened\" is still observable on the timeline.");
            Assert.AreEqual(0, recorder.Spans[0].Span.Listeners.Count);
        }

        [Test]
        public void Emits_AreRetainedInOrder()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);

            bus.Emit(new PingSignal());
            bus.Emit(new PingSignal());
            bus.Emit(new PingSignal());

            Assert.AreEqual(3, recorder.Spans.Count);
            Assert.Less(recorder.Spans[0].Span.Id, recorder.Spans[1].Span.Id);
            Assert.Less(recorder.Spans[1].Span.Id, recorder.Spans[2].Span.Id);
        }

        [Test]
        public void Capacity_EvictsOldestWhenExceeded()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder { Capacity = 2 };
            recorder.Attach(bus);

            bus.Emit(new PingSignal()); // id 1
            bus.Emit(new PingSignal()); // id 2
            bus.Emit(new PingSignal()); // id 3

            Assert.AreEqual(2, recorder.Spans.Count);
            Assert.AreEqual(2, recorder.Spans[0].Span.Id, "The oldest span was evicted.");
            Assert.AreEqual(3, recorder.Spans[1].Span.Id);
        }

        [Test]
        public void Capacity_IsClampedToAtLeastOne()
        {
            SpanRecorder recorder = new SpanRecorder { Capacity = 0 };
            Assert.AreEqual(1, recorder.Capacity);
        }

        [Test]
        public void LoweringCapacity_TrimsImmediately()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bus.Emit(new PingSignal());
            bus.Emit(new PingSignal());
            bus.Emit(new PingSignal());

            recorder.Capacity = 1;

            Assert.AreEqual(1, recorder.Spans.Count);
            Assert.AreEqual(3, recorder.Spans[0].Span.Id, "Only the most recent span survives the trim.");
        }

        [Test]
        public void Paused_IgnoresNewSpans_ThenResumes()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);

            recorder.IsRecording = false;
            bus.Emit(new PingSignal());
            Assert.AreEqual(0, recorder.Spans.Count, "A paused recorder captures nothing new.");

            recorder.IsRecording = true;
            bus.Emit(new PingSignal());
            Assert.AreEqual(1, recorder.Spans.Count);
        }

        [Test]
        public void Detach_StopsRecording()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);

            recorder.Detach();
            bus.Emit(new PingSignal());

            Assert.AreEqual(0, recorder.Spans.Count);
            Assert.IsFalse(recorder.IsAttached);
        }

        [Test]
        public void Attach_ToNewBus_StopsRecordingTheOldOne()
        {
            EventBus first = new EventBus();
            EventBus second = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(first);

            recorder.Attach(second);
            first.Emit(new PingSignal());

            Assert.AreEqual(0, recorder.Spans.Count, "The recorder no longer hears the bus it detached from.");
        }

        [Test]
        public void Clear_EmptiesSpansAndViolations()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bus.Emit(new PingSignal());

            recorder.Clear();

            Assert.AreEqual(0, recorder.Spans.Count);
            Assert.AreEqual(0, recorder.Violations.Count);
        }

        [Test]
        public void Violation_IsRecorded()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            object owner = new object();

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });
            LogAssert.Expect(LogType.Error, new Regex("already registered"));
            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 2 });

            Assert.AreEqual(1, recorder.Violations.Count);
            Assert.AreEqual(ViolationKind.MultipleProviders, recorder.Violations[0].Kind);
            Assert.AreEqual(typeof(PingSignal), recorder.Violations[0].EventType);
        }

        [Test]
        public void TryGetFrameRange_EmptyReturnsFalse()
        {
            SpanRecorder recorder = new SpanRecorder();
            Assert.IsFalse(recorder.TryGetFrameRange(out int first, out int last));
            Assert.AreEqual(0, first);
            Assert.AreEqual(0, last);
        }

        [Test]
        public void TryGetFrameRange_SpansTheRetainedDispatches()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bus.Emit(new PingSignal());

            Assert.IsTrue(recorder.TryGetFrameRange(out int first, out int last));
            DispatchSpan span = recorder.Spans[0].Span;
            Assert.AreEqual(span.BeginFrame, first);
            Assert.AreEqual(span.EndFrame, last);
        }

        [Test]
        public void DurativeCue_RecordsInFlightSpan_ThenReflectsCompletion()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            AwaitableCompletionSource performer = new AwaitableCompletionSource();
            bus.Perform<FlashCue>(new object(), _ => performer.Awaitable);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();

            Assert.AreEqual(1, recorder.Spans.Count);
            Assert.IsFalse(recorder.Spans[0].Span.IsComplete, "A durative cue is retained as a live, still-open span.");

            performer.SetResult();
            Assert.IsTrue(recorder.Spans[0].Span.IsComplete, "The retained span reflects completion in place.");
            awaiter.GetResult();
        }

        [Test]
        public void CaptureEmitterStacks_OnCapturesCallerOffLeavesNull()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);

            bus.Emit(new PingSignal());
            Assert.IsNull(recorder.Spans[0].EmitterStack, "Stack capture is off by default.");

            recorder.CaptureEmitterStacks = true;
            bus.Emit(new PingSignal());
            string stack = recorder.Spans[1].EmitterStack;
            Assert.IsFalse(string.IsNullOrEmpty(stack), "Stack capture records the emitter's call stack.");
            StringAssert.Contains(nameof(CaptureEmitterStacks_OnCapturesCallerOffLeavesNull), stack, "The captured stack starts at the caller, not the bus internals.");
        }

        [Test]
        public void Changed_RaisedWhenASpanIsRecorded()
        {
            EventBus bus = new EventBus();
            SpanRecorder recorder = new SpanRecorder();
            recorder.Attach(bus);
            bool changed = false;
            recorder.Changed += () => changed = true;

            bus.Emit(new PingSignal());

            Assert.IsTrue(changed);
        }

    }

}
