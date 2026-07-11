namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Struct signal used across the signal tests — exercises the no-boxing struct-payload path.
    /// </summary>
    internal struct PingSignal : ISignal
    {
        public int Value;
    }

    /// <summary>
    /// A second, unrelated struct signal — proves per-type isolation and cross-type cleanup.
    /// </summary>
    internal struct PongSignal : ISignal
    {
        public string Text;
    }

    /// <summary>
    /// Class signal with a derived type, used to prove exact-type dispatch: emitting <see cref="DerivedSignal"/>
    /// must never reach a <see cref="BaseSignal"/> listener.
    /// </summary>
    internal class BaseSignal : ISignal
    {
        public int Value;
    }

    /// <inheritdoc cref="BaseSignal"/>
    internal class DerivedSignal : BaseSignal { }

    /// <summary>
    /// A stable callback target for the method-group unsubscribe regression: two references to <see cref="OnPing"/> are
    /// distinct delegate instances but compare equal under <see cref="System.Delegate.Equals(object)"/> (same target +
    /// method).
    /// </summary>
    internal class Counter
    {
        public int Count;

        public void OnPing(PingSignal signal) => Count++;
    }

}
