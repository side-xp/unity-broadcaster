using NUnit.Framework;

using SideXP.Broadcaster.EditorOnly;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Live-index tests: the window's per-type tally is fed only by the bus's registration hooks. Registering and removing (by handle,
    /// owner or clear) must keep the counts and single-slot owners correct. Each test uses a fresh bus.
    /// </summary>
    public class EventBusLiveIndexTests
    {

        [Test]
        public void Listeners_CountUpAndDown()
        {
            EventBus bus = new EventBus();
            EventBusLiveIndex index = new EventBusLiveIndex();
            index.Attach(bus);
            object owner = new object();

            SubscriptionHandle a = bus.Subscribe<PingSignal>(owner, _ => { });
            bus.Subscribe<PingSignal>(owner, _ => { });

            Assert.IsTrue(index.TryGet(typeof(PingSignal), out EventLiveState twoState));
            Assert.AreEqual(2, twoState.Listeners);

            a.Dispose();
            index.TryGet(typeof(PingSignal), out EventLiveState oneState);
            Assert.AreEqual(1, oneState.Listeners);
        }

        [Test]
        public void Provider_TracksPresenceAndOwner()
        {
            EventBus bus = new EventBus();
            EventBusLiveIndex index = new EventBusLiveIndex();
            index.Attach(bus);
            object owner = new object();

            SubscriptionHandle handle = bus.Provide<PingSignal>(owner, () => new PingSignal { Value = 1 });

            Assert.IsTrue(index.TryGet(typeof(PingSignal), out EventLiveState alive));
            Assert.IsTrue(alive.HasProvider);
            Assert.AreSame(owner, alive.ProviderOwner);

            handle.Dispose();
            index.TryGet(typeof(PingSignal), out EventLiveState gone);
            Assert.IsFalse(gone.HasProvider);
            Assert.IsNull(gone.ProviderOwner);
        }

        [Test]
        public void Handler_TracksPresenceAndOwner()
        {
            EventBus bus = new EventBus();
            EventBusLiveIndex index = new EventBusLiveIndex();
            index.Attach(bus);
            object owner = new object();

            bus.Answer<SumRequest, int>(owner, req => req.A + req.B);

            Assert.IsTrue(index.TryGet(typeof(SumRequest), out EventLiveState state));
            Assert.IsTrue(state.HasHandler);
            Assert.AreSame(owner, state.HandlerOwner);
        }

        [Test]
        public void Performers_CountThenClearedByUnsubscribeAll()
        {
            EventBus bus = new EventBus();
            EventBusLiveIndex index = new EventBusLiveIndex();
            index.Attach(bus);
            object owner = new object();

            bus.Perform<FlashCue>(owner, _ => { });
            bus.Perform<FlashCue>(owner, _ => { });

            index.TryGet(typeof(FlashCue), out EventLiveState two);
            Assert.AreEqual(2, two.Performers);

            bus.UnsubscribeAll(owner);
            index.TryGet(typeof(FlashCue), out EventLiveState none);
            Assert.AreEqual(0, none.Performers);
        }

        [Test]
        public void Detach_StopsTracking()
        {
            EventBus bus = new EventBus();
            EventBusLiveIndex index = new EventBusLiveIndex();
            index.Attach(bus);
            object owner = new object();

            index.Detach();
            bus.Subscribe<PingSignal>(owner, _ => { });

            Assert.IsFalse(index.TryGet(typeof(PingSignal), out _));
        }

        [Test]
        public void Attach_ToNewBus_ClearsPreviousTally()
        {
            EventBus first = new EventBus();
            EventBusLiveIndex index = new EventBusLiveIndex();
            index.Attach(first);
            object owner = new object();
            first.Subscribe<PingSignal>(owner, _ => { });

            EventBus second = new EventBus();
            index.Attach(second);

            Assert.IsFalse(index.TryGet(typeof(PingSignal), out _));
        }

    }

}
