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
    /// Cue tests: the three <c>Perform</c> shapes (instant / durative / callback-style), synchronous start in registration order,
    /// when-all completion, zero performers, per-performer exception isolation, cancellation fan-out, and the never-hangs guarantee
    /// (a performer unregistered mid-cue still lets the cue resolve). Every test uses a fresh <see cref="EventBus"/>.
    /// <br/>
    /// Like the async tests these run in EditMode and stay synchronous: a durative performer's completion is driven through a
    /// test-owned <see cref="AwaitableCompletionSource"/> (setting its result resumes the cue's bookkeeping on the same call).
    /// </summary>
    public class EventBusCueTests
    {

        #region Completion & ordering

        [Test]
        public void Cue_ZeroPerformers_CompletesInstantly()
        {
            EventBus bus = new EventBus();

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();

            Assert.IsTrue(awaiter.IsCompleted, "A cue with no performers completes immediately.");
            awaiter.GetResult(); // must not throw
        }

        [Test]
        public void Cue_InstantPerformers_RunInRegistrationOrderAndComplete()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            List<int> order = new List<int>();

            bus.Perform<FlashCue>(owner, _ => order.Add(1));
            bus.Perform<FlashCue>(owner, _ => order.Add(2));
            bus.Perform<FlashCue>(owner, _ => order.Add(3));

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();

            Assert.IsTrue(awaiter.IsCompleted, "Instant performers complete within the Cue call.");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
        }

        [Test]
        public void Cue_DurativePerformer_WaitsForItToFinish()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource performer = new AwaitableCompletionSource();

            bus.Perform<FlashCue>(owner, _ => performer.Awaitable);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted, "The cue waits on the durative performer.");

            performer.SetResult();
            Assert.IsTrue(awaiter.IsCompleted);
            awaiter.GetResult();
        }

        [Test]
        public void Cue_MixedPerformers_CompletesWhenTheLastFinishes()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool instantRan = false;
            AwaitableCompletionSource slow = new AwaitableCompletionSource();

            bus.Perform<FlashCue>(owner, _ => instantRan = true);
            bus.Perform<FlashCue>(owner, _ => slow.Awaitable);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            Assert.IsTrue(instantRan, "The instant performer ran synchronously.");
            Assert.IsFalse(awaiter.IsCompleted, "The cue is when-all: it waits for the durative performer.");

            slow.SetResult();
            Assert.IsTrue(awaiter.IsCompleted);
        }

        [Test]
        public void Cue_StartsEveryPerformerSynchronously()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool durativeStarted = false;
            AwaitableCompletionSource gate = new AwaitableCompletionSource();

            // A durative performer whose synchronous stretch flips a flag before the first await.
            bus.Perform<FlashCue>(owner, _ => { durativeStarted = true; return gate.Awaitable; });

            bus.Cue(new FlashCue());
            Assert.IsTrue(durativeStarted, "A durative performer's synchronous stretch runs during the Cue call, not later.");

            gate.SetResult();
        }

        [Test]
        public void Cue_CallbackPerformer_CompletesWhenDoneIsCalled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            Action done = null;

            bus.Perform<FlashCue>(owner, (_, finish) => done = finish);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted, "A callback performer keeps the cue open until it signals completion.");
            Assert.IsNotNull(done);

            done.Invoke();
            Assert.IsTrue(awaiter.IsCompleted);
        }

        [Test]
        public void Cue_AsyncLambda_BindsToDurativeOverload()
        {
            // Compile-time + behavioural proof: an async lambda binds to the Func<T, Awaitable> overload (durative), not the instant
            // Action<T>, so the cue waits for it.
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource gate = new AwaitableCompletionSource();

            bus.Perform<FlashCue>(owner, async _ => await gate.Awaitable);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted, "An async performer is durative; the cue waits.");

            gate.SetResult();
            Assert.IsTrue(awaiter.IsCompleted);
        }

        #endregion


        #region Isolation

        [Test]
        public void Cue_InstantPerformerThrows_OthersStillRunAndCueResolves()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool secondRan = false;

            // Typed local so the throw-expression lambda binds to the instant overload rather than being ambiguous.
            Action<FlashCue> throwing = _ => throw new InvalidOperationException("boom");
            bus.Perform<FlashCue>(owner, throwing);
            bus.Perform<FlashCue>(owner, _ => secondRan = true);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();

            Assert.IsTrue(secondRan, "A throwing performer never stops the others.");
            Assert.IsTrue(awaiter.IsCompleted);
            awaiter.GetResult(); // the cue itself does not fault
        }

        [Test]
        public void Cue_DurativePerformerFaults_IsolatedAndCueResolves()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource faulting = new AwaitableCompletionSource();
            bool otherCompleted = false;
            AwaitableCompletionSource other = new AwaitableCompletionSource();

            bus.Perform<FlashCue>(owner, _ => faulting.Awaitable);
            bus.Perform<FlashCue>(owner, _ => { otherCompleted = true; return other.Awaitable; });

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            faulting.SetException(new InvalidOperationException("boom"));
            Assert.IsFalse(awaiter.IsCompleted, "The cue still waits on the healthy performer.");
            Assert.IsTrue(otherCompleted);

            other.SetResult();
            Assert.IsTrue(awaiter.IsCompleted);
            awaiter.GetResult(); // the cue does not fault on a performer's fault
        }

        #endregion


        #region Cancellation

        [Test]
        public void Cue_TokenAlreadyCancelled_ResolvesCancelled()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool ran = false;
            bus.Perform<FlashCue>(owner, _ => ran = true);

            CancellationToken cancelled = new CancellationToken(canceled: true);
            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue(), cancelled).GetAwaiter();

            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
            Assert.IsFalse(ran, "An already-cancelled cue never starts its performers.");
        }

        [Test]
        public void Cue_TokenCancelledDuringFlight_ResolvesCancelledAndFansOut()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource first = new AwaitableCompletionSource();
            AwaitableCompletionSource second = new AwaitableCompletionSource();
            bus.Perform<FlashCue>(owner, _ => first.Awaitable);
            bus.Perform<FlashCue>(owner, _ => second.Awaitable);

            CancellationTokenSource cts = new CancellationTokenSource();
            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue(), cts.Token).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            // Cancellation fans out to both in-flight performers, resolving the cue as cancelled.
            cts.Cancel();
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());
        }

        [Test]
        public void Cue_CallbackPerformerCallsDoneAfterCancel_Ignored()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            Action done = null;
            bus.Perform<FlashCue>(owner, (_, finish) => done = finish);

            CancellationTokenSource cts = new CancellationTokenSource();
            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue(), cts.Token).GetAwaiter();

            cts.Cancel();
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.Catch<OperationCanceledException>(() => awaiter.GetResult());

            // A late done() from the cancelled performer is a harmless no-op (the slot already drained).
            Assert.DoesNotThrow(() => done.Invoke());
        }

        #endregion


        #region Never-hangs (performer released mid-cue)

        [Test]
        public void Cue_PerformerReleasedMidCue_CueStillResolves()
        {
            EventBus bus = new EventBus();
            object fast = new object();
            object slow = new object();
            bool fastRan = false;
            AwaitableCompletionSource never = new AwaitableCompletionSource(); // never completes

            bus.Perform<FlashCue>(fast, _ => fastRan = true);
            bus.Perform<FlashCue>(slow, _ => never.Awaitable);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            Assert.IsTrue(fastRan);
            Assert.IsFalse(awaiter.IsCompleted, "The slow performer is still in flight.");

            bus.UnsubscribeAll(slow); // the slow performer vanishes before completing

            Assert.IsTrue(awaiter.IsCompleted, "The cue resolves once the released performer's slot drains.");
            awaiter.GetResult(); // completes (not cancelled — this drain wasn't a token cancellation)
        }

        [Test]
        public void Cue_PerformerDisposedMidCue_CueStillResolves()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource never = new AwaitableCompletionSource();
            SubscriptionHandle handle = bus.Perform<FlashCue>(owner, _ => never.Awaitable);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            Assert.IsFalse(awaiter.IsCompleted);

            handle.Dispose();

            Assert.IsTrue(awaiter.IsCompleted);
            awaiter.GetResult();
        }

        [Test]
        public void Cue_PerformersClearedMidCue_CueStillResolves()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            AwaitableCompletionSource never = new AwaitableCompletionSource();
            bus.Perform<FlashCue>(owner, _ => never.Awaitable);

            Awaitable.Awaiter awaiter = bus.Cue(new FlashCue()).GetAwaiter();
            bus.Clear<FlashCue>();

            Assert.IsTrue(awaiter.IsCompleted);
            awaiter.GetResult();
        }

        #endregion


        #region Registration & cleanup

        [Test]
        public void Perform_UnsubscribeAll_RemovesPerformers()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool ran = false;
            bus.Perform<FlashCue>(owner, _ => ran = true);

            Assert.AreEqual(1, bus.UnsubscribeAll(owner));

            bus.Cue(new FlashCue()).GetAwaiter().GetResult();
            Assert.IsFalse(ran, "A removed performer is not invoked.");
        }

        [Test]
        public void Perform_HandleDispose_RemovesPerformer()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool ran = false;
            SubscriptionHandle handle = bus.Perform<FlashCue>(owner, _ => ran = true);

            handle.Dispose();

            bus.Cue(new FlashCue()).GetAwaiter().GetResult();
            Assert.IsFalse(ran);
        }

        [Test]
        public void Cue_PerformerRegisteredDuringDispatch_NotStartedByThisCue()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int runs = 0;

            // Re-entrancy: a performer registered while a cue is dispatching is not invoked by that same cue.
            bus.Perform<FlashCue>(owner, _ =>
            {
                runs++;
                bus.Perform<FlashCue>(owner, __ => runs++);
            });

            bus.Cue(new FlashCue()).GetAwaiter().GetResult();
            Assert.AreEqual(1, runs, "The performer added mid-dispatch is not started by the running cue.");
        }

        [Test]
        public void Cue_SeparateBuses_DoNotCrossTalk()
        {
            EventBus a = new EventBus();
            EventBus b = new EventBus();
            object owner = new object();
            bool ranOnA = false;
            a.Perform<FlashCue>(owner, _ => ranOnA = true);

            b.Cue(new FlashCue()).GetAwaiter().GetResult();
            Assert.IsFalse(ranOnA, "A performer on one bus never reacts to a cue on another.");
        }

        #endregion

    }

}
