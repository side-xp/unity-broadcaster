using System;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// The Timeline window's filter: which recorded dispatches it shows, decided by event kind, a text query on the event type name, and an
    /// optional owner. It's a plain predicate over spans (and violations) so the window stays a thin renderer over tested matching logic.
    /// </summary>
    /// <remarks>
    /// Every axis is combined with AND: a span shows when its kind is enabled, its type name matches the query, and one of the callbacks
    /// it invoked was registered by that owner (when an owner is set). All kinds are enabled and no owner is set by default, so a fresh
    /// filter matches everything. Per-field payload matching is intentionally not here yet; it lands as a later addition.
    /// </remarks>
    public sealed class TimelineFilter
    {

        // One flag per EventKind, indexed by (int)kind. All true by default.
        private readonly bool[] _kinds = { true, true, true, true };
        private string _typeQuery = string.Empty;
        private object _owner;

        /// <summary>Raised whenever a filter setting changed, so the window can re-run the filter and repaint.</summary>
        public event Action Changed;

        /// <summary>
        /// A case-insensitive substring matched against each event type's name. Empty (the default) matches every type. Null is treated as
        /// empty.
        /// </summary>
        public string TypeQuery
        {
            get => _typeQuery;
            set
            {
                string normalized = value ?? string.Empty;
                if (normalized == _typeQuery)
                    return;

                _typeQuery = normalized;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// When set, only dispatches that invoked a callback registered by this owner are shown (and violations attributed to it). Null (the
        /// default) matches every owner.
        /// </summary>
        public object Owner
        {
            get => _owner;
            set
            {
                if (ReferenceEquals(value, _owner))
                    return;

                _owner = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Whether dispatches of <paramref name="kind"/> are shown.</summary>
        public bool IsKindEnabled(EventKind kind) => _kinds[(int)kind];

        /// <summary>Shows or hides dispatches of <paramref name="kind"/>.</summary>
        public void SetKindEnabled(EventKind kind, bool enabled)
        {
            if (_kinds[(int)kind] == enabled)
                return;

            _kinds[(int)kind] = enabled;
            Changed?.Invoke();
        }

        /// <summary>
        /// Whether <paramref name="span"/> passes every active filter: its kind is enabled, its type name matches the query, and (when an
        /// owner is set) it invoked a callback registered by that owner.
        /// </summary>
        public bool Matches(DispatchSpan span)
        {
            if (span == null)
                return false;
            if (!IsKindEnabled(span.Kind))
                return false;
            if (!MatchesTypeQuery(span.EventType))
                return false;
            if (_owner != null && !InvolvesOwner(span))
                return false;

            return true;
        }

        /// <summary>
        /// Whether <paramref name="violation"/> passes the type and owner filters. Its kind is a misuse category, not an event kind, so the
        /// kind toggles don't apply; a violation with no attributed owner passes the owner filter only when no owner is set.
        /// </summary>
        public bool Matches(Violation violation)
        {
            if (!MatchesTypeQuery(violation.EventType))
                return false;
            if (_owner != null && !ReferenceEquals(violation.Owner, _owner))
                return false;

            return true;
        }

        private bool MatchesTypeQuery(Type eventType)
        {
            if (_typeQuery.Length == 0)
                return true;
            if (eventType == null)
                return false;

            return eventType.Name.IndexOf(_typeQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool InvolvesOwner(DispatchSpan span)
        {
            foreach (ListenerSpan listener in span.Listeners)
            {
                if (ReferenceEquals(listener.Owner, _owner))
                    return true;
            }
            return false;
        }

    }

}
