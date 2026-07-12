using NUnit.Framework;

namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Verifies the static <see cref="Broadcaster"/> façade forwards to its default <see cref="EventBus"/>. This is the
    /// one suite that touches the shared façade, so it resets the default bus around every test.
    /// </summary>
    public class BroadcasterFacadeTests
    {

        [SetUp]
        [TearDown]
        public void ResetDefault() => Broadcaster.Clear();

        [Test]
        public void Default_IsStableSingleton()
        {
            Assert.IsNotNull(Broadcaster.Default);
            Assert.AreSame(Broadcaster.Default, Broadcaster.Default);
        }

        [Test]
        public void Emit_ForwardsToDefaultBus()
        {
            object owner = new object();
            int calls = 0;

            Broadcaster.Subscribe<PingSignal>(owner, _ => calls++);
            Broadcaster.Emit(new PingSignal());

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void UnsubscribeAll_ForwardsToDefaultBus()
        {
            object owner = new object();
            int calls = 0;

            Broadcaster.Subscribe<PingSignal>(owner, _ => calls++);
            Broadcaster.UnsubscribeAll(owner);
            Broadcaster.Emit(new PingSignal());

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Cue_ForwardsToDefaultBus()
        {
            object owner = new object();
            bool performed = false;

            Broadcaster.Perform<FlashCue>(owner, _ => performed = true);
            Broadcaster.Cue(new FlashCue()).GetAwaiter().GetResult();

            Assert.IsTrue(performed);
        }

    }

}
