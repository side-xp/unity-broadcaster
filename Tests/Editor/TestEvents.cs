namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Struct signal used across the signal tests, exercises the no-boxing struct-payload path.
    /// </summary>
    internal struct PingSignal : ISignal
    {
        public int Value;
    }

    /// <summary>
    /// A second, unrelated struct signal, proves per-type isolation and cross-type cleanup.
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

    /// <summary>
    /// A void command. Has a handler that performs an action and acknowledges it, but reports no outcome.
    /// </summary>
    internal struct MoveCommand : ICommand
    {
        public int Steps;
    }

    /// <summary>
    /// A valued command. Its handler performs an action and reports the outcome (here, the doubled input). Sibling of
    /// <see cref="ICommand"/>, never inheriting it, so overload resolution on <c>Order</c> stays unambiguous.
    /// </summary>
    internal struct DoubleCommand : ICommand<int>
    {
        public int Value;
    }

    /// <summary>
    /// A request. Its single handler answers with a value derived from the payload (here, the sum) without mutating state.
    /// </summary>
    internal struct SumRequest : IRequest<int>
    {
        public int A;
        public int B;
    }

    /// <summary>
    /// A cue. 0..N performers react to it (instant, durative, or callback-style) and a sender may await when they've all finished.
    /// </summary>
    internal struct FlashCue : ICue
    {
        public int Value;
    }

}
