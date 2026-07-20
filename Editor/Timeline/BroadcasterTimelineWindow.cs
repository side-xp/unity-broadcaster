using System.Collections.Generic;
using System.Text;

using UnityEditor;
using UnityEngine;

using SideXP.Core;
using SideXP.Core.EditorOnly;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// A profiler-style view of the dispatches a bus reported: time runs left to right, each dispatch is a bar (a synchronous one collapses
    /// to a point, a cue or async order/ask stretches into a block), the callbacks it invoked show as sub-segments, cascades link a parent
    /// dispatch to the ones it triggered, and violations mark where the bus was misused. Selecting a bar reveals its payload, its callbacks
    /// and (when captured) the emitter's call stack, with ping/filter shortcuts to the owners involved.<br/>
    /// It's a thin renderer: retention lives in a <see cref="SpanRecorder"/> and matching in a <see cref="TimelineFilter"/>; this window only
    /// lays them out.
    /// </summary>
    public class BroadcasterTimelineWindow : EditorWindow
    {

        #region Fields

        private const string WindowTitle = "Broadcaster Timeline";
        private const string MenuItem = EditorConstants.EditorWindowMenu + "/Broadcaster/Timeline";

        /// <summary>The violations strip along the top of the canvas.</summary>
        private const float TopStrip = 14f;
        /// <summary>The time-ruler strip above the violations strip, holding the graduation labels.</summary>
        private const float RulerHeight = 16f;
        /// <summary>Total header height (ruler + violations strip) that the dispatch lanes start below.</summary>
        private const float HeaderHeight = RulerHeight + TopStrip;
        private const float LaneHeight = 18f;
        private const float LaneGap = 4f;
        private const float SidePad = 6f;
        /// <summary>A synchronous dispatch has ~zero duration; drawn at least this wide as a point.</summary>
        private const float MinBarWidth = 4f;
        /// <summary>The detail pane's default height; resizable at runtime via the splitter above it.</summary>
        private const float DetailHeight = 190f;
        private const float MinDetailHeight = 60f;
        private const float MinCanvasHeight = 80f;
        private const float SplitterThickness = 4f;

        private static readonly GUIContent s_capLabel = new GUIContent("Cap", "Maximum number of recorded dispatches kept in memory. The oldest are dropped once this many are exceeded.");
        private static readonly GUIContent s_framesLabel = new GUIContent("Frames", "Keep only dispatches from the last N frames (0 = unlimited), so a long session's timeline doesn't compress endlessly.");

        // Persisted so the setup survives domain reloads. The recorder and filter are runtime objects rebuilt on enable from these.
        [SerializeField] private int _capacity = SpanRecorder.DefaultCapacity;
        [SerializeField] private int _maxFrameSpan = 0;
        [SerializeField] private bool _recording = true;
        [SerializeField] private bool _captureStacks = false;
        [SerializeField] private string _search = string.Empty;
        [SerializeField] private bool _showSignals = true;
        [SerializeField] private bool _showCues = true;
        [SerializeField] private bool _showCommands = true;
        [SerializeField] private bool _showRequests = true;
        [SerializeField] private float _detailHeight = DetailHeight;

        private SpanRecorder _recorder;
        private TimelineFilter _filter;

        private Vector2 _scroll;
        private DispatchSpan _selectedSpan;
        private Vector2 _detailScroll;
        private bool _resizingDetail;

        // Rebuilt each repaint from the visible spans, then reused for hit-testing this same frame.
        private readonly List<SpanLayout> _layouts = new List<SpanLayout>();
        private readonly Dictionary<DispatchSpan, Rect> _rectsBySpan = new Dictionary<DispatchSpan, Rect>();

        #endregion


        #region Lifecycle

        [MenuItem(MenuItem)]
        public static BroadcasterTimelineWindow Open()
        {
            BroadcasterTimelineWindow window = GetWindow<BroadcasterTimelineWindow>(false, WindowTitle, true);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            _recorder = new SpanRecorder
            {
                Capacity = Mathf.Max(1, _capacity),
                MaxFrameSpan = Mathf.Max(0, _maxFrameSpan),
                IsRecording = _recording,
                CaptureEmitterStacks = _captureStacks,
            };
            _recorder.Changed += Repaint;
            _recorder.Attach(Broadcaster.Default);

            _filter = new TimelineFilter { TypeQuery = _search };
            _filter.SetKindEnabled(EventKind.Signal, _showSignals);
            _filter.SetKindEnabled(EventKind.Cue, _showCues);
            _filter.SetKindEnabled(EventKind.Command, _showCommands);
            _filter.SetKindEnabled(EventKind.Request, _showRequests);
            _filter.Changed += Repaint;

            EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChange;

            if (_recorder != null)
            {
                _recorder.Changed -= Repaint;
                _recorder.Detach();
                _recorder = null;
            }
        }

        // The default bus is recreated across play-mode transitions, so re-point the recorder at whatever the current default bus is. Retained
        // spans are kept (the recorder only clears on an explicit Clear), so a session's timeline doesn't vanish on stop.
        private void HandlePlayModeStateChange(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                _recorder.Attach(Broadcaster.Default);
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

            bool hasSelection = _selectedSpan != null;
            float splitterHeight = hasSelection ? SplitterThickness : 0f;
            float maxDetail = Mathf.Max(MinDetailHeight, position.height - toolbarHeight - splitterHeight - MinCanvasHeight);
            float detailHeight = hasSelection ? Mathf.Clamp(_detailHeight, MinDetailHeight, maxDetail) : 0f;

            Rect canvasRect = new Rect(0f, toolbarHeight, position.width, position.height - toolbarHeight - splitterHeight - detailHeight);
            DrawCanvas(canvasRect);

            if (hasSelection)
            {
                Rect splitterRect = new Rect(0f, canvasRect.yMax, position.width, splitterHeight);
                HandleDetailSplitter(splitterRect, maxDetail);
                DrawDetailPane(new Rect(0f, splitterRect.yMax, position.width, detailHeight));
            }
        }

        // The draggable divider above the detail pane; drives _detailHeight (clamped back in OnGUI). Dragging up grows the pane, so a
        // dispatch with many callbacks is readable without endless scrolling.
        private void HandleDetailSplitter(Rect rect, float maxDetail)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.center.y - 0.5f, rect.width, 1f), new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                    _resizingDetail = true;
                    e.Use();
                    break;
                case EventType.MouseDrag when _resizingDetail:
                    _detailHeight = Mathf.Clamp(_detailHeight - e.delta.y, MinDetailHeight, maxDetail);
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when _resizingDetail:
                    _resizingDetail = false;
                    e.Use();
                    break;
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool recording = GUILayout.Toggle(_recording, _recording ? "Recording" : "Paused", EditorStyles.toolbarButton, GUILayout.Width(76));
                if (recording != _recording)
                {
                    _recording = recording;
                    _recorder.IsRecording = recording;
                }

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(46)))
                {
                    _recorder.Clear();
                    _selectedSpan = null;
                }

                GUILayout.Space(8);
                DrawKindToggle(EventKind.Signal, ref _showSignals);
                DrawKindToggle(EventKind.Cue, ref _showCues);
                DrawKindToggle(EventKind.Command, ref _showCommands);
                DrawKindToggle(EventKind.Request, ref _showRequests);

                GUILayout.Space(8);
                string search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(160));
                if (search != _search)
                {
                    _search = search;
                    _filter.TypeQuery = search;
                }

                if (_filter.Owner != null)
                {
                    if (GUILayout.Button($"Owner: {OwnerName(_filter.Owner)}  ✕", EditorStyles.toolbarButton))
                        _filter.Owner = null;
                }

                GUILayout.FlexibleSpace();

                bool captureStacks = GUILayout.Toggle(_captureStacks, new GUIContent("Stacks", "Capture the emitter's call stack for each dispatch."), EditorStyles.toolbarButton, GUILayout.Width(52));
                if (captureStacks != _captureStacks)
                {
                    _captureStacks = captureStacks;
                    _recorder.CaptureEmitterStacks = captureStacks;
                }

                using (new LabelWidthScope(MoreGUI.WidthXS))
                {
                    int capacity = EditorGUILayout.DelayedIntField(s_capLabel, _capacity);
                    if (capacity != _capacity)
                    {
                        _capacity = Mathf.Max(1, capacity);
                        _recorder.Capacity = _capacity;
                    }
                }

                using (new LabelWidthScope(MoreGUI.WidthS))
                {
                    int maxFrameSpan = EditorGUILayout.DelayedIntField(s_framesLabel, _maxFrameSpan);
                    if (maxFrameSpan != _maxFrameSpan)
                    {
                        _maxFrameSpan = Mathf.Max(0, maxFrameSpan);
                        _recorder.MaxFrameSpan = _maxFrameSpan;
                    }
                }
            }
        }

        private void DrawKindToggle(EventKind kind, ref bool enabled)
        {
            Color previous = GUI.color;
            if (enabled)
                GUI.color = EventKindColors.Get(kind);
            bool next = GUILayout.Toggle(enabled, KindName(kind), EditorStyles.toolbarButton, GUILayout.Width(64));
            GUI.color = previous;

            if (next != enabled)
            {
                enabled = next;
                _filter.SetKindEnabled(kind, next);
            }
        }

        #endregion


        #region Canvas

        private void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, CanvasBackground);

            List<RecordedSpan> visibleSpans = CollectVisibleSpans();
            if (visibleSpans.Count == 0)
            {
                DrawCanvasInfo(rect);
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            GetTimeBounds(visibleSpans, now, out double t0, out double t1);

            BuildLayout(visibleSpans, rect.width - 16f, t0, t1, now, out int laneCount);
            float contentHeight = HeaderHeight + laneCount * (LaneHeight + LaneGap) + LaneGap;

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(contentHeight, rect.height));
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);
            {
                // Graduations sit behind everything; drawing them first keeps the gridlines under the bars.
                DrawTimeGraduations(viewRect.width, t0, t1, viewRect.height);

                // Cascade edges use Handles, which only render on the repaint pass; drawing them before the bars keeps them underneath.
                if (Event.current.type == EventType.Repaint)
                    DrawCascadeEdges();

                DrawSpanBars(now);
                DrawViolations(viewRect.width, t0, t1, now);
                HandleCanvasClick(viewRect);
            }
            GUI.EndScrollView();
        }

        private List<RecordedSpan> CollectVisibleSpans()
        {
            List<RecordedSpan> visible = new List<RecordedSpan>();
            bool selectionStillPresent = false;
            foreach (RecordedSpan recorded in _recorder.Spans)
            {
                if (recorded.Span == _selectedSpan)
                    selectionStillPresent = true;
                if (_filter.Matches(recorded.Span))
                    visible.Add(recorded);
            }

            // Drop a selection whose span the recorder has evicted, so the detail pane doesn't linger on a gone dispatch.
            if (_selectedSpan != null && !selectionStillPresent)
                _selectedSpan = null;

            return visible;
        }

        private static void GetTimeBounds(List<RecordedSpan> spans, double now, out double t0, out double t1)
        {
            t0 = double.MaxValue;
            t1 = double.MinValue;
            foreach (RecordedSpan recorded in spans)
            {
                DispatchSpan span = recorded.Span;
                double end = span.IsComplete ? span.EndTime : now;
                if (span.BeginTime < t0) t0 = span.BeginTime;
                if (end > t1) t1 = end;
            }

            // A single instant (all-synchronous) session has no width; give it a nominal one so bars still lay out.
            if (t1 - t0 < 1e-4)
                t1 = t0 + 1e-4;
        }

        // Greedy lane packing: each span goes in the first lane whose last bar ends before it starts, so overlapping (concurrent) dispatches
        // stack onto separate lanes and sequential ones reuse a lane. Fills _layouts and _rectsBySpan for this frame.
        private void BuildLayout(List<RecordedSpan> spans, float width, double t0, double t1, double now, out int laneCount)
        {
            _layouts.Clear();
            _rectsBySpan.Clear();
            List<float> laneEnds = new List<float>();

            foreach (RecordedSpan recorded in spans)
            {
                DispatchSpan span = recorded.Span;
                double end = span.IsComplete ? span.EndTime : now;
                float x0 = MapX(span.BeginTime, t0, t1, width);
                float x1 = MapX(end, t0, t1, width);
                float barWidth = Mathf.Max(MinBarWidth, x1 - x0);

                int lane = 0;
                for (; lane < laneEnds.Count; lane++)
                {
                    if (laneEnds[lane] <= x0 - 2f)
                        break;
                }
                if (lane == laneEnds.Count)
                    laneEnds.Add(0f);
                laneEnds[lane] = x0 + barWidth;

                float y = HeaderHeight + lane * (LaneHeight + LaneGap) + LaneGap;
                Rect barRect = new Rect(x0, y, barWidth, LaneHeight);
                _layouts.Add(new SpanLayout { Recorded = recorded, Rect = barRect });
                _rectsBySpan[span] = barRect;
            }

            laneCount = laneEnds.Count;
        }

        private void DrawSpanBars(double now)
        {
            foreach (SpanLayout layout in _layouts)
            {
                DispatchSpan span = layout.Recorded.Span;
                Rect rect = layout.Rect;

                Color fill = EventKindColors.Get(span.Kind);
                fill.a = span.IsComplete ? 0.55f : 0.35f; // an in-flight block reads fainter than a finished one
                EditorGUI.DrawRect(rect, fill);

                DrawListenerSegments(span, rect, now);

                if (!span.IsComplete)
                    DrawOutline(rect, InFlightOutline, 1f);
                if (span == _selectedSpan)
                    DrawOutline(rect, SelectionOutline, 2f);
                if (SpanFaulted(span))
                    DrawOutline(rect, ViolationColor, 1f);

                if (rect.width > 42f)
                {
                    Rect labelRect = new Rect(rect.x + 4f, rect.y, rect.width - 6f, rect.height);
                    GUI.Label(labelRect, span.EventType.Name, BarLabelStyle);
                }
            }
        }

        // The callbacks a dispatch invoked, drawn as thin segments along the bottom of its bar, each spanning the time that callback ran (a
        // synchronous callback is a tick; a durative performer stretches). Faulted ones tint red.
        private void DrawListenerSegments(DispatchSpan span, Rect barRect, double now)
        {
            if (span.Listeners.Count == 0 || barRect.width < MinBarWidth * 2f)
                return;

            double spanStart = span.BeginTime;
            double spanEnd = span.IsComplete ? span.EndTime : now;
            double range = spanEnd - spanStart;
            if (range <= 0d)
                return;

            float trackY = barRect.yMax - 4f;
            foreach (ListenerSpan listener in span.Listeners)
            {
                double lStart = listener.BeginTime;
                double lEnd = listener.IsComplete ? listener.EndTime : now;
                float lx0 = barRect.x + (float)((lStart - spanStart) / range) * barRect.width;
                float lx1 = barRect.x + (float)((lEnd - spanStart) / range) * barRect.width;
                Rect seg = new Rect(lx0, trackY, Mathf.Max(2f, lx1 - lx0), 3f);
                EditorGUI.DrawRect(seg, listener.Outcome == DispatchOutcome.Faulted ? ViolationColor : ListenerSegment);
            }
        }

        // A faint connector from a parent dispatch's bar to each visible child it triggered — the causal cascade (a guaranteed link for
        // synchronous cascades, temporal containment otherwise).
        private void DrawCascadeEdges()
        {
            Handles.color = CascadeEdge;
            foreach (SpanLayout layout in _layouts)
            {
                DispatchSpan span = layout.Recorded.Span;
                if (span.Parent == null || !_rectsBySpan.TryGetValue(span.Parent, out Rect parentRect))
                    continue;

                Rect childRect = layout.Rect;
                Vector3 from = new Vector3(parentRect.x, parentRect.yMax);
                Vector3 to = new Vector3(childRect.x, childRect.y);
                Handles.DrawLine(from, to);
            }
        }

        // Violations sit in the top strip at the time they happened (dispatch-time ones under their dispatch; registration-time ones, which
        // carry no dispatch, at the current edge). Red markers; hovering one shows its message as a tooltip.
        private void DrawViolations(float width, double t0, double t1, double now)
        {
            foreach (Violation violation in _recorder.Violations)
            {
                if (!_filter.Matches(violation))
                    continue;

                double when = violation.Span != null ? violation.Span.BeginTime : now;
                float x = MapX(when, t0, t1, width);
                Rect marker = new Rect(x - 4f, RulerHeight + 2f, 8f, 8f);
                EditorGUI.DrawRect(marker, ViolationColor);
                // An (invisible) label over the marker carries the message as a hover tooltip, so a violation is readable in place.
                GUI.Label(new Rect(x - 6f, RulerHeight, 12f, TopStrip), new GUIContent(string.Empty, violation.Message));
            }
        }

        // Vertical time graduations behind the bars: faint gridlines at "nice" intervals, each labelled in the top ruler with the elapsed
        // time from the left edge (the earliest visible dispatch). The X axis is time-based, so these mark time; frame graduations can ride
        // an optional toggle later.
        private void DrawTimeGraduations(float width, double t0, double t1, float height)
        {
            double range = t1 - t0;
            double step = NiceTimeStep(range, width, 90f);
            if (step <= 0.0)
                return;

            bool milliseconds = range < 1.0;
            double scale = milliseconds ? 1000.0 : 1.0;
            string unit = milliseconds ? "ms" : "s";

            for (int k = 0; ; k++)
            {
                double elapsed = k * step;
                double t = t0 + elapsed;
                if (t > t1 + step * 0.5)
                    break;

                float x = MapX(t, t0, t1, width);
                EditorGUI.DrawRect(new Rect(x, RulerHeight, 1f, height - RulerHeight), GraduationLine);
                GUI.Label(new Rect(x + 2f, 0f, 60f, RulerHeight), $"{elapsed * scale:0.##} {unit}", EditorStyles.miniLabel);
            }
        }

        // A "nice" graduation step (1, 2 or 5 times a power of ten) for the given time range and pixel width, aiming for roughly one line
        // every targetPixels. Zero when there's nothing to space out.
        private static double NiceTimeStep(double range, float width, float targetPixels)
        {
            if (range <= 0.0 || width <= 0f)
                return 0.0;

            double approxSteps = System.Math.Max(1.0, width / targetPixels);
            double rawStep = range / approxSteps;
            double magnitude = System.Math.Pow(10.0, System.Math.Floor(System.Math.Log10(rawStep)));
            double normalized = rawStep / magnitude;
            double nice = normalized <= 1.0 ? 1.0 : normalized <= 2.0 ? 2.0 : normalized <= 5.0 ? 5.0 : 10.0;
            return nice * magnitude;
        }

        private void HandleCanvasClick(Rect viewRect)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !viewRect.Contains(e.mousePosition))
                return;

            // Top-most lane wins when bars overlap in screen space: iterate back-to-front.
            for (int i = _layouts.Count - 1; i >= 0; i--)
            {
                if (_layouts[i].Rect.Contains(e.mousePosition))
                {
                    _selectedSpan = _layouts[i].Recorded.Span;
                    e.Use();
                    Repaint();
                    return;
                }
            }

            _selectedSpan = null;
            e.Use();
            Repaint();
        }

        private void DrawCanvasInfo(Rect rect)
        {
            string message = _recorder.Spans.Count == 0
                ? (Application.isPlaying ? "Recording. Emit, cue, order or ask on the default bus to see dispatches here." : "No dispatches recorded yet. Fire events from the Events window or enter play mode.")
                : "No recorded dispatch matches the current filters.";
            Rect box = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 40f);
            EditorGUI.HelpBox(box, message, MessageType.Info);
        }

        private static float MapX(double t, double t0, double t1, float width)
        {
            double n = (t - t0) / (t1 - t0);
            return SidePad + (float)n * (width - 2f * SidePad);
        }

        #endregion


        #region Detail pane

        private void DrawDetailPane(Rect rect)
        {
            EditorGUI.DrawRect(rect, DetailBackground);
            DispatchSpan span = _selectedSpan;
            if (span == null)
                return;

            using (new GUILayout.AreaScope(rect))
            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_detailScroll))
            {
                _detailScroll = scroll.scrollPosition;
                EditorGUILayout.Space(6);

                // Title row: the event name large on the left, the colorized kind tag on the right (mirrors the Events window header).
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(span.EventType.Name, EditorStyles.largeLabel);
                    GUILayout.FlexibleSpace();
                    DrawKindTag(span.Kind);
                }

                // The real (fully-qualified) type under the title, in italic.
                EditorGUILayout.LabelField(span.EventType.FullName, ItalicTypeStyle);

                // How the dispatch resolved, under the type.
                string timing = span.IsComplete
                    ? $"{OutcomeName(span.Outcome)} · frames {span.BeginFrame}–{span.EndFrame} · {DurationMs(span.BeginTime, span.EndTime):0.###} ms"
                    : $"running · began frame {span.BeginFrame}";
                EditorGUILayout.LabelField(timing, EditorStyles.miniLabel);

                // The payload snapshot in a bordered block, monospaced, with JSON values pretty-printed when they parse.
                EditorGUILayout.Space(4);
                DrawPayload(span.Payload);

                DrawListenerRows(span);
                DrawEmitterStack(span);
            }
        }

        // Renders the captured payload: its custom ToString summary if it has one, otherwise its captured fields one per line. Each value
        // is pretty-printed when it's well-formed JSON, and shown as-is otherwise.
        private static void DrawPayload(PayloadSnapshot payload)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (payload == null)
                {
                    GUILayout.Label("No payload.", EditorStyles.miniLabel);
                    return;
                }

                if (!string.IsNullOrEmpty(payload.Summary))
                {
                    GUILayout.Label(PrettyJsonOrRaw(payload.Summary), PayloadStyle);
                    return;
                }

                if (payload.Fields.Count == 0)
                {
                    GUILayout.Label(payload.TypeName, PayloadStyle);
                    return;
                }

                foreach (PayloadField field in payload.Fields)
                    GUILayout.Label($"{field.Name} = {PrettyJsonOrRaw(field.Value)}", PayloadStyle);
            }
        }

        private static string PrettyJsonOrRaw(string text) => TryPrettyJson(text) ?? text;

        // A best-effort JSON pretty-printer. It re-indents a value that reads as a JSON object or array (balanced braces/brackets, closed
        // strings) and returns null for anything else, so a non-JSON value silently falls back to its raw text. It re-formats rather than
        // fully validates: it won't reject every malformed document, but it never throws and never garbles a value it can't format.
        private static string TryPrettyJson(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            string trimmed = text.Trim();
            if (trimmed.Length < 2 || (trimmed[0] != '{' && trimmed[0] != '['))
                return null;

            StringBuilder sb = new StringBuilder(trimmed.Length + 32);
            int indent = 0;
            bool inString = false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];

                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < trimmed.Length)
                        sb.Append(trimmed[++i]); // keep an escaped character (incl. an escaped quote) verbatim
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        int peek = SkipWhitespace(trimmed, i + 1);
                        if (peek < trimmed.Length && (trimmed[peek] == '}' || trimmed[peek] == ']'))
                        {
                            sb.Append(trimmed[peek]); // keep an empty container ({} or []) on one line
                            i = peek;
                        }
                        else
                        {
                            indent++;
                            AppendIndent(sb, indent);
                        }
                        break;
                    case '}':
                    case ']':
                        indent--;
                        if (indent < 0)
                            return null; // more closers than openers — not valid JSON
                        AppendIndent(sb, indent);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        AppendIndent(sb, indent);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    case ' ':
                    case '\t':
                    case '\n':
                    case '\r':
                        break; // collapse existing whitespace outside strings; the indentation is rebuilt
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return inString || indent != 0 ? null : sb.ToString();
        }

        private static int SkipWhitespace(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
            return i;
        }

        private static void AppendIndent(StringBuilder sb, int indent)
        {
            sb.Append('\n');
            for (int i = 0; i < indent; i++)
                sb.Append("  ");
        }

        private void DrawListenerRows(DispatchSpan span)
        {
            EditorGUILayout.Space(8);
            MoreEditorGUI.HorizontalSeparator();
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(span.Listeners.Count == 1 ? "1 callback" : $"{span.Listeners.Count} callbacks", EditorStyles.boldLabel);
            if (span.Listeners.Count == 0)
            {
                EditorGUILayout.LabelField("No callback was invoked.", EditorStyles.miniLabel);
                return;
            }

            foreach (ListenerSpan listener in span.Listeners)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string status = listener.IsComplete ? OutcomeName(listener.Outcome) : "running";
                    EditorGUILayout.LabelField($"{RoleName(listener.Role)} · {OwnerName(listener.Owner)} · {status}", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();

                    UnityEngine.Object unityOwner = listener.Owner as UnityEngine.Object;
                    using (new EditorGUI.DisabledScope(unityOwner == null))
                    {
                        if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40)))
                            EditorGUIUtility.PingObject(unityOwner);
                    }

                    if (GUILayout.Button("Filter", EditorStyles.miniButton, GUILayout.Width(46)))
                        _filter.Owner = listener.Owner;
                }
            }
        }

        private void DrawEmitterStack(DispatchSpan span)
        {
            string stack = FindEmitterStack(span);
            if (string.IsNullOrEmpty(stack))
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Emitter", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(stack, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight * 3f));
        }

        private string FindEmitterStack(DispatchSpan span)
        {
            foreach (RecordedSpan recorded in _recorder.Spans)
            {
                if (recorded.Span == span)
                    return recorded.EmitterStack;
            }
            return null;
        }

        // A right-aligned kind label tinted by the shared kind color, matching the Events window header.
        private static void DrawKindTag(EventKind kind)
        {
            GUIStyle style = KindTagStyle;
            Color previous = style.normal.textColor;
            style.normal.textColor = EventKindColors.Get(kind);
            GUILayout.Label(KindName(kind), style);
            style.normal.textColor = previous;
        }

        #endregion


        #region Helpers & styles

        private static bool SpanFaulted(DispatchSpan span)
        {
            if (span.Outcome == DispatchOutcome.Faulted)
                return true;
            foreach (ListenerSpan listener in span.Listeners)
            {
                if (listener.Outcome == DispatchOutcome.Faulted)
                    return true;
            }
            return false;
        }

        private static double DurationMs(double begin, double end) => (end - begin) * 1000d;

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
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

        private static string RoleName(RegistrationRole role)
        {
            switch (role)
            {
                case RegistrationRole.SignalListener: return "listener";
                case RegistrationRole.CuePerformer: return "performer";
                case RegistrationRole.CommandHandler: return "handler";
                case RegistrationRole.RequestHandler: return "handler";
                case RegistrationRole.Provider: return "provider";
                default: return role.ToString();
            }
        }

        private static string OutcomeName(DispatchOutcome outcome)
        {
            switch (outcome)
            {
                case DispatchOutcome.Completed: return "completed";
                case DispatchOutcome.Faulted: return "faulted";
                case DispatchOutcome.Cancelled: return "cancelled";
                default: return outcome.ToString();
            }
        }

        private static string OwnerName(object owner)
        {
            if (owner == null)
                return "none";
            if (owner is UnityEngine.Object unityObject)
                return unityObject == null ? "(destroyed)" : unityObject.name;
            return owner.GetType().Name;
        }

        private static Color CanvasBackground => EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.76f, 0.76f, 0.76f);
        private static Color DetailBackground => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.82f, 0.82f, 0.82f);
        private static Color ListenerSegment => EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f, 0.8f) : new Color(0.15f, 0.15f, 0.15f, 0.8f);
        private static Color CascadeEdge => new Color(1f, 1f, 1f, 0.22f);
        private static Color GraduationLine => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.08f);
        private static Color SelectionOutline => new Color(1f, 1f, 1f, 0.9f);
        private static Color InFlightOutline => new Color(1f, 1f, 1f, 0.35f);
        private static Color ViolationColor => new Color(0.95f, 0.35f, 0.3f);
        private static Color SeparatorColor => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.18f);

        private static GUIStyle s_kindTagStyle;
        private static GUIStyle KindTagStyle
        {
            get
            {
                if (s_kindTagStyle == null)
                    s_kindTagStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                return s_kindTagStyle;
            }
        }

        private static GUIStyle s_italicTypeStyle;
        private static GUIStyle ItalicTypeStyle
        {
            get
            {
                if (s_italicTypeStyle == null)
                    s_italicTypeStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
                return s_italicTypeStyle;
            }
        }

        private static GUIStyle s_barLabelStyle;
        private static GUIStyle BarLabelStyle
        {
            get
            {
                if (s_barLabelStyle == null)
                    s_barLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                return s_barLabelStyle;
            }
        }

        // The payload's monospaced style: a wrapping mini-label whose font is a monospace one built from whatever the OS provides, so
        // values line up column-wise. Resolved once; if no monospace font is installed the font stays default (a silent fallback).
        private static GUIStyle s_payloadStyle;
        private static GUIStyle PayloadStyle
        {
            get
            {
                if (s_payloadStyle == null)
                {
                    s_payloadStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
                    if (MonospaceFont != null)
                        s_payloadStyle.font = MonospaceFont;
                }
                return s_payloadStyle;
            }
        }

        // A monospace font sourced from the editor's OS, trying a few common families before a generic fallback. Built lazily and cached
        // (creating a dynamic font isn't free); null only if the OS exposes none of them, in which case the payload keeps the default font.
        private static bool s_monoFontResolved;
        private static Font s_monoFont;
        private static Font MonospaceFont
        {
            get
            {
                if (!s_monoFontResolved)
                {
                    s_monoFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Consolas", "Menlo", "Monaco", "DejaVu Sans Mono", "Courier New", "monospace" }, 12);
                    s_monoFontResolved = true;
                }
                return s_monoFont;
            }
        }

        #endregion


        #region Types

        private struct SpanLayout
        {
            public RecordedSpan Recorded;
            public Rect Rect;
        }

        #endregion

    }

}
