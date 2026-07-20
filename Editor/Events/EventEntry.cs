using System;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// A single event type as the Events window catalogs it: its kind, its display metadata, and (for a valued command or a request) the
    /// type it produces. Purely descriptive, computed once from a <see cref="Type"/> by <see cref="EventCatalog"/>; it holds no live bus
    /// state (the window pairs it with that separately).
    /// </summary>
    public sealed class EventEntry
    {

        /// <summary>The concrete event type this entry describes.</summary>
        public Type EventType { get; }

        /// <summary>Which of the four kinds the type is, decided from the marker interface it implements.</summary>
        public EventKind Kind { get; }

        /// <summary>
        /// The value a valued command or a request produces (the marker's type argument), or <c>null</c> for a signal, a cue or a void
        /// command.
        /// </summary>
        public Type ResultType { get; }

        /// <summary>The C# type name.</summary>
        public string Name { get; }

        /// <summary>
        /// The name to show in the window: the type's <c>[Event(Name = ...)]</c> when it sets one, otherwise <see cref="Name"/>. May be a
        /// <c>/</c>-separated path.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>The namespace the type lives in, or an empty string for the global namespace.</summary>
        public string Namespace { get; }

        /// <summary>The description from the type's <c>[Event]</c> attribute, or <c>null</c> when it carries none.</summary>
        public string Description { get; }

        /// <summary>Whether the type opted out of payload capture with <c>[Event(OmitSnapshot = true)]</c>.</summary>
        public bool OmitSnapshot { get; }

        /// <summary>
        /// Whether the type asked to be kept out of the catalog by default with <c>[Event(Hidden = true)]</c>.
        /// </summary>
        public bool Hidden { get; }

        /// <summary>Whether this entry describes a valued command or a request (one that reports a <see cref="ResultType"/>).</summary>
        public bool HasResult => ResultType != null;

        /// <inheritdoc cref="EventEntry"/>
        public EventEntry(Type eventType, EventKind kind, Type resultType, string customName, string description, bool omitSnapshot, bool hidden)
        {
            EventType = eventType;
            Kind = kind;
            ResultType = resultType;
            Name = eventType.Name;
            DisplayName = string.IsNullOrWhiteSpace(customName) ? Name : customName.Trim();
            Namespace = eventType.Namespace ?? string.Empty;
            Description = description;
            OmitSnapshot = omitSnapshot;
            Hidden = hidden;
        }

    }

}
