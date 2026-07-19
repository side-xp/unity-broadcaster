using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

using SideXP.Core.EditorOnly;
using SideXP.Core;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// Catalogs every event type in the project, grouped by kind and searchable, and lets you fire any of them at the default bus from an
    /// expandable emit box.<br/>
    /// In play mode each row also shows what's live for its type (listeners, performers, a provider, a handler's owner). It's a thin
    /// renderer: the cataloging, drafting, firing and live tally all live in plain, tested classes.
    /// </summary>
    public class BroadcasterEventsWindow : EditorWindow
    {

        #region Fields

        private const string WindowTitle = "Broadcaster Events";
        private const string MenuItem = EditorConstants.EditorWindowMenu + "/Broadcaster/Events";

        // Width reserved on the right of each row for its live columns.
        private const float LiveColumnsWidth = 220f;

        [SerializeField]
        private string _search = string.Empty;

        // When off (the default), events flagged with [Broadcast(Hidden = true)] — chiefly package test events — are left out, so the window
        // shows only the events a project actually authors. The toolbar eye reveals them.
        [SerializeField]
        private bool _showHidden = false;

        [SerializeField]
        private Vector2 _scroll = Vector2.zero;

        // Persisted across domain reloads so expanded rows stay open; keyed by the type's assembly-qualified name.
        [SerializeField]
        private List<string> _expanded = new List<string>();

        // Rebuilt on enable (and on demand): the catalog is only stale after a recompile, which reloads the domain and re-enables the window.
        private List<EventEntry> _catalog;

        // Per-type emit-box state (a draft payload and the last fire result), created lazily when a row is first expanded.
        private readonly Dictionary<Type, RowState> _rows = new Dictionary<Type, RowState>();

        private EventBusLiveIndex _live;

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

            _live = new EventBusLiveIndex();
            _live.Changed += Repaint;
            _live.Attach(Broadcaster.Default);

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


        #region UI

        private void OnGUI()
        {
            DrawToolbar();

            List<EventEntry> filtered = EventCatalog.Filter(_catalog, _search, _showHidden);
            List<KeyValuePair<EventKind, List<EventEntry>>> groups = EventCatalog.GroupByKind(filtered);

            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;

                if (_catalog.Count == 0)
                {
                    EditorGUILayout.HelpBox("No event types found. Define a type implementing ISignal, ICue, ICommand, ICommand<T> or IRequest<T>.", MessageType.Info);
                    return;
                }
                if (filtered.Count == 0)
                {
                    // Tell the two "nothing here" cases apart: the eye hiding everything that would otherwise match vs. a genuine no-match.
                    bool onlyHiddenMatch = !_showHidden && EventCatalog.Filter(_catalog, _search, includeHidden: true).Count > 0;
                    EditorGUILayout.HelpBox(
                        onlyHiddenMatch
                            ? "Only hidden events match. Toggle the eye in the toolbar to show them."
                            : "No event type matches the search.",
                        MessageType.Info);
                    return;
                }

                foreach (KeyValuePair<EventKind, List<EventEntry>> group in groups)
                {
                    DrawGroupHeader(group.Key, group.Value.Count);
                    foreach (EventEntry entry in group.Value)
                        DrawRow(entry);
                }
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, MoreGUI.WidthXLOpt);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    _catalog = EventCatalog.Build();

                _showHidden = GUILayout.Toggle(_showHidden, ShowHiddenContent(_showHidden), EditorStyles.toolbarButton, MoreGUI.WidthXSOpt);
            }
        }

        // The eye toggle, mirroring Unity's object-picker "show hidden" affordance: an open eye when hidden events are shown, a closed one
        // when they're filtered out. A fresh GUIContent (not the shared cached icon) so setting the tooltip doesn't leak into other callers.
        private static GUIContent ShowHiddenContent(bool showHidden)
        {
            string icon = showHidden ? "animationvisibilitytoggleon" : "animationvisibilitytoggleoff";
            string tooltip = "Toggle hidden events.";
            return new GUIContent(EditorGUIUtility.IconContent(icon).image, tooltip);
        }

        private static void DrawGroupHeader(EventKind kind, int count)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"{KindLabel(kind)} ({count})", EditorStyles.boldLabel);
        }

        private void DrawRow(EventEntry entry)
        {
            string key = entry.EventType.AssemblyQualifiedName;
            bool expanded = _expanded.Contains(key);

            Rect rowRect = EditorGUILayout.GetControlRect();
            Rect foldoutRect = new Rect(rowRect.x, rowRect.y, rowRect.width - LiveColumnsWidth, rowRect.height);
            Rect liveRect = new Rect(foldoutRect.xMax, rowRect.y, LiveColumnsWidth, rowRect.height);

            GUIContent label = new GUIContent(entry.Name, entry.Description);
            bool now = EditorGUI.Foldout(foldoutRect, expanded, label, true);
            if (now != expanded)
            {
                if (now) _expanded.Add(key);
                else _expanded.Remove(key);
            }

            if (Application.isPlaying)
                DrawLiveColumns(liveRect, entry);

            if (now)
                DrawEmitBox(entry);
        }

        private void DrawLiveColumns(Rect rect, EventEntry entry)
        {
            if (_live == null || !_live.TryGet(entry.EventType, out EventLiveState state))
                return;

            string text;
            switch (entry.Kind)
            {
                case EventKind.Signal:
                    text = state.HasProvider
                        ? $"{state.Listeners} listeners · provider"
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

            GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            GUI.Label(rect, text, style);
        }

        private void DrawEmitBox(EventEntry entry)
        {
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!string.IsNullOrEmpty(entry.Description))
                    EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedMiniLabel);

                RowState row = GetRow(entry);

                if (!row.Draft.CanInstantiate)
                {
                    EditorGUILayout.HelpBox($"'{entry.Name}' has no public parameterless constructor, so it can't be drafted here.", MessageType.Warning);
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

        private RowState GetRow(EventEntry entry)
        {
            if (!_rows.TryGetValue(entry.EventType, out RowState row))
            {
                row = new RowState { Draft = new EventDraft(entry.EventType) };
                _rows[entry.EventType] = row;
            }
            return row;
        }

        private static string KindLabel(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Signal: return "Signals";
                case EventKind.Cue: return "Cues";
                case EventKind.Command: return "Commands";
                case EventKind.Request: return "Requests";
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

        // Non-serialized per-row state: recreated lazily after a domain reload (only the expanded set persists).
        private sealed class RowState
        {
            public EventDraft Draft;
            public FireResult Result;
        }

        #endregion

    }

}
