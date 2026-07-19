using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Signal-core tests: emit/subscribe round-trips, the dispatch guarantees (registration order, synchrony, exception
    /// isolation, re-entrancy, exact-type dispatch, delegate-equality removal), owner/handle cleanup, and per-instance
    /// isolation. Every test uses a fresh <see cref="EventBus"/>.
    /// </summary>
    public class EventBusSignalTests
    {

        #region Round-trip & ordering

        [Test]
        public void Emit_WithStructPayload_DeliversValueSynchronously()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int received = -1;

            bus.Subscribe<PingSignal>(owner, signal => received = signal.Value);
            bus.Emit(new PingSignal { Value = 42 });

            // Synchronous: the value is already there when Emit returns.
            Assert.AreEqual(42, received);
        }

        [Test]
        public void Emit_ZeroListeners_IsSilent()
        {
            EventBus bus = new EventBus();
            Assert.DoesNotThrow(() => bus.Emit(new PingSignal { Value = 1 }));
        }

        [Test]
        public void Emit_MultipleListeners_InvokedInRegistrationOrder()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            List<int> order = new List<int>();

            bus.Subscribe<PingSignal>(owner, _ => order.Add(1));
            bus.Subscribe<PingSignal>(owner, _ => order.Add(2));
            bus.Subscribe<PingSignal>(owner, _ => order.Add(3));
            bus.Emit(new PingSignal());

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
        }

        #endregion


        #region Exception isolation

        [Test]
        public void Emit_ThrowingListener_DoesNotStopOthers()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool secondRan = false;

            bus.Subscribe<PingSignal>(owner, _ => throw new InvalidOperationException("boom"));
            bus.Subscribe<PingSignal>(owner, _ => secondRan = true);

            // The exception is logged (owner as context) and the next listener still runs.
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            bus.Emit(new PingSignal());

            Assert.IsTrue(secondRan);
        }

        #endregion


        #region Re-entrancy

        [Test]
        public void Emit_ListenerAddedDuringDispatch_NotInvokedByThatDispatch()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool added = false;
            int lateCalls = 0;

            bus.Subscribe<PingSignal>(owner, _ =>
            {
                if (added)
                    return;
                added = true;
                bus.Subscribe<PingSignal>(owner, _2 => lateCalls++);
            });

            bus.Emit(new PingSignal());
            Assert.AreEqual(0, lateCalls, "A listener added during a dispatch must not be invoked by that dispatch.");

            bus.Emit(new PingSignal());
            Assert.AreEqual(1, lateCalls, "A listener added during a previous dispatch is invoked by later ones.");
        }

        [Test]
        public void Emit_ListenerRemovedDuringDispatch_NotInvokedAfterwards()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool secondRan = false;
            SubscriptionHandle second = default;

            // The first listener removes the second before the dispatch reaches it.
            bus.Subscribe<PingSignal>(owner, _ => second.Dispose());
            second = bus.Subscribe<PingSignal>(owner, _ => secondRan = true);

            bus.Emit(new PingSignal());

            Assert.IsFalse(secondRan, "A registration removed during a dispatch must not be invoked afterwards in it.");
        }

        [Test]
        public void Emit_ListenerUnsubscribedDuringDispatch_NotInvokedAfterwards()
        {
            // Same guarantee as the handle-based removal above, through the delegate-based removal route.
            EventBus bus = new EventBus();
            object owner = new object();
            bool secondRan = false;
            Action<PingSignal> second = _ => secondRan = true;

            // The first listener unsubscribes the second (by delegate) before the dispatch reaches it.
            bus.Subscribe<PingSignal>(owner, _ => bus.Unsubscribe(second));
            bus.Subscribe(owner, second);

            bus.Emit(new PingSignal());

            Assert.IsFalse(secondRan, "A registration unsubscribed during a dispatch must not be invoked afterwards in it.");
        }

        [Test]
        public void Emit_ReentrantEmitSameType_DoesNotCorruptIteration()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int outerRuns = 0;
            int innerRuns = 0;
            bool reentered = false;

            bus.Subscribe<PingSignal>(owner, _ =>
            {
                outerRuns++;
                if (reentered)
                    return;
                reentered = true;
                bus.Emit(new PingSignal()); // nested dispatch of the same type
            });
            bus.Subscribe<PingSignal>(owner, _ => innerRuns++);

            Assert.DoesNotThrow(() => bus.Emit(new PingSignal()));
            // Two full dispatches run over the same two listeners (the outer one, and the nested one it triggers), so
            // each listener fires exactly twice, and neither iteration is corrupted by the other.
            Assert.AreEqual(2, outerRuns);
            Assert.AreEqual(2, innerRuns);
        }

        #endregion


        #region Exact-type dispatch

        [Test]
        public void Emit_DerivedSignal_DoesNotReachBaseListener()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int baseCalls = 0;
            int derivedCalls = 0;

            bus.Subscribe<BaseSignal>(owner, _ => baseCalls++);
            bus.Subscribe<DerivedSignal>(owner, _ => derivedCalls++);

            bus.Emit(new DerivedSignal());
            Assert.AreEqual(0, baseCalls, "Exact-type dispatch: a derived emit must not reach a base listener.");
            Assert.AreEqual(1, derivedCalls);

            bus.Emit(new BaseSignal());
            Assert.AreEqual(1, baseCalls);
            Assert.AreEqual(1, derivedCalls);
        }

        [Test]
        public void Emit_InterfaceTypeArgument_LogsDevError()
        {
            // The silent-miss trap of exact-type dispatch: a variable declared as the marker interface infers the type argument
            // as the interface itself, which no concrete listener is ever keyed on. Dev builds must call it out.
            EventBus bus = new EventBus();
            object owner = new object();
            int calls = 0;
            bus.Subscribe<PingSignal>(owner, _ => calls++);

            ISignal signal = new PingSignal();
            LogAssert.Expect(LogType.Error, new Regex("interface or an abstract type"));
            bus.Emit(signal);

            Assert.AreEqual(0, calls, "The emit is keyed on the interface type and reaches no concrete listener.");
        }

        [Test]
        public void Subscribe_InterfaceTypeArgument_LogsDevError()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            LogAssert.Expect(LogType.Error, new Regex("interface or an abstract type"));
            bus.Subscribe<ISignal>(owner, _ => { });
        }

        #endregion


        #region Delegate-based removal

        [Test]
        public void Unsubscribe_MethodGroup_RemovesByDelegateEquality()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            Counter counter = new Counter();

            bus.Subscribe<PingSignal>(owner, counter.OnPing);
            // A second `counter.OnPing` is a distinct delegate instance; removal must still match it by target + method.
            bool removed = bus.Unsubscribe<PingSignal>(counter.OnPing);

            Assert.IsTrue(removed);
            bus.Emit(new PingSignal());
            Assert.AreEqual(0, counter.Count);
        }

        [Test]
        public void Unsubscribe_UnknownListener_ReturnsFalse()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            Counter counter = new Counter();

            bus.Subscribe<PingSignal>(owner, counter.OnPing);
            bool removed = bus.Unsubscribe<PingSignal>(_ => { });

            Assert.IsFalse(removed);
            bus.Emit(new PingSignal());
            Assert.AreEqual(1, counter.Count, "The genuine listener must be untouched.");
        }

        #endregion


        #region Handles

        [Test]
        public void Handle_Dispose_UnregistersAndReportsInactive()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool ran = false;

            SubscriptionHandle handle = bus.Subscribe<PingSignal>(owner, _ => ran = true);
            Assert.IsTrue(handle.IsActive);

            handle.Dispose();
            Assert.IsFalse(handle.IsActive);

            bus.Emit(new PingSignal());
            Assert.IsFalse(ran);

            Assert.DoesNotThrow(() => handle.Dispose(), "Dispose must be idempotent.");
        }

        [Test]
        public void Handle_Default_IsInactiveAndSafeToDispose()
        {
            SubscriptionHandle handle = default;
            Assert.IsFalse(handle.IsActive);
            Assert.DoesNotThrow(() => handle.Dispose());
        }

        #endregion


        #region Owner cleanup & clearing

        [Test]
        public void UnsubscribeAll_RemovesEveryRegistrationOfOwnerAcrossTypes()
        {
            EventBus bus = new EventBus();
            object owner1 = new object();
            object owner2 = new object();
            int owner2Calls = 0;

            bus.Subscribe<PingSignal>(owner1, _ => { });
            bus.Subscribe<PongSignal>(owner1, _ => { });
            bus.Subscribe<PingSignal>(owner2, _ => owner2Calls++);

            int removed = bus.UnregisterAll(owner1);
            Assert.AreEqual(2, removed);

            bus.Emit(new PingSignal());
            Assert.AreEqual(1, owner2Calls, "Only the given owner's registrations are removed.");
        }

        [Test]
        public void UnsubscribeAll_UnknownOwner_RemovesNothing()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bus.Subscribe<PingSignal>(owner, _ => { });

            Assert.AreEqual(0, bus.UnregisterAll(new object()));
        }

        [Test]
        public void Clear_RemovesAllRegistrations()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            bool ran = false;

            bus.Subscribe<PingSignal>(owner, _ => ran = true);
            bus.Clear();
            bus.Emit(new PingSignal());

            Assert.IsFalse(ran);
        }

        [Test]
        public void ClearOfType_RemovesOnlyThatType()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int pings = 0;
            int pongs = 0;

            bus.Subscribe<PingSignal>(owner, _ => pings++);
            bus.Subscribe<PongSignal>(owner, _ => pongs++);

            bus.Clear<PingSignal>();
            bus.Emit(new PingSignal());
            bus.Emit(new PongSignal());

            Assert.AreEqual(0, pings);
            Assert.AreEqual(1, pongs);
        }

        #endregion


        #region Per-instance isolation & validation

        [Test]
        public void TwoInstances_DoNotCrossTalk()
        {
            EventBus a = new EventBus();
            EventBus b = new EventBus();
            object owner = new object();
            int aCalls = 0;

            a.Subscribe<PingSignal>(owner, _ => aCalls++);

            b.Emit(new PingSignal());
            Assert.AreEqual(0, aCalls, "Emitting on one bus must not reach listeners on another.");

            a.Emit(new PingSignal());
            Assert.AreEqual(1, aCalls);
        }

        [Test]
        public void Subscribe_NullArguments_Throw()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<PingSignal>(null, _ => { }));
            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<PingSignal>(owner, null));
        }

        #endregion

    }

}
