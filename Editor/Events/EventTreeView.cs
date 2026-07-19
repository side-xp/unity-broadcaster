using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// The Events window's left pane: a <see cref="TreeView{T}"/> that renders the <see cref="EventTree"/> nesting, gives keyboard navigation
    /// and expansion/selection persistence for free (via its <see cref="TreeViewState{T}"/>), and reports the selected event to the window.
    /// It's a thin adapter: all the nesting logic lives in the tested <see cref="EventTree"/>.
    /// </summary>
    internal sealed class EventTreeView : TreeView<int>
    {

        // The entries to nest, and the id-to-node map rebuilt on every reload so selection and per-row drawing can resolve back to a node.
        private List<EventEntry> _entries = new List<EventEntry>();
        private readonly Dictionary<int, EventTreeNode> _nodesById = new Dictionary<int, EventTreeNode>();

        private static Texture2D s_folderIcon;
        private static GUIStyle s_rightTag;

        /// <summary>Raised when the selection changes, so the window can repaint its details pane.</summary>
        public Action SelectionChangedCallback;

        /// <summary>The live registration tally, read to draw the per-row badge in play mode. Left null to omit it.</summary>
        public EventBusLiveIndex Live { get; set; }

        /// <inheritdoc cref="EventTreeView"/>
        public EventTreeView(TreeViewState<int> state) : base(state)
        {
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            rowHeight = EditorGUIUtility.singleLineHeight + 2f;
        }

        /// <summary>
        /// Sets the entries the next <see cref="TreeView{T}.Reload"/> will nest.
        /// </summary>
        public void SetEntries(List<EventEntry> entries)
        {
            _entries = entries ?? new List<EventEntry>();
        }

        /// <summary>The event of the selected row, or <c>null</c> when nothing (or a pure folder) is selected.</summary>
        public EventEntry SelectedEntry
        {
            get
            {
                foreach (int id in GetSelection())
                {
                    if (_nodesById.TryGetValue(id, out EventTreeNode node) && node.Entry != null)
                        return node.Entry;
                }
                return null;
            }
        }

        protected override TreeViewItem<int> BuildRoot()
        {
            _nodesById.Clear();

            EventTreeNode model = EventTree.Build(_entries);
            TreeViewItem<int> root = new TreeViewItem<int> { id = 0, depth = -1, displayName = "Root" };

            int nextId = 1;
            AddChildren(model, root, ref nextId);

            // TreeView needs a non-null children list even when the (filtered) catalog is empty; the window draws its own empty state.
            if (!root.hasChildren)
                root.children = new List<TreeViewItem<int>>();

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        // Depth-first over the already-sorted model, so ids are deterministic across reloads and the persisted expansion/selection lines up.
        private void AddChildren(EventTreeNode node, TreeViewItem<int> parent, ref int nextId)
        {
            foreach (EventTreeNode child in node.Children)
            {
                int id = nextId++;
                TreeViewItem<int> item = new TreeViewItem<int> { id = id, displayName = child.Name };
                _nodesById[id] = child;
                parent.AddChild(item);

                if (child.HasChildren)
                    AddChildren(child, item, ref nextId);
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (!_nodesById.TryGetValue(args.item.id, out EventTreeNode node))
            {
                base.RowGUI(args);
                return;
            }

            Rect rect = args.rowRect;
            Rect content = new Rect(rect.x + GetContentIndent(args.item), rect.y, 0f, rect.height);
            content.xMax = rect.xMax - 4f;

            if (node.HasChildren && FolderIcon != null)
            {
                GUI.DrawTexture(new Rect(content.x, content.y, 16f, content.height), FolderIcon, ScaleMode.ScaleToFit);
                content.xMin += 18f;
            }

            // The right cluster: a live badge (play mode) then the kind tag, drawn right-to-left so the label gets whatever room is left.
            if (node.Entry != null)
            {
                content.xMax = DrawRightTag(content, node.Entry.Kind.ToString());

                string live = LiveBadge(node.Entry);
                if (!string.IsNullOrEmpty(live))
                    content.xMax = DrawRightTag(content, live) - 4f;
            }

            GUI.Label(content, node.Name);
        }

        // Draws text right-aligned within rect and returns the x it started at, so the next thing can be placed to its left.
        private static float DrawRightTag(Rect rect, string text)
        {
            GUIContent content = new GUIContent(text);
            float width = RightTagStyle.CalcSize(content).x;
            Rect tagRect = new Rect(rect.xMax - width, rect.y, width, rect.height);
            GUI.Label(tagRect, content, RightTagStyle);
            return tagRect.x;
        }

        // A compact per-kind tally, or empty when not playing / nothing registered for this event yet.
        private string LiveBadge(EventEntry entry)
        {
            if (Live == null || !Application.isPlaying || !Live.TryGet(entry.EventType, out EventLiveState state))
                return string.Empty;

            switch (entry.Kind)
            {
                case EventKind.Signal: return state.HasProvider ? $"{state.Listeners} •" : state.Listeners.ToString();
                case EventKind.Cue: return state.Performers.ToString();
                default: return state.HasHandler ? "1" : "0";
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            SelectionChangedCallback?.Invoke();
        }

        protected override bool CanMultiSelect(TreeViewItem<int> item) => false;

        private static Texture2D FolderIcon
        {
            get
            {
                if (s_folderIcon == null)
                    s_folderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
                return s_folderIcon;
            }
        }

        private static GUIStyle RightTagStyle
        {
            get
            {
                if (s_rightTag == null)
                    s_rightTag = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                return s_rightTag;
            }
        }

    }

}
