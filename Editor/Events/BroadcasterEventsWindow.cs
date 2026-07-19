using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

using SideXP.Core;
using SideXP.Core.EditorOnly;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// Catalogs every event type in the project as a searchable tree (nested by each event's display-name path) paired with a details pane
    /// that drafts and fires the selected event and shows what's live for it in play mode.<br/>
    /// It's a thin renderer: the cataloging, tree building, drafting, firing and live tally all live in plain classes; this window only lays
    /// them out and forwards to them.
    /// </summary>
    public class BroadcasterEventsWindow : EditorWindow
    {

        #region Fields

        private const string WindowTitle = "Broadcaster Events";
        private const string MenuItem = EditorConstants.EditorWindowMenu + "/Broadcaster/Events";

        private const float SplitterWidth = 4f;
        private const float MinPaneWidth = 160f;

        [SerializeField]
        private string _search = string.Empty;

        // When off (the default), events flagged with [Event(Hidden = true)] (chiefly package test events) are left out, so the window
        // shows only the events a project actually authors. The toolbar eye reveals them.
        [SerializeField]
        private bool _showHidden = false;

        // Width of the tree pane; the details pane fills the rest. Persisted so the split you set survives reloads.
        [SerializeField]
        private float _treeWidth = 280f;

        // The tree's expansion, selection and scroll, serialized so they're restored across domain reloads and editor restarts.
        [SerializeField]
        private TreeViewState<int> _treeState;

        // Rebuilt on enable (and on demand): the catalog is only stale after a recompile, which reloads the domain and re-enables the window.
        private List<EventEntry> _catalog;

        // Per-type details state (a draft payload and the last fire result), created lazily when a type is first selected.
        private readonly Dictionary<Type, RowState> _rows = new Dictionary<Type, RowState>();

        private EventTreeView _tree;
        private EventBusLiveIndex _live;

        private Vector2 _detailScroll;
        private bool _resizingSplitter;

        // Snapshot of the last filter pass, for the tree pane's empty-state message.
        private int _visibleCount;
        private bool _onlyHiddenMatch;

        #endregion


        #region Lifecycle

        [MenuItem(MenuItem)]
        public static BroadcasterEventsWindow Open()
        {
            BroadcasterEventsWindow window = GetWindow<BroadcasterEventsWindow>(false, WindowTitle, true);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            _catalog = EventCatalog.Build();

            if (_treeState == null)
                _treeState = new TreeViewState<int>();

            _live = new EventBusLiveIndex();
            _live.Changed += Repaint;
            _live.Attach(Broadcaster.Default);

            _tree = new EventTreeView(_treeState) { SelectionChangedCallback = Repaint, Live = _live };
            RebuildTree();

            EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChange;

            if (_live != null)
            {
                _live.Changed -= Repaint;
                _live.Detach();
                _live = null;
            }
        }

        // The default bus is recreated on entering play mode (and drafts fired in one session shouldn't leak state into the next), so
        // re-point the live tally at whatever the current default bus is whenever the mode changes.
        private void HandlePlayModeStateChange(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                _live.Attach(Broadcaster.Default);
                Repaint();
            }
        }

        #endregion


        #region Layout

        private void OnGUI()
        {
            float toolbarHeight = Mathf.Max(EditorStyles.toolbar.fixedHeight, 18f);
            using (new GUILayout.AreaScope(new Rect(0f, 0f, position.width, toolbarHeight)))
                DrawToolbar();

            Rect body = new Rect(0f, toolbarHeight, position.width, position.height - toolbarHeight);
            _treeWidth = Mathf.Clamp(_treeWidth, MinPaneWidth, Mathf.Max(MinPaneWidth, body.width - MinPaneWidth));

            Rect treeRect = new Rect(body.x, body.y, _treeWidth, body.height);
            Rect splitterRect = new Rect(treeRect.xMax, body.y, SplitterWidth, body.height);
            Rect detailRect = new Rect(splitterRect.xMax, body.y, body.width - splitterRect.xMax, body.height);

            DrawTreePane(treeRect);
            HandleSplitter(splitterRect);
            DrawDetailPane(detailRect);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, MoreGUI.WidthXLOpt);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    _catalog = EventCatalog.Build();
                    RebuildTree();
                }

                bool showHidden = GUILayout.Toggle(_showHidden, ShowHiddenContent(_showHidden), EditorStyles.toolbarButton, MoreGUI.WidthXSOpt);

                if (search != _search || showHidden != _showHidden)
                {
                    _search = search;
                    _showHidden = showHidden;
                    RebuildTree();
                }
            }
        }

        private void DrawTreePane(Rect rect)
        {
            if (_catalog.Count == 0)
            {
                DrawPaneInfo(rect, "No event types found. Define a type implementing ISignal, ICue, ICommand, ICommand<T> or IRequest<T>.");
                return;
            }
            if (_visibleCount == 0)
            {
                DrawPaneInfo(rect, _onlyHiddenMatch
                    ? "Only hidden events match. Toggle the eye in the toolbar to show them."
                    : "No event type matches the search.");
                return;
            }

            _tree.OnGUI(rect);
        }

        // The draggable divider between the two panes; drives _treeWidth (clamped back in OnGUI).
        private void HandleSplitter(Rect rect)
        {
            EditorGUI.DrawRect(new Rect(rect.x + rect.width * 0.5f - 0.5f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                    _resizingSplitter = true;
                    e.Use();
                    break;
                case EventType.MouseDrag when _resizingSplitter:
                    _treeWidth += e.delta.x;
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when _resizingSplitter:
                    _resizingSplitter = false;
                    e.Use();
                    break;
            }
        }

        private void DrawDetailPane(Rect rect)
        {
            using (new GUILayout.AreaScope(rect))
            {
                EventEntry entry = _tree.SelectedEntry;
                if (entry == null)
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Select an event to draft and fire it.", EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_detailScroll))
                {
                    _detailScroll = scroll.scrollPosition;
                    DrawDetailHeader(entry);
                    DrawEmitBox(entry);
                    DrawLiveStatus(entry);
                }
            }
        }

        private static void DrawPaneInfo(Rect rect, string message)
        {
            using (new GUILayout.AreaScope(rect))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }

        // The eye toggle, mirroring Unity's object-picker "show hidden" affordance: an open eye when hidden events are shown, a closed one
        // when they're filtered out. A fresh GUIContent (not the shared cached icon) so setting the tooltip doesn't leak into other callers.
        private static GUIContent ShowHiddenContent(bool showHidden)
        {
            string icon = showHidden ? "animationvisibilitytoggleon" : "animationvisibilitytoggleoff";
            return new GUIContent(EditorGUIUtility.IconContent(icon).image, "Toggle hidden events.");
        }

        #endregion


        #region Details pane

        private void DrawDetailHeader(EventEntry entry)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(entry.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(KindName(entry.Kind) + ResultTypeSuffix(entry), EditorStyles.miniLabel);

            string fullName = string.IsNullOrEmpty(entry.Namespace) ? entry.Name : entry.Namespace + "." + entry.Name;
            EditorGUILayout.LabelField(fullName, EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(entry.Description))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space(4);
        }

        private void DrawEmitBox(EventEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                RowState row = GetRow(entry);

                if (!row.Draft.CanInstantiate)
                {
                    EditorGUILayout.HelpBox($"'{entry.DisplayName}' has no public parameterless constructor, so it can't be drafted here.", MessageType.Warning);
                    return;
                }

                if (row.Draft.Fields.Count == 0)
                {
                    EditorGUILayout.LabelField("No editable fields.", EditorStyles.miniLabel);
                }
                else
                {
                    foreach (System.Reflection.FieldInfo field in row.Draft.Fields)
                        MoreEditorGUI.PropertyField(row.Draft.Instance, field, autoLabel: true);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"{FireVerb(entry)}{ResultTypeSuffix(entry)}", GUILayout.Width(160)))
                        Fire(entry, row);

                    if (GUILayout.Button("Reset", GUILayout.Width(60)))
                    {
                        row.Draft.Reset();
                        row.Result = null;
                    }

                    GUILayout.Space(6);
                    DrawResult(row.Result);
                }
            }
        }

        private static void DrawResult(FireResult result)
        {
            if (result == null)
                return;

            Color previous = GUI.color;
            if (result.Faulted)
                GUI.color = new Color(1f, 0.6f, 0.6f);
            GUILayout.Label(result.Summary, EditorStyles.miniLabel);
            GUI.color = previous;
        }

        private void DrawLiveStatus(EventEntry entry)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.LabelField("Registrations show in play mode.", EditorStyles.miniLabel);
                return;
            }

            if (_live == null || !_live.TryGet(entry.EventType, out EventLiveState state))
            {
                EditorGUILayout.LabelField("Nothing registered for this event yet.", EditorStyles.miniLabel);
                return;
            }

            string text;
            switch (entry.Kind)
            {
                case EventKind.Signal:
                    text = state.HasProvider
                        ? $"{state.Listeners} listeners · provider alive"
                        : $"{state.Listeners} listeners";
                    break;
                case EventKind.Cue:
                    text = $"{state.Performers} performers";
                    break;
                default:
                    text = state.HasHandler
                        ? $"handler: {OwnerName(state.HandlerOwner)}"
                        : "no handler";
                    break;
            }
            EditorGUILayout.LabelField(text, EditorStyles.miniLabel);
        }

        #endregion


        #region Firing

        // Fires the drafted event; for a cue, keeps the row's result in sync with the cue's completion as its performers finish.
        private async void Fire(EventEntry entry, RowState row)
        {
            row.Result = EventFireDispatcher.Fire(Broadcaster.Default, entry, row.Draft.Instance);
            Repaint();

            if (row.Result.Completion == null)
                return;

            FireResult pending = row.Result;
            try
            {
                await pending.Completion;
                pending.Summary = "Performed.";
            }
            catch (OperationCanceledException)
            {
                pending.Summary = "Cancelled.";
            }
            catch (Exception exception)
            {
                pending.Summary = "Faulted: " + exception.Message;
            }

            // The window may have been closed while awaiting (its native object is then destroyed); don't touch it.
            if (this == null)
                return;

            // The row may have been reset or re-fired while awaiting; only repaint, the stored result already updated in place.
            Repaint();
        }

        #endregion


        #region Helpers

        private void RebuildTree()
        {
            List<EventEntry> filtered = EventCatalog.Filter(_catalog, _search, _showHidden);
            _visibleCount = filtered.Count;
            _onlyHiddenMatch = !_showHidden && _visibleCount == 0 && EventCatalog.Filter(_catalog, _search, includeHidden: true).Count > 0;

            _tree.SetEntries(filtered);
            _tree.Reload();
        }

        private RowState GetRow(EventEntry entry)
        {
            if (!_rows.TryGetValue(entry.EventType, out RowState row))
            {
                row = new RowState { Draft = new EventDraft(entry.EventType) };
                _rows[entry.EventType] = row;
            }
            return row;
        }

        private static string KindName(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Signal: return "Signal";
                case EventKind.Cue: return "Cue";
                case EventKind.Command: return "Command";
                case EventKind.Request: return "Request";
                default: return kind.ToString();
            }
        }

        private static string FireVerb(EventEntry entry)
        {
            switch (entry.Kind)
            {
                case EventKind.Signal: return "Emit";
                case EventKind.Cue: return "Cue";
                case EventKind.Command: return "Order";
                case EventKind.Request: return "Ask";
                default: return "Fire";
            }
        }

        private static string ResultTypeSuffix(EventEntry entry)
        {
            return entry.HasResult ? $" → {entry.ResultType.Name}" : string.Empty;
        }

        private static string OwnerName(object owner)
        {
            if (owner == null)
                return "none";
            if (owner is UnityEngine.Object unityObject)
                return unityObject == null ? "(destroyed)" : unityObject.name;
            return owner.GetType().Name;
        }

        #endregion


        #region Types

        // Non-serialized per-type details state: recreated lazily after a domain reload (the tree's own state persists separately).
        private sealed class RowState
        {
            public EventDraft Draft;
            public FireResult Result;
        }

        #endregion

    }

}
