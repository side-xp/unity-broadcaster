using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// A bounded, swappable record of the dispatches a bus reported through its monitor hooks, kept for the Timeline window to render.
    /// Retention lives here (a ring buffer of the most recent spans and violations) so the window stays a thin renderer and any other
    /// consumer can hold its own recorder. Bus-agnostic: it records whichever <see cref="EventBus"/> it's pointed at and never assumes there
    /// is only one, so pointing it at a named bus later is just a different <see cref="Attach"/> target.
    /// </summary>
    /// <remarks>
    /// A span is captured when it begins, so an in-flight cue or async order shows as a live block; the bus mutates that same instance as it
    /// finishes, so the retained entry reflects completion without being re-added. Like the hooks it listens to, it reflects only what
    /// happened since it attached (dispatches already in flight when it attaches aren't seen), and it exists in the editor and development
    /// builds only — when the monitor is compiled out the hooks don't exist and nothing is recorded.
    /// </remarks>
    public sealed class SpanRecorder
    {

        /// <summary>The number of spans (and, separately, violations) retained before the oldest are evicted, unless changed.</summary>
        public const int DefaultCapacity = 500;

        // The assemblies whose leading stack frames a captured emitter stack skips, so attribution starts at the code that sent the event
        // rather than at the bus internals and this recorder. Matched by assembly (not namespace) so user code that happens to live under a
        // SideXP.Broadcaster* namespace is never mistaken for the bus.
        private static readonly Assembly BroadcasterRuntimeAssembly = typeof(EventBus).Assembly;
        private static readonly Assembly BroadcasterEditorAssembly = typeof(SpanRecorder).Assembly;

        private readonly List<RecordedSpan> _spans = new List<RecordedSpan>();
        private readonly List<Violation> _violations = new List<Violation>();

        private EventBus _bus;
        private int _capacity = DefaultCapacity;
        private int _maxFrameSpan;
        private bool _isRecording = true;

        /// <summary>Raised whenever the retained data changed (a span began or finished, a violation arrived, a trim or a clear), so the window can repaint.</summary>
        public event Action Changed;

        /// <summary>The retained spans, oldest first.</summary>
        public IReadOnlyList<RecordedSpan> Spans => _spans;

        /// <summary>The retained violations, oldest first.</summary>
        public IReadOnlyList<Violation> Violations => _violations;

        /// <summary>Whether a bus is currently attached.</summary>
        public bool IsAttached => _bus != null;

        /// <summary>
        /// How many spans, and how many violations, to retain. Lowering it drops the oldest entries immediately. Clamped to at least one.
        /// </summary>
        public int Capacity
        {
            get => _capacity;
            set
            {
                int clamped = value < 1 ? 1 : value;
                if (clamped == _capacity)
                    return;

                _capacity = clamped;
                if (TrimToCapacity())
                    Changed?.Invoke();
            }
        }

        /// <summary>
        /// Whether new dispatches are being captured. While false (paused), spans and violations the bus reports are ignored — but a span
        /// captured while recording still reflects completion afterwards, because the bus owns and mutates that instance.
        /// </summary>
        public bool IsRecording
        {
            get => _isRecording;
            set => _isRecording = value;
        }

        /// <summary>
        /// How many of the most recent frames to retain: a dispatch or violation older than this many frames behind the newest one is
        /// dropped, so a long-running session's timeline stops compressing endlessly. Zero (the default) keeps everything, bounded only by
        /// <see cref="Capacity"/>. Clamped to at least zero; lowering it drops the now-expired entries immediately.
        /// </summary>
        public int MaxFrameSpan
        {
            get => _maxFrameSpan;
            set
            {
                int clamped = value < 0 ? 0 : value;
                if (clamped == _maxFrameSpan)
                    return;

                _maxFrameSpan = clamped;
                if (TryGetNewestFrame(out int reference) && TrimToFrameWindow(reference))
                    Changed?.Invoke();
            }
        }

        /// <summary>
        /// Whether to capture the emitter's call stack when a span begins, for attribution in the window. Off by default: taking a managed
        /// stack per dispatch is costly, so it's an opt-in the window toggles. It never reaches release builds — the hooks that feed it don't
        /// exist there.
        /// </summary>
        public bool CaptureEmitterStacks { get; set; }

        /// <summary>
        /// Starts recording <paramref name="bus"/>, detaching from any previously recorded one first. Retained data is kept, so re-attaching
        /// across a play-mode transition doesn't wipe the timeline; call <see cref="Clear"/> to reset. Passing <c>null</c> just detaches.
        /// </summary>
        public void Attach(EventBus bus)
        {
            Detach();

            _bus = bus;
            if (_bus == null)
                return;

            _bus.OnSpanBegan += HandleSpanBegan;
            _bus.OnSpanEnded += HandleSpanEnded;
            _bus.OnListenerEnded += HandleListenerEnded;
            _bus.OnViolation += HandleViolation;
        }

        /// <summary>
        /// Stops recording the current bus. Safe to call when not attached. Retained data is left as-is.
        /// </summary>
        public void Detach()
        {
            if (_bus == null)
                return;

            _bus.OnSpanBegan -= HandleSpanBegan;
            _bus.OnSpanEnded -= HandleSpanEnded;
            _bus.OnListenerEnded -= HandleListenerEnded;
            _bus.OnViolation -= HandleViolation;
            _bus = null;
        }

        /// <summary>
        /// Drops every retained span and violation. Does nothing (and stays quiet) when there was nothing to drop.
        /// </summary>
        public void Clear()
        {
            if (_spans.Count == 0 && _violations.Count == 0)
                return;

            _spans.Clear();
            _violations.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// The range of frames the retained dispatches cover: the earliest frame any began on, and the latest frame any of them reached (a
        /// still-open span reaches only its begin frame here — the window extends it to the current frame as it draws). Returns false, with
        /// both outputs zero, when nothing is retained.
        /// </summary>
        public bool TryGetFrameRange(out int firstFrame, out int lastFrame)
        {
            firstFrame = 0;
            lastFrame = 0;
            if (_spans.Count == 0)
                return false;

            bool any = false;
            foreach (RecordedSpan recorded in _spans)
            {
                DispatchSpan span = recorded.Span;
                int spanLast = span.IsComplete ? span.EndFrame : span.BeginFrame;
                if (!any)
                {
                    firstFrame = span.BeginFrame;
                    lastFrame = spanLast;
                    any = true;
                    continue;
                }

                if (span.BeginFrame < firstFrame) firstFrame = span.BeginFrame;
                if (spanLast > lastFrame) lastFrame = spanLast;
            }
            return any;
        }

        private void HandleSpanBegan(DispatchSpan span)
        {
            if (!_isRecording)
                return;

            _spans.Add(new RecordedSpan(span, CaptureEmitterStacks ? CaptureStack() : null));
            TrimToCapacity();
            TrimToFrameWindow(span.BeginFrame);
            Changed?.Invoke();
        }

        // A span (and its listener sub-spans) mutates in place as it finishes; we hold the same instance, so there is nothing to store on
        // completion — just prompt a repaint so an in-flight block redraws as it closes.
        private void HandleSpanEnded(DispatchSpan span) => Changed?.Invoke();

        private void HandleListenerEnded(ListenerSpan listener) => Changed?.Invoke();

        private void HandleViolation(Violation violation)
        {
            if (!_isRecording)
                return;

            _violations.Add(violation);
            TrimToCapacity();
            TrimToFrameWindow(violation.Frame);
            Changed?.Invoke();
        }

        // Evicts oldest-first down to capacity. Front removal on a List is O(n), which is fine at editor-tooling scale (a few hundred
        // entries) and keeps the retained order directly indexable for rendering.
        private bool TrimToCapacity()
        {
            bool trimmed = false;
            if (_spans.Count > _capacity)
            {
                _spans.RemoveRange(0, _spans.Count - _capacity);
                trimmed = true;
            }
            if (_violations.Count > _capacity)
            {
                _violations.RemoveRange(0, _violations.Count - _capacity);
                trimmed = true;
            }
            return trimmed;
        }

        // Drops the leading (oldest) spans and violations that fall outside the frame window ending at referenceFrame. Both stores are
        // oldest-first with non-decreasing frames (spans in begin order, violations in arrival order), so the expired ones are always a
        // prefix. A window of zero is unbounded and trims nothing.
        private bool TrimToFrameWindow(int referenceFrame)
        {
            if (_maxFrameSpan <= 0)
                return false;

            bool trimmed = false;

            int spansToDrop = 0;
            while (spansToDrop < _spans.Count && IsExpiredByFrame(_spans[spansToDrop].Span.BeginFrame, referenceFrame, _maxFrameSpan))
                spansToDrop++;
            if (spansToDrop > 0)
            {
                _spans.RemoveRange(0, spansToDrop);
                trimmed = true;
            }

            int violationsToDrop = 0;
            while (violationsToDrop < _violations.Count && IsExpiredByFrame(_violations[violationsToDrop].Frame, referenceFrame, _maxFrameSpan))
                violationsToDrop++;
            if (violationsToDrop > 0)
            {
                _violations.RemoveRange(0, violationsToDrop);
                trimmed = true;
            }

            return trimmed;
        }

        // The newest frame currently retained, used as the window's right edge when the window size changes (a live capture uses the
        // incoming dispatch's frame instead). False when nothing is retained.
        private bool TryGetNewestFrame(out int frame)
        {
            frame = 0;
            bool any = false;
            if (_spans.Count > 0)
            {
                frame = _spans[_spans.Count - 1].Span.BeginFrame;
                any = true;
            }
            if (_violations.Count > 0)
            {
                int violationFrame = _violations[_violations.Count - 1].Frame;
                frame = any ? System.Math.Max(frame, violationFrame) : violationFrame;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Whether an entry on <paramref name="frame"/> falls outside a window of <paramref name="maxFrameSpan"/> frames ending at
        /// <paramref name="referenceFrame"/> (the newest activity). A window of zero is unbounded, so nothing is ever expired; the window
        /// is inclusive of the reference frame, so the last <paramref name="maxFrameSpan"/> frames are kept.
        /// </summary>
        internal static bool IsExpiredByFrame(int frame, int referenceFrame, int maxFrameSpan)
        {
            return maxFrameSpan > 0 && frame <= referenceFrame - maxFrameSpan;
        }

        // Captures the managed call stack at emit time, dropping the leading frames that are inside Broadcaster itself so attribution starts
        // at the code that actually sent the event. Only called when CaptureEmitterStacks is on.
        private static string CaptureStack()
        {
            StackTrace trace = new StackTrace(1, true);
            StringBuilder builder = new StringBuilder();
            bool reachedCaller = false;

            for (int i = 0; i < trace.FrameCount; i++)
            {
                StackFrame frame = trace.GetFrame(i);
                MethodBase method = frame?.GetMethod();
                if (method == null)
                    continue;

                if (!reachedCaller)
                {
                    Assembly assembly = method.DeclaringType?.Assembly;
                    if (assembly == BroadcasterRuntimeAssembly || assembly == BroadcasterEditorAssembly)
                        continue;
                    reachedCaller = true;
                }

                if (builder.Length > 0)
                    builder.Append('\n');
                builder.Append(method.DeclaringType?.FullName).Append('.').Append(method.Name);

                string file = frame.GetFileName();
                if (!string.IsNullOrEmpty(file))
                    builder.Append(" (").Append(System.IO.Path.GetFileName(file)).Append(':').Append(frame.GetFileLineNumber()).Append(')');
            }

            return builder.ToString();
        }

    }

    /// <summary>
    /// One retained dispatch: the monitor's <see cref="DispatchSpan"/> plus the recorder-side extras it doesn't carry (currently the
    /// emitter's captured call stack), present only when stack capture was on when the span began.
    /// </summary>
    /// <remarks>
    /// Holds the live span instance (a reference, stable), so reading <see cref="Span"/> always reflects its latest state, completion
    /// included.
    /// </remarks>
    public readonly struct RecordedSpan
    {

        /// <summary>The dispatch this record is for.</summary>
        public DispatchSpan Span { get; }

        /// <summary>The emitter's call stack captured when the span began, or <c>null</c> when stack capture was off.</summary>
        public string EmitterStack { get; }

        /// <inheritdoc cref="RecordedSpan"/>
        public RecordedSpan(DispatchSpan span, string emitterStack)
        {
            Span = span;
            EmitterStack = emitterStack;
        }

    }

}
