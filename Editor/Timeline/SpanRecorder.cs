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
    /// One retained dispatch: the monitor's <see cref="DispatchSpan"/> plus the recorder-side extras it doesn't carry — currently the
    /// emitter's captured call stack, present only when stack capture was on when the span began.
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
