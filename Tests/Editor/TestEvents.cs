namespace SideXP.Broadcaster.Tests
{

    // Every event here is flagged Hidden so it stays out of the Events window catalog by default: these are the package's own test
    // fixtures, not vocabulary a project using Broadcaster authors, so they'd only be noise in the window (revealable via its eye toggle).
    // Each also declares a "Broadcaster/Tests/<TypeName>" display-name path so that, once revealed, they group under one folder in the tree;
    // the leaf segment uses nameof so it follows a type rename.

    /// <summary>
    /// Struct signal used across the signal tests, exercises the no-boxing struct-payload path.
    /// </summary>
    [Event(Name = "Broadcaster/Tests/" + nameof(PingSignal), Hidden = true)]
    internal struct PingSignal : ISignal
    {
        public int Value;
    }

    /// <summary>
    /// A second, unrelated struct signal, proves per-type isolation and cross-type cleanup.
    /// </summary>
    [Event(Name = "Broadcaster/Tests/" + nameof(PongSignal), Hidden = true)]
    internal struct PongSignal : ISignal
    {
        public string Text;
    }

    /// <summary>
    /// Class signal with a derived type, used to prove exact-type dispatch: emitting <see cref="DerivedSignal"/>
    /// must never reach a <see cref="BaseSignal"/> listener.
    /// </summary>
    [Event(Name = "Broadcaster/Tests/" + nameof(BaseSignal), Hidden = true)]
    internal class BaseSignal : ISignal
    {
        public int Value;
    }

    /// <inheritdoc cref="BaseSignal"/>
    [Event(Name = "Broadcaster/Tests/" + nameof(DerivedSignal), Hidden = true)]
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
    [Event(Name = "Broadcaster/Tests/" + nameof(MoveCommand), Hidden = true)]
    internal struct MoveCommand : ICommand
    {
        public int Steps;
    }

    /// <summary>
    /// A valued command. Its handler performs an action and reports the outcome (here, the doubled input). Sibling of
    /// <see cref="ICommand"/>, never inheriting it, so overload resolution on <c>Order</c> stays unambiguous.
    /// </summary>
    [Event(Name = "Broadcaster/Tests/" + nameof(DoubleCommand), Hidden = true)]
    internal struct DoubleCommand : ICommand<int>
    {
        public int Value;
    }

    /// <summary>
    /// A request. Its single handler answers with a value derived from the payload (here, the sum) without mutating state.
    /// </summary>
    [Event(Name = "Broadcaster/Tests/" + nameof(SumRequest), Hidden = true)]
    internal struct SumRequest : IRequest<int>
    {
        public int A;
        public int B;
    }

    /// <summary>
    /// A cue. 0..N performers react to it (instant, durative, or callback-style) and a sender may await when they've all finished.
    /// </summary>
    [Event(Name = "Broadcaster/Tests/" + nameof(FlashCue), Hidden = true)]
    internal struct FlashCue : ICue
    {
        public int Value;
    }

}
