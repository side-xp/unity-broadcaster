using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEditor;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// Builds the Events window's catalog: it finds every concrete event type in the project, classifies each by the marker interface it
    /// implements, and offers grouping and search over the result. The classification and filtering are plain, side-effect-free functions
    /// so they can be tested without opening a window (the window is a thin renderer over this).
    /// </summary>
    public static class EventCatalog
    {

        /// <summary>
        /// Collects and classifies every concrete event type the editor knows about (via <see cref="TypeCache"/>), sorted for display.
        /// </summary>
        /// <returns>One <see cref="EventEntry"/> per catalogable type.</returns>
        public static List<EventEntry> Build()
        {
            return BuildFrom(TypeCache.GetTypesDerivedFrom<IEvent>());
        }

        /// <summary>
        /// Classifies the given types into catalog entries, skipping the ones that can't be cataloged (an interface, an abstract type, an
        /// open generic, or a type that implements no kind marker), then sorts them by kind, namespace and name.
        /// </summary>
        /// <param name="types">The candidate types (typically everything assignable to <see cref="IEvent"/>).</param>
        /// <returns>The entries for the catalogable types among <paramref name="types"/>.</returns>
        public static List<EventEntry> BuildFrom(IEnumerable<Type> types)
        {
            List<EventEntry> entries = new List<EventEntry>();
            if (types == null)
                return entries;

            foreach (Type type in types)
            {
                if (TryClassify(type, out EventEntry entry))
                    entries.Add(entry);
            }

            entries.Sort(CompareForDisplay);
            return entries;
        }

        /// <summary>
        /// Decides whether a type is a catalogable event and, if so, which kind it is and what it produces. A type is catalogable when it
        /// is a concrete, closed class or struct that implements exactly one of the kind markers. The markers are checked from the most
        /// specific to the least, so a valued command (which carries a result) is never mistaken for anything else.
        /// </summary>
        /// <param name="type">The candidate type.</param>
        /// <param name="entry">The classified entry, or <c>null</c> when the type isn't catalogable.</param>
        /// <returns>True when <paramref name="type"/> was classified into <paramref name="entry"/>.</returns>
        public static bool TryClassify(Type type, out EventEntry entry)
        {
            entry = null;
            if (type == null)
                return false;

            // The bus only ever dispatches on a concrete, closed type: an interface, an abstract type or an open generic can never be the
            // exact key of an emitted event, so none of them belongs in the catalog.
            if (type.IsInterface || type.IsAbstract || type.IsGenericTypeDefinition || type.ContainsGenericParameters)
                return false;

            if (!typeof(IEvent).IsAssignableFrom(type))
                return false;

            if (!TryResolveKind(type, out EventKind kind, out Type resultType))
                return false;

            EventAttribute attribute = type.GetCustomAttribute<EventAttribute>(inherit: false);
            entry = new EventEntry(type, kind, resultType, attribute?.Name, attribute?.Description, attribute != null && attribute.OmitSnapshot, attribute != null && attribute.Hidden);
            return true;
        }

        /// <summary>
        /// Resolves which kind a type is from the markers it implements. A request wins over a valued command (a type would only implement
        /// both by mistake), a valued command over a void one, then cue, then signal, so a malformed multi-marker type still classifies
        /// deterministically rather than being dropped.
        /// </summary>
        private static bool TryResolveKind(Type type, out EventKind kind, out Type resultType)
        {
            resultType = null;

            Type requestResult = GetGenericMarkerArgument(type, typeof(IRequest<>));
            if (requestResult != null)
            {
                kind = EventKind.Request;
                resultType = requestResult;
                return true;
            }

            Type commandResult = GetGenericMarkerArgument(type, typeof(ICommand<>));
            if (commandResult != null)
            {
                kind = EventKind.Command;
                resultType = commandResult;
                return true;
            }

            if (typeof(ICommand).IsAssignableFrom(type))
            {
                kind = EventKind.Command;
                return true;
            }

            if (typeof(ICue).IsAssignableFrom(type))
            {
                kind = EventKind.Cue;
                return true;
            }

            if (typeof(ISignal).IsAssignableFrom(type))
            {
                kind = EventKind.Signal;
                return true;
            }

            kind = default;
            return false;
        }

        /// <summary>
        /// Returns the type argument of the closed <paramref name="openMarker"/> (e.g. <see cref="IRequest{TResult}"/>) that
        /// <paramref name="type"/> implements, or <c>null</c> when it implements none. A well-formed event implements a kind marker at most
        /// once; if several are found the first is used.
        /// </summary>
        private static Type GetGenericMarkerArgument(Type type, Type openMarker)
        {
            foreach (Type implemented in type.GetInterfaces())
            {
                if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == openMarker)
                    return implemented.GetGenericArguments()[0];
            }
            return null;
        }

        /// <summary>
        /// Keeps the entries whose name, namespace or description contains <paramref name="search"/> (case-insensitive), optionally dropping
        /// the ones marked hidden. A null or empty search matches every (still-visible) entry.
        /// </summary>
        /// <param name="entries">The entries to filter.</param>
        /// <param name="search">The text to match, or null/empty to match everything.</param>
        /// <param name="includeHidden">By default, entries flagged with <c>[Event(Hidden = true)]</c> are left out regardless of the
        /// search. If enabled, hidden entries are kept like any other.
        /// </param>
        /// <returns>The matching entries, in their original order.</returns>
        public static List<EventEntry> Filter(IEnumerable<EventEntry> entries, string search, bool includeHidden = false)
        {
            List<EventEntry> result = new List<EventEntry>();
            if (entries == null)
                return result;

            bool matchAll = string.IsNullOrWhiteSpace(search);
            foreach (EventEntry entry in entries)
            {
                if (!includeHidden && entry.Hidden)
                    continue;
                if (matchAll || Matches(entry, search))
                    result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Whether an entry matches a non-empty search term across its name, namespace and description.
        /// </summary>
        private static bool Matches(EventEntry entry, string search)
        {
            return Contains(entry.Name, search)
                || Contains(entry.DisplayName, search)
                || Contains(entry.Namespace, search)
                || Contains(entry.Description, search);
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Groups entries by kind, preserving each group's incoming order and yielding the groups in kind order (signal, cue, command,
        /// request). Kinds with no entries are omitted.
        /// </summary>
        /// <param name="entries">The entries to group.</param>
        /// <returns>One list per non-empty kind, in <see cref="EventKind"/> order.</returns>
        public static List<KeyValuePair<EventKind, List<EventEntry>>> GroupByKind(IEnumerable<EventEntry> entries)
        {
            Dictionary<EventKind, List<EventEntry>> buckets = new Dictionary<EventKind, List<EventEntry>>();
            if (entries != null)
            {
                foreach (EventEntry entry in entries)
                {
                    if (!buckets.TryGetValue(entry.Kind, out List<EventEntry> bucket))
                    {
                        bucket = new List<EventEntry>();
                        buckets[entry.Kind] = bucket;
                    }
                    bucket.Add(entry);
                }
            }

            List<KeyValuePair<EventKind, List<EventEntry>>> groups = new List<KeyValuePair<EventKind, List<EventEntry>>>();
            foreach (EventKind kind in new[] { EventKind.Signal, EventKind.Cue, EventKind.Command, EventKind.Request })
            {
                if (buckets.TryGetValue(kind, out List<EventEntry> bucket))
                    groups.Add(new KeyValuePair<EventKind, List<EventEntry>>(kind, bucket));
            }
            return groups;
        }

        /// <summary>
        /// Orders entries for display: by kind, then namespace, then name, so a rebuilt catalog is stable and readable.
        /// </summary>
        private static int CompareForDisplay(EventEntry a, EventEntry b)
        {
            int byKind = a.Kind.CompareTo(b.Kind);
            if (byKind != 0)
                return byKind;

            int byNamespace = string.Compare(a.Namespace, b.Namespace, StringComparison.OrdinalIgnoreCase);
            if (byNamespace != 0)
                return byNamespace;

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

    }

}
