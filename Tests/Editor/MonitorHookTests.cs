#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !BROADCASTER_MONITOR_OFF
#define BROADCASTER_MONITOR
#endif

#if BROADCASTER_MONITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Tests for the monitor hooks on signal dispatch and on registration changes: the exact begin/end sequence of a dispatch and its
    /// callback sub-spans, the fields each span carries (kind, type, payload snapshot, outcome), synchronous-cascade causality (a nested
    /// emit's parent is the emit it ran inside of), and the role/reason reported when registrations come and go. Every test uses a fresh
    /// <see cref="EventBus"/> and attaches a <see cref="Recorder"/>.
    /// </summary>
    public class MonitorHookTests
    {

        #region Helpers

        /// <summary>Records the hook stream of a bus for assertions.</summary>
        private sealed class Recorder
        {
            public readonly List<string> Sequence = new List<string>();
            public readonly List<DispatchSpan> SpansBegan = new List<DispatchSpan>();
            public readonly List<DispatchSpan> SpansEnded = new List<DispatchSpan>();
            public readonly List<ListenerSpan> ListenersEnded = new List<ListenerSpan>();
            public readonly List<RegistrationInfo> Registered = new List<RegistrationInfo>();
            public readonly List<RegistrationInfo> Unregistered = new List<RegistrationInfo>();

            public Recorder(EventBus bus)
            {
                bus.OnSpanBegan += span => { Sequence.Add("span+"); SpansBegan.Add(span); };
                bus.OnSpanEnded += span => { Sequence.Add("span-"); SpansEnded.Add(span); };
                bus.OnListenerBegan += _ => Sequence.Add("listener+");
                bus.OnListenerEnded += listener => { Sequence.Add("listener-"); ListenersEnded.Add(listener); };
                bus.OnRegistered += info => Registered.Add(info);
                bus.OnUnregistered += info => Unregistered.Add(info);
            }
        }

        /// <summary>Returns the captured value of a snapshot field by name.</summary>
        private static string ValueOf(PayloadSnapshot snapshot, string name)
        {
            foreach (PayloadField field in snapshot.Fields)
            {
                if (field.Name == name)
                    return field.Value;
            }
            Assert.Fail($"Expected a captured member named '{name}'. Snapshot: {snapshot}");
            return null;
        }

        #endregion


        #region Dispatch spans

        [Test]
        public void Emit_OneListener_ProducesSpanWithOneSubSpan()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Subscribe<PingSignal>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.Emit(new PingSignal { Value = 7 });

            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "span-" }, recorder.Sequence);

            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(1, span.Id, "The first span on a fresh bus has id 1.");
            Assert.AreEqual(EventKind.Signal, span.Kind);
            Assert.AreEqual(typeof(PingSignal), span.EventType);
            Assert.AreEqual("7", ValueOf(span.Payload, nameof(PingSignal.Value)));
            Assert.IsTrue(span.IsComplete);
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome);
            Assert.AreSame(span, recorder.SpansEnded[0], "SpanBegan and SpanEnded carry the same instance.");

            Assert.AreEqual(1, span.Listeners.Count);
            ListenerSpan listener = span.Listeners[0];
            Assert.AreSame(owner, listener.Owner);
            Assert.AreEqual(RegistrationRole.SignalListener, listener.Role);
            Assert.AreEqual(DispatchOutcome.Completed, listener.Outcome);
            Assert.IsTrue(listener.IsComplete);
        }

        [Test]
        public void Emit_ZeroListeners_ProducesAnEmptySpan()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            bus.Emit(new PingSignal { Value = 1 });

            CollectionAssert.AreEqual(new[] { "span+", "span-" }, recorder.Sequence);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count, "Nobody listened, so the span has no sub-spans.");
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansBegan[0].Outcome);
        }

        [Test]
        public void Emit_TwoListeners_SubSpansInOrder()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Subscribe<PingSignal>(owner, _ => { });
            bus.Subscribe<PingSignal>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.Emit(new PingSignal());

            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "listener+", "listener-", "span-" }, recorder.Sequence);
            Assert.AreEqual(2, recorder.SpansBegan[0].Listeners.Count);
        }

        [Test]
        public void Emit_ThrowingListener_SubSpanFaultedDispatchCompleted()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Subscribe<PingSignal>(owner, _ => throw new InvalidOperationException("boom"));
            Recorder recorder = new Recorder(bus);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            bus.Emit(new PingSignal());

            ListenerSpan listener = recorder.ListenersEnded[0];
            Assert.AreEqual(DispatchOutcome.Faulted, listener.Outcome);
            Assert.IsNotNull(listener.Exception);
            Assert.AreEqual("boom", listener.Exception.Message);
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansEnded[0].Outcome, "A faulting listener is isolated; the dispatch still completes.");
        }

        #endregion


        #region Causality

        [Test]
        public void Emit_NestedFromListener_InnerSpanParentIsOuter()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Subscribe<PingSignal>(owner, _ => bus.Emit(new PongSignal { Text = "x" }));
            bus.Subscribe<PongSignal>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.Emit(new PingSignal());

            CollectionAssert.AreEqual(
                new[] { "span+", "listener+", "span+", "listener+", "listener-", "span-", "listener-", "span-" },
                recorder.Sequence,
                "The inner dispatch opens and closes entirely inside the outer listener's sub-span.");

            DispatchSpan outer = recorder.SpansBegan[0];
            DispatchSpan inner = recorder.SpansBegan[1];
            Assert.AreEqual(typeof(PingSignal), outer.EventType);
            Assert.AreEqual(typeof(PongSignal), inner.EventType);
            Assert.IsNull(outer.Parent, "The top-level emit has no parent.");
            Assert.AreSame(outer, inner.Parent, "The nested emit's parent is the emit it ran inside of.");
        }

        [Test]
        public void Emit_AfterCascade_NextTopLevelSpanHasNoParent()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Subscribe<PingSignal>(owner, _ => bus.Emit(new PongSignal()));
            bus.Subscribe<PongSignal>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.Emit(new PingSignal());
            bus.Emit(new PingSignal());

            // The ambient span is restored after each cascade, so the second top-level emit is parentless.
            DispatchSpan secondTopLevel = recorder.SpansBegan[2];
            Assert.AreEqual(typeof(PingSignal), secondTopLevel.EventType);
            Assert.IsNull(secondTopLevel.Parent);
        }

        #endregion


        #region Registration changes

        [Test]
        public void Subscribe_ReportsSignalListenerRegistration()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            Recorder recorder = new Recorder(bus);

            bus.Subscribe<PingSignal>(owner, _ => { });

            Assert.AreEqual(1, recorder.Registered.Count);
            RegistrationInfo info = recorder.Registered[0];
            Assert.AreEqual(EventKind.Signal, info.Kind);
            Assert.AreEqual(RegistrationRole.SignalListener, info.Role);
            Assert.AreEqual(typeof(PingSignal), info.EventType);
            Assert.AreSame(owner, info.Owner);
            Assert.AreEqual(RegistrationChangeReason.Registered, info.Reason);
        }

        [Test]
        public void Provide_ReportsProviderRegistration()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            bus.Provide<PingSignal>(new object(), () => new PingSignal());

            Assert.AreEqual(RegistrationRole.Provider, recorder.Registered[0].Role);
            Assert.AreEqual(EventKind.Signal, recorder.Registered[0].Kind);
        }

        [Test]
        public void Obey_ReportsCommandHandlerRegistration()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            bus.Obey<MoveCommand>(new object(), _ => { });

            Assert.AreEqual(RegistrationRole.CommandHandler, recorder.Registered[0].Role);
            Assert.AreEqual(EventKind.Command, recorder.Registered[0].Kind);
        }

        [Test]
        public void Answer_ReportsRequestHandlerRegistration()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            bus.Answer<SumRequest, int>(new object(), request => request.A + request.B);

            Assert.AreEqual(RegistrationRole.RequestHandler, recorder.Registered[0].Role);
            Assert.AreEqual(EventKind.Request, recorder.Registered[0].Kind);
        }

        [Test]
        public void Perform_ReportsCuePerformerRegistration()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            bus.Perform<FlashCue>(new object(), _ => { });

            Assert.AreEqual(RegistrationRole.CuePerformer, recorder.Registered[0].Role);
            Assert.AreEqual(EventKind.Cue, recorder.Registered[0].Kind);
        }

        [Test]
        public void Unsubscribe_ReportsUnsubscribedReason()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            Action<PingSignal> listener = _ => { };
            bus.Subscribe(owner, listener);
            Recorder recorder = new Recorder(bus);

            bus.Unsubscribe(listener);

            Assert.AreEqual(1, recorder.Unregistered.Count);
            Assert.AreEqual(RegistrationChangeReason.Unsubscribed, recorder.Unregistered[0].Reason);
        }

        [Test]
        public void HandleDispose_ReportsDisposedReason()
        {
            EventBus bus = new EventBus();
            SubscriptionHandle handle = bus.Subscribe<PingSignal>(new object(), _ => { });
            Recorder recorder = new Recorder(bus);

            handle.Dispose();

            Assert.AreEqual(RegistrationChangeReason.Disposed, recorder.Unregistered[0].Reason);
        }

        [Test]
        public void UnsubscribeAll_ReportsRemovedByOwnerReason()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Subscribe<PingSignal>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.UnsubscribeAll(owner);

            Assert.AreEqual(RegistrationChangeReason.RemovedByOwner, recorder.Unregistered[0].Reason);
        }

        [Test]
        public void Clear_ReportsClearedReason()
        {
            EventBus bus = new EventBus();
            bus.Subscribe<PingSignal>(new object(), _ => { });
            Recorder recorder = new Recorder(bus);

            bus.Clear();

            Assert.AreEqual(1, recorder.Unregistered.Count);
            Assert.AreEqual(RegistrationChangeReason.Cleared, recorder.Unregistered[0].Reason);
        }

        [Test]
        public void Provide_Replace_ReportsReplacedThenRegistered()
        {
            EventBus bus = new EventBus();
            object first = new object();
            object second = new object();
            Recorder recorder = new Recorder(bus);

            bus.Provide<PingSignal>(first, () => new PingSignal());
            bus.Provide<PingSignal>(second, () => new PingSignal(), replace: true);

            Assert.AreEqual(1, recorder.Unregistered.Count);
            Assert.AreEqual(RegistrationChangeReason.Replaced, recorder.Unregistered[0].Reason);
            Assert.AreSame(first, recorder.Unregistered[0].Owner, "The superseded provider is the one reported as replaced.");
            Assert.AreEqual(2, recorder.Registered.Count, "Both the original and the replacement are reported as registered.");
        }

        #endregion


        #region Cue spans

        [Test]
        public void Cue_InstantPerformers_SpanWithSubSpans()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Perform<FlashCue>(owner, _ => { });
            bus.Perform<FlashCue>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.Cue(new FlashCue { Value = 5 }).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "listener+", "listener-", "span-" }, recorder.Sequence);

            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(EventKind.Cue, span.Kind);
            Assert.AreEqual(typeof(FlashCue), span.EventType);
            Assert.AreEqual("5", ValueOf(span.Payload, nameof(FlashCue.Value)));
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome);
            Assert.AreEqual(2, span.Listeners.Count);
            Assert.AreEqual(RegistrationRole.CuePerformer, span.Listeners[0].Role);
            Assert.AreEqual(DispatchOutcome.Completed, span.Listeners[0].Outcome);
        }

        [Test]
        public void Cue_ZeroPerformers_EmptyCompletedSpan()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            bus.Cue(new FlashCue()).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "span+", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count);
        }

        [Test]
        public void Cue_AlreadyCancelled_CancelledSpanNoSubSpans()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Perform<FlashCue>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.Cue(new FlashCue(), new CancellationToken(canceled: true));

            CollectionAssert.AreEqual(new[] { "span+", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Cancelled, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count, "An already-cancelled cue never starts its performers.");
        }

        [Test]
        public void Cue_DurativePerformer_SubSpanAndSpanCloseWhenItFinishes()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource performer = new AwaitableCompletionSource();
            bus.Perform<FlashCue>(owner, _ => performer.Awaitable);
            Recorder recorder = new Recorder(bus);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();

            // The performer's sub-span opened but neither it nor the cue's span has closed yet.
            CollectionAssert.AreEqual(new[] { "span+", "listener+" }, recorder.Sequence);
            Assert.IsFalse(recorder.SpansBegan[0].IsComplete);

            performer.SetResult();
            awaiter.GetResult();

            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansBegan[0].Listeners[0].Outcome);
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansBegan[0].Outcome);
        }

        [Test]
        public void Cue_InstantPerformerThrows_SubSpanFaultedCueCompleted()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            Action<FlashCue> throwing = _ => throw new InvalidOperationException("boom");
            bus.Perform<FlashCue>(owner, throwing);
            bus.Perform<FlashCue>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            bus.Cue(new FlashCue()).GetAwaiter().GetResult();

            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(DispatchOutcome.Faulted, span.Listeners[0].Outcome);
            Assert.AreEqual("boom", span.Listeners[0].Exception.Message);
            Assert.AreEqual(DispatchOutcome.Completed, span.Listeners[1].Outcome, "The healthy performer still completes.");
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome, "A faulting performer is isolated; the cue still completes.");
        }

        [Test]
        public void Cue_CancelledDuringFlight_SubSpansAndSpanCancelled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource first = new AwaitableCompletionSource();
            AwaitableCompletionSource second = new AwaitableCompletionSource();
            bus.Perform<FlashCue>(owner, _ => first.Awaitable);
            bus.Perform<FlashCue>(owner, _ => second.Awaitable);
            Recorder recorder = new Recorder(bus);

            CancellationTokenSource cts = new CancellationTokenSource();
            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue(), cts.Token).GetAwaiter();
            cts.Cancel();
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());

            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Outcome);
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Listeners[0].Outcome);
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Listeners[1].Outcome, "The cancellation fans out to every in-flight performer's sub-span.");
        }

        [Test]
        public void Cue_PerformerReleasedMidCue_SubSpanCancelledCueCompletes()
        {
            EventBus bus = new EventBus();
            object fast = new object();
            object slow = new object();
            AwaitableCompletionSource never = new AwaitableCompletionSource();
            bus.Perform<FlashCue>(fast, _ => { });
            bus.Perform<FlashCue>(slow, _ => never.Awaitable);
            Recorder recorder = new Recorder(bus);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            bus.UnsubscribeAll(slow);
            awaiter.GetResult();

            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(DispatchOutcome.Completed, span.Listeners[0].Outcome, "The instant performer completed.");
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Listeners[1].Outcome, "The released performer's sub-span resolves as cancelled.");
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome, "The cue still completes on the rest (this drain wasn't a token cancellation).");
        }

        #endregion


        #region Handled spans (sync)

        [Test]
        public void Order_VoidHandler_SpanWithHandlerSubSpan()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Obey<MoveCommand>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bool handled = bus.Order(new MoveCommand { Steps = 3 });

            Assert.IsTrue(handled);
            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "span-" }, recorder.Sequence);
            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(EventKind.Command, span.Kind);
            Assert.AreEqual(typeof(MoveCommand), span.EventType);
            Assert.AreEqual("3", ValueOf(span.Payload, nameof(MoveCommand.Steps)));
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome);
            Assert.AreEqual(RegistrationRole.CommandHandler, span.Listeners[0].Role);
            Assert.AreEqual(DispatchOutcome.Completed, span.Listeners[0].Outcome);
        }

        [Test]
        public void Order_VoidNoHandler_FaultedSpanNoSubSpan()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            LogAssert.Expect(LogType.Error, new Regex("No handler is registered for command"));
            bool handled = bus.Order(new MoveCommand());

            Assert.IsFalse(handled);
            CollectionAssert.AreEqual(new[] { "span+", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Faulted, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count);
        }

        [Test]
        public void Order_VoidHandlerThrows_FaultedSubSpanAndSpanPropagates()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            // Typed local so the throw-expression lambda binds to the void handler overload, not the async one.
            Action<MoveCommand> throwing = _ => throw new InvalidOperationException("boom");
            bus.Obey<MoveCommand>(owner, throwing);
            Recorder recorder = new Recorder(bus);

            Assert.Throws<InvalidOperationException>(() => bus.Order(new MoveCommand()));

            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(DispatchOutcome.Faulted, span.Listeners[0].Outcome);
            Assert.AreEqual("boom", span.Listeners[0].Exception.Message);
            Assert.AreEqual(DispatchOutcome.Faulted, span.Outcome);
        }

        [Test]
        public void Order_VoidAsyncHandlerThroughSyncVerb_FaultedSpan()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Obey<MoveCommand>(owner, _ => new AwaitableCompletionSource().Awaitable);
            Recorder recorder = new Recorder(bus);

            LogAssert.Expect(LogType.Error, new Regex("asynchronous"));
            bool handled = bus.Order(new MoveCommand());

            Assert.IsFalse(handled);
            Assert.AreEqual(DispatchOutcome.Faulted, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count, "The async handler is never invoked by the sync verb.");
        }

        [Test]
        public void Order_ValuedHandler_SpanCompletedWithResult()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Obey<DoubleCommand, int>(owner, command => command.Value * 2);
            Recorder recorder = new Recorder(bus);

            int result = bus.Order(new DoubleCommand { Value = 21 });

            Assert.AreEqual(42, result);
            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "span-" }, recorder.Sequence);
            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(typeof(DoubleCommand), span.EventType, "The span is keyed on the runtime command type, not the interface.");
            Assert.AreEqual("21", ValueOf(span.Payload, nameof(DoubleCommand.Value)), "The snapshot reflects the concrete command, not the empty interface.");
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome);
        }

        [Test]
        public void Order_ValuedNoHandler_FaultedSpanThenThrows()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            Assert.Throws<InvalidOperationException>(() => bus.Order(new DoubleCommand()));

            Assert.AreEqual(DispatchOutcome.Faulted, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count);
        }

        [Test]
        public void Ask_Handler_SpanCompletedWithAnswer()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);
            Recorder recorder = new Recorder(bus);

            int answer = bus.Ask(new SumRequest { A = 2, B = 3 });

            Assert.AreEqual(5, answer);
            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "span-" }, recorder.Sequence);
            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(EventKind.Request, span.Kind);
            Assert.AreEqual(typeof(SumRequest), span.EventType);
            Assert.AreEqual(RegistrationRole.RequestHandler, span.Listeners[0].Role);
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome);
        }

        [Test]
        public void Ask_NoHandler_FaultedSpanThenThrows()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            Assert.Throws<InvalidOperationException>(() => bus.Ask(new SumRequest()));

            Assert.AreEqual(DispatchOutcome.Faulted, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count);
        }

        [Test]
        public void TryAsk_NoHandler_CompletedSpanNoSubSpan()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            bool answered = bus.TryAsk(new SumRequest(), out int _);

            Assert.IsFalse(answered);
            CollectionAssert.AreEqual(new[] { "span+", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansBegan[0].Outcome, "A tolerant miss is not a fault.");
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count);
        }

        [Test]
        public void TryAsk_Handler_CompletedSpanWithSubSpan()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Answer<SumRequest, int>(owner, request => request.A + request.B);
            Recorder recorder = new Recorder(bus);

            bool answered = bus.TryAsk(new SumRequest { A = 4, B = 6 }, out int result);

            Assert.IsTrue(answered);
            Assert.AreEqual(10, result);
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(RegistrationRole.RequestHandler, recorder.SpansBegan[0].Listeners[0].Role);
        }

        #endregion


        #region Handled spans (async)

        [Test]
        public void OrderAsync_AsyncHandler_BlockClosesWhenResolved()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource handler = new AwaitableCompletionSource();
            bus.Obey<MoveCommand>(owner, _ => handler.Awaitable);
            Recorder recorder = new Recorder(bus);

            Awaitable.Awaiter awaiter = bus.OrderAsync(new MoveCommand { Steps = 2 }).GetAwaiter();

            // The block is open: the span and its sub-span began but haven't closed.
            CollectionAssert.AreEqual(new[] { "span+", "listener+" }, recorder.Sequence);
            Assert.IsFalse(recorder.SpansBegan[0].IsComplete);
            Assert.AreEqual("2", ValueOf(recorder.SpansBegan[0].Payload, nameof(MoveCommand.Steps)));

            handler.SetResult();
            awaiter.GetResult();

            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "span-" }, recorder.Sequence);
            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(EventKind.Command, span.Kind);
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome);
            Assert.AreEqual(DispatchOutcome.Completed, span.Listeners[0].Outcome);
        }

        [Test]
        public void OrderAsync_AsyncHandlerFaults_FaultedBlock()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource handler = new AwaitableCompletionSource();
            bus.Obey<MoveCommand>(owner, _ => handler.Awaitable);
            Recorder recorder = new Recorder(bus);

            Awaitable.Awaiter awaiter = bus.OrderAsync(new MoveCommand()).GetAwaiter();
            handler.SetException(new InvalidOperationException("boom"));

            Assert.Catch(() => awaiter.GetResult());
            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(DispatchOutcome.Faulted, span.Listeners[0].Outcome);
            Assert.AreEqual("boom", span.Listeners[0].Exception.Message);
            Assert.AreEqual(DispatchOutcome.Faulted, span.Outcome);
        }

        [Test]
        public void OrderAsync_CancelledDuringFlight_CancelledBlock()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource never = new AwaitableCompletionSource();
            bus.Obey<MoveCommand>(owner, _ => never.Awaitable);
            Recorder recorder = new Recorder(bus);

            CancellationTokenSource cts = new CancellationTokenSource();
            Awaitable.Awaiter awaiter = bus.OrderAsync(new MoveCommand(), cts.Token).GetAwaiter();
            cts.Cancel();

            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Outcome);
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Listeners[0].Outcome);
        }

        [Test]
        public void OrderAsync_HandlerUnregisteredMidFlight_CancelledBlock()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource never = new AwaitableCompletionSource();
            SubscriptionHandle handle = bus.Obey<MoveCommand>(owner, _ => never.Awaitable);
            Recorder recorder = new Recorder(bus);

            Awaitable.Awaiter awaiter = bus.OrderAsync(new MoveCommand()).GetAwaiter();
            handle.Dispose();

            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Outcome, "Unregistering the handler mid-flight resolves the block as cancelled.");
            Assert.AreEqual(DispatchOutcome.Cancelled, span.Listeners[0].Outcome);
        }

        [Test]
        public void OrderAsync_SyncHandlerThroughAsyncVerb_CompletesSynchronously()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Obey<MoveCommand>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            Awaitable.Awaiter awaiter = bus.OrderAsync(new MoveCommand()).GetAwaiter();

            Assert.IsTrue(awaiter.IsCompleted);
            CollectionAssert.AreEqual(new[] { "span+", "listener+", "listener-", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Completed, recorder.SpansBegan[0].Outcome);
        }

        [Test]
        public void OrderAsync_NoHandler_FaultedSpan()
        {
            EventBus bus = new EventBus();
            Recorder recorder = new Recorder(bus);

            LogAssert.Expect(LogType.Error, new Regex("No handler is registered for command"));
            bus.OrderAsync(new MoveCommand()).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "span+", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Faulted, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count);
        }

        [Test]
        public void OrderAsync_AlreadyCancelled_CancelledSpan()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Obey<MoveCommand>(owner, _ => { });
            Recorder recorder = new Recorder(bus);

            bus.OrderAsync(new MoveCommand(), new CancellationToken(canceled: true));

            CollectionAssert.AreEqual(new[] { "span+", "span-" }, recorder.Sequence);
            Assert.AreEqual(DispatchOutcome.Cancelled, recorder.SpansBegan[0].Outcome);
            Assert.AreEqual(0, recorder.SpansBegan[0].Listeners.Count, "An already-cancelled order never invokes its handler.");
        }

        [Test]
        public void AskAsync_AsyncHandler_BlockCarriesAnswer()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource<int> handler = new AwaitableCompletionSource<int>();
            bus.Answer<SumRequest, int>(owner, _ => handler.Awaitable);
            Recorder recorder = new Recorder(bus);

            Awaitable<int>.Awaiter awaiter = bus.AskAsync(new SumRequest { A = 1, B = 2 }).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            handler.SetResult(3);
            Assert.AreEqual(3, awaiter.GetResult());

            DispatchSpan span = recorder.SpansBegan[0];
            Assert.AreEqual(EventKind.Request, span.Kind);
            Assert.AreEqual(DispatchOutcome.Completed, span.Outcome);
            Assert.AreEqual(RegistrationRole.RequestHandler, span.Listeners[0].Role);
            Assert.AreEqual(DispatchOutcome.Completed, span.Listeners[0].Outcome);
        }

        #endregion

    }

}
#endif
