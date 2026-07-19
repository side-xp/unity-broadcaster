using System;
using System.Collections.Generic;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// Builds the Events window's tree from catalog entries: each entry's <see cref="EventEntry.DisplayName"/> is read as a <c>/</c>-separated
    /// path (like <see cref="UnityEngine.CreateAssetMenuAttribute"/>), and the entries are nested under folder nodes accordingly. A plain,
    /// side-effect-free function so the nesting can be tested without a window (the <see cref="EventTreeView"/> is a thin renderer over it).
    /// </summary>
    public static class EventTree
    {

        /// <summary>
        /// Nests <paramref name="entries"/> into a tree by their display-name paths.
        /// </summary>
        /// <param name="entries">The catalog entries to place (already filtered/sorted as the window wants them).</param>
        /// <returns>
        /// The (nameless) root node; its <see cref="EventTreeNode.Children"/> are the top-level folders and events, folders before leaves and
        /// each alphabetical. When two entries resolve to the same path the last one wins that leaf.
        /// </returns>
        public static EventTreeNode Build(IEnumerable<EventEntry> entries)
        {
            EventTreeNode root = new EventTreeNode(string.Empty, string.Empty);
            if (entries == null)
                return root;

            foreach (EventEntry entry in entries)
            {
                if (entry == null)
                    continue;

                string[] segments = SplitPath(entry);
                EventTreeNode node = root;
                string path = string.Empty;
                for (int i = 0; i < segments.Length; i++)
                {
                    path = path.Length == 0 ? segments[i] : path + "/" + segments[i];
                    node = GetOrAddChild(node, segments[i], path);
                }
                // The final segment is the event itself; a folder of the same path (a collision) still carries it.
                node.Entry = entry;
            }

            Sort(root);
            return root;
        }

        /// <summary>
        /// Splits an entry's display name into non-empty, trimmed path segments. Falls back to the type name when the display name is all
        /// separators/whitespace, so every entry always lands somewhere.
        /// </summary>
        private static string[] SplitPath(EventEntry entry)
        {
            string[] raw = entry.DisplayName.Split('/');
            List<string> segments = new List<string>(raw.Length);
            foreach (string part in raw)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    segments.Add(trimmed);
            }

            if (segments.Count == 0)
                segments.Add(entry.Name);
            return segments.ToArray();
        }

        /// <summary>
        /// Returns the child of <paramref name="parent"/> with the given segment name, creating it when it doesn't exist yet.
        /// </summary>
        private static EventTreeNode GetOrAddChild(EventTreeNode parent, string name, string path)
        {
            foreach (EventTreeNode child in parent.Children)
            {
                if (child.Name == name)
                    return child;
            }

            EventTreeNode created = new EventTreeNode(name, path);
            parent.Children.Add(created);
            return created;
        }

        /// <summary>
        /// Sorts a node's children (folders first, then leaves, each alphabetical) and recurses.
        /// </summary>
        private static void Sort(EventTreeNode node)
        {
            node.Children.Sort(Compare);
            foreach (EventTreeNode child in node.Children)
                Sort(child);
        }

        private static int Compare(EventTreeNode a, EventTreeNode b)
        {
            if (a.HasChildren != b.HasChildren)
                return a.HasChildren ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

    }

}
