using System.Collections.Generic;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// One node in the Events window's tree: either a folder that groups other nodes (a segment of some event's display-name path), a leaf
    /// that stands for a single event, or both at once (when an event's full path is also a folder for others). Built by
    /// <see cref="EventTree"/> from the catalog entries; the window's <see cref="EventTreeView"/> renders it.
    /// </summary>
    public sealed class EventTreeNode
    {

        /// <summary>The label shown for this node: a single path segment (the leaf name for an event).</summary>
        public string Name { get; }

        /// <summary>The full <c>/</c>-joined path from the root to this node.</summary>
        public string Path { get; }

        /// <summary>The event this node stands for, or <c>null</c> when the node is a pure folder.</summary>
        public EventEntry Entry { get; set; }

        /// <summary>The child nodes nested under this one, in display order.</summary>
        public List<EventTreeNode> Children { get; } = new List<EventTreeNode>();

        /// <summary>Whether this node groups other nodes (and so renders as an expandable folder).</summary>
        public bool HasChildren => Children.Count > 0;

        /// <inheritdoc cref="EventTreeNode"/>
        public EventTreeNode(string name, string path)
        {
            Name = name;
            Path = path;
        }

    }

}
