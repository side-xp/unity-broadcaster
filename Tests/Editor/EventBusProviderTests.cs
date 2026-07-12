using System;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// State &amp; provider tests: <see cref="EventBus.Provide{T}"/>, <see cref="EventBus.TryGetCurrent{T}"/>, the
    /// <c>init</c> pull on subscribe, the one-provider-per-type rule, and provider cleanup. Every test uses a fresh
    /// <see cref="EventBus"/>.
    /// </summary>
    public class EventBusProviderTests
    {

        #region init pull

        [Test]
        public void Subscribe_InitWithLiveProvider_InvokesImmediatelyWithCurrentValue()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int received = -1;

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 7 });
            bus.Subscribe<PingSignal>(owner, signal => received = signal.Value, init: true);

            Assert.AreEqual(7, received);
        }

        [Test]
        public void Subscribe_InitWithoutProvider_DoesNotInvoke()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int calls = 0;

            bus.Subscribe<PingSignal>(owner, _ => calls++, init: true);

            Assert.AreEqual(0, calls, "init with no live provider must do nothing.");
        }

        [Test]
        public void Subscribe_InitFalse_DoesNotPullEvenWithProvider()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int calls = 0;

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 7 });
            bus.Subscribe<PingSignal>(owner, _ => calls++); // init defaults to false

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Subscribe_Init_StillReceivesLaterEmits()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int calls = 0;

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });
            bus.Subscribe<PingSignal>(owner, _ => calls++, init: true); // once, immediately
            bus.Emit(new PingSignal { Value = 2 });                     // and again on emit

            Assert.AreEqual(2, calls);
        }

        [Test]
        public void Subscribe_InitAfterProviderOwnerReleased_IsNoOp()
        {
            EventBus bus = new EventBus();
            object providerOwner = new object();
            object subscriberOwner = new object();
            int calls = 0;

            bus.Provide<PingSignal>(providerOwner, () => new PingSignal { Value = 5 });
            bus.UnsubscribeAll(providerOwner); // provider dies with its owner

            bus.Subscribe<PingSignal>(subscriberOwner, _ => calls++, init: true);

            Assert.AreEqual(0, calls, "A released provider must not feed a later init (anti-stale).");
        }

        [Test]
        public void Subscribe_InitWithThrowingProvider_IsIsolatedAndSubscriptionStands()
        {
            EventBus bus = new EventBus();
            object providerOwner = new object();
            object subscriberOwner = new object();
            int received = 0;

            bus.Provide<PingSignal>(providerOwner, () => throw new InvalidOperationException("boom"));

            // The provider's exception is isolated (logged against the provider's owner), never thrown at the subscriber.
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            SubscriptionHandle handle = default;
            Assert.DoesNotThrow(() => handle = bus.Subscribe<PingSignal>(subscriberOwner, _ => received++, init: true));

            Assert.AreEqual(0, received, "The init pull is skipped when the provider throws.");
            Assert.IsTrue(handle.IsActive, "The subscription itself still stands.");

            bus.Emit(new PingSignal { Value = 1 });
            Assert.AreEqual(1, received, "The listener registered by the throwing-init call receives later emits.");
        }

        [Test]
        public void Subscribe_InitProviderReEntersBus_IsSafe()
        {
            // The init pull runs the provider's callback inside the bus: a provider that re-enters the bus from there
            // (registers something, removes itself) must never corrupt the subscription being made.
            EventBus bus = new EventBus();
            object providerOwner = new object();
            object subscriberOwner = new object();

            SubscriptionHandle providerHandle = default;
            providerHandle = bus.Provide<PingSignal>(providerOwner, () =>
            {
                bus.Subscribe<PongSignal>(providerOwner, _ => { });
                providerHandle.Dispose(); // a one-shot provider removing itself mid-pull
                return new PingSignal { Value = 7 };
            });

            int received = 0;
            Assert.DoesNotThrow(() => bus.Subscribe<PingSignal>(subscriberOwner, signal => received = signal.Value, init: true));

            Assert.AreEqual(7, received, "The init pull still delivers the value the re-entrant provider returned.");
            Assert.IsFalse(bus.TryGetCurrent<PingSignal>(out _), "The provider removed itself during the pull.");
        }

        #endregion


        #region Provide & TryGetCurrent

        [Test]
        public void TryGetCurrent_WithProvider_ReturnsCurrentValueLazily()
        {
            EventBus bus = new EventBus();
            object owner = new object();
            int backing = 3;

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = backing });

            Assert.IsTrue(bus.TryGetCurrent<PingSignal>(out PingSignal first));
            Assert.AreEqual(3, first.Value);

            // Never cached: a later read reflects the new backing value.
            backing = 9;
            bus.TryGetCurrent<PingSignal>(out PingSignal second);
            Assert.AreEqual(9, second.Value);
        }

        [Test]
        public void TryGetCurrent_WithoutProvider_ReturnsFalseAndDefault()
        {
            EventBus bus = new EventBus();

            Assert.IsFalse(bus.TryGetCurrent<PingSignal>(out PingSignal current));
            Assert.AreEqual(0, current.Value);
        }

        [Test]
        public void Provide_Duplicate_LogsErrorAndKeepsFirst()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });

            LogAssert.Expect(LogType.Error, new Regex("already registered"));
            SubscriptionHandle second = bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 2 });

            Assert.IsFalse(second.IsActive, "The rejected duplicate returns an inactive handle.");
            bus.TryGetCurrent<PingSignal>(out PingSignal current);
            Assert.AreEqual(1, current.Value, "The first provider stays authoritative.");
        }

        [Test]
        public void Provide_NullArguments_Throw()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            Assert.Throws<ArgumentNullException>(() => bus.Provide<PingSignal>(null, () => new PingSignal()));
            Assert.Throws<ArgumentNullException>(() => bus.Provide<PingSignal>(owner, null));
        }

        #endregion


        #region Provider replacement (hand-off)

        [Test]
        public void Provide_Replace_SupersedesExistingWithoutError()
        {
            EventBus bus = new EventBus();
            object first = new object();
            object second = new object();

            bus.Provide<PingSignal>(first, () => new PingSignal { Value = 1 });
            // No LogAssert.Expect: replace must NOT log the duplicate error (an unexpected log would fail the test).
            SubscriptionHandle handle = bus.Provide<PingSignal>(second, () => new PingSignal { Value = 2 }, replace: true);

            Assert.IsTrue(handle.IsActive);
            bus.TryGetCurrent<PingSignal>(out PingSignal current);
            Assert.AreEqual(2, current.Value, "The replacing provider is now authoritative.");
        }

        [Test]
        public void Provide_ReplaceWhenNoneExists_RegistersNormally()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            SubscriptionHandle handle = bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 4 }, replace: true);

            Assert.IsTrue(handle.IsActive);
            bus.TryGetCurrent<PingSignal>(out PingSignal current);
            Assert.AreEqual(4, current.Value);
        }

        [Test]
        public void Provide_Replace_OldOwnerCleanupDoesNotRemoveNewProvider()
        {
            // The additive-scene hand-off: the incoming provider replaces the outgoing one while both owners are alive,
            // then the outgoing owner is cleaned up. The new provider must survive.
            EventBus bus = new EventBus();
            object outgoing = new object();
            object incoming = new object();

            bus.Provide<PingSignal>(outgoing, () => new PingSignal { Value = 1 });
            bus.Provide<PingSignal>(incoming, () => new PingSignal { Value = 2 }, replace: true);

            bus.UnsubscribeAll(outgoing); // outgoing scene unloads after the hand-off

            Assert.IsTrue(bus.TryGetCurrent<PingSignal>(out PingSignal current), "The incoming provider must remain.");
            Assert.AreEqual(2, current.Value);
        }

        [Test]
        public void Provide_Replace_OldHandleBecomesInactive()
        {
            EventBus bus = new EventBus();
            object first = new object();
            object second = new object();

            SubscriptionHandle firstHandle = bus.Provide<PingSignal>(first, () => new PingSignal { Value = 1 });
            bus.Provide<PingSignal>(second, () => new PingSignal { Value = 2 }, replace: true);

            Assert.IsFalse(firstHandle.IsActive, "The superseded provider's handle reports inactive.");

            // Disposing the stale handle must not disturb the current provider.
            firstHandle.Dispose();
            Assert.IsTrue(bus.TryGetCurrent<PingSignal>(out PingSignal current));
            Assert.AreEqual(2, current.Value);
        }

        #endregion


        #region Provider cleanup

        [Test]
        public void Provider_HandleDispose_RemovesProvider()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            SubscriptionHandle handle = bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });
            handle.Dispose();

            Assert.IsFalse(bus.TryGetCurrent<PingSignal>(out _));
        }

        [Test]
        public void Provider_UnsubscribeAll_RemovesProviderAndCounts()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });
            bus.Subscribe<PingSignal>(owner, _ => { });

            int removed = bus.UnsubscribeAll(owner);

            Assert.AreEqual(2, removed, "Both the listener and the provider are counted.");
            Assert.IsFalse(bus.TryGetCurrent<PingSignal>(out _));
        }

        [Test]
        public void Provide_AfterFirstRemoved_Succeeds()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            SubscriptionHandle first = bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });
            first.Dispose();

            SubscriptionHandle second = bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 2 });

            Assert.IsTrue(second.IsActive);
            bus.TryGetCurrent<PingSignal>(out PingSignal current);
            Assert.AreEqual(2, current.Value);
        }

        [Test]
        public void ClearOfType_RemovesProvider()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });
            bus.Clear<PingSignal>();

            Assert.IsFalse(bus.TryGetCurrent<PingSignal>(out _));
        }

        [Test]
        public void Clear_RemovesProviders()
        {
            EventBus bus = new EventBus();
            object owner = new object();

            bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });
            bus.Clear();

            Assert.IsFalse(bus.TryGetCurrent<PingSignal>(out _));
        }

        #endregion

    }

}
