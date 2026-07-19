namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Extra event types for the Events window (catalog/draft/dispatcher) tests, on top of the shared ones in
    /// <c>TestEvents.cs</c>: they exercise attribute metadata, empty payloads, non-instantiable types, and open generics.
    /// </summary>

    /// <summary>A signal carrying <c>[Broadcast]</c> metadata, to prove the catalog reads the description, opt-out and hidden flag.</summary>
    [Event(Description = "A described signal.", OmitSnapshot = true, Hidden = true)]
    internal struct DescribedSignal : ISignal
    {
        public int Value;
    }

    /// <summary>
    /// A signal with no fields, proving the emit box handles an empty payload. Its catalog visibility is gated on <c>SIDEXP_DEMOS</c>: with
    /// the demos enabled it carries no <c>[Event]</c> attribute, so it stays visible and doubles as the fixture proving a type with no
    /// metadata still catalogs (null description, nothing omitted, not hidden) — the common case for a project's own events. Without the
    /// define it's marked hidden, so a package consumer who never opted into the demos doesn't see this test fixture in the window.
    /// </summary>
#if !SIDEXP_DEMOS
    [Event(Hidden = true)]
#endif
    internal struct EmptySignal : ISignal { }

    /// <summary>A command class with no public parameterless constructor, so a draft can't instantiate it.</summary>
    [Event(Hidden = true)]
    internal class NoDefaultCtorCommand : ICommand
    {
        public int Steps;

        public NoDefaultCtorCommand(int steps)
        {
            Steps = steps;
        }
    }

    /// <summary>An open generic signal, to prove the catalog rejects generic type definitions.</summary>
    internal class OpenSignal<T> : ISignal { }

}
