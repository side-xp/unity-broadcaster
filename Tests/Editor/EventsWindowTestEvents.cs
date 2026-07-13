namespace SideXP.Broadcaster.Tests
{

    /// <summary>
    /// Extra event types for the Events window (catalog/draft/dispatcher) tests, on top of the shared ones in
    /// <c>TestEvents.cs</c>: they exercise attribute metadata, empty payloads, non-instantiable types, and open generics.
    /// </summary>

    /// <summary>A signal carrying <c>[Broadcast]</c> metadata, to prove the catalog reads the description and opt-out.</summary>
    [Event(Description = "A described signal.", OmitSnapshot = true)]
    internal struct DescribedSignal : ISignal
    {
        public int Value;
    }

    /// <summary>A signal with no fields, to prove the emit box handles an empty payload.</summary>
    internal struct EmptySignal : ISignal { }

    /// <summary>A command class with no public parameterless constructor, so a draft can't instantiate it.</summary>
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
