using UnityEditor;
using UnityEngine;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// The shared accent colors for the four event kinds, so every Broadcaster editor window colorizes them the same way (the events tree's
    /// kind bars, tags and row tints today; the timeline and selection windows later). Each color is theme-aware (brighter on the dark skin,
    /// deeper on the light one) so read it at draw time rather than caching it.
    /// </summary>
    public static class EventKindColors
    {

        /// <summary>
        /// Alpha applied by <see cref="Background(EventKind)"/> for a faint kind tint: low enough that a tree's alternating light/dark row
        /// banding still shows through when the tint is drawn over it.
        /// </summary>
        public const float BackgroundAlpha = 0.06f;

        /// <summary>Accent for signals (past, "it happened").</summary>
        public static Color Signal => Pick(new Color(0.40f, 0.65f, 0.95f), new Color(0.20f, 0.45f, 0.85f)); // blue

        /// <summary>Accent for cues (presentation-tempo feedback).</summary>
        public static Color Cue => Pick(new Color(0.72f, 0.55f, 0.95f), new Color(0.52f, 0.34f, 0.80f)); // purple

        /// <summary>Accent for commands (an action to perform).</summary>
        public static Color Command => Pick(new Color(0.95f, 0.65f, 0.35f), new Color(0.82f, 0.48f, 0.14f)); // orange

        /// <summary>Accent for requests (a question to answer).</summary>
        public static Color Request => Pick(new Color(0.45f, 0.80f, 0.50f), new Color(0.24f, 0.58f, 0.30f)); // green

        /// <summary>The accent color for a kind, or gray for an unknown value.</summary>
        public static Color Get(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Signal: return Signal;
                case EventKind.Cue: return Cue;
                case EventKind.Command: return Command;
                case EventKind.Request: return Request;
                default: return Color.gray;
            }
        }

        /// <summary>
        /// The kind's accent at <see cref="BackgroundAlpha"/>, a faint fill meant to be drawn over a row so its background banding shows
        /// through the transparency.
        /// </summary>
        public static Color Background(EventKind kind)
        {
            Color color = Get(kind);
            color.a = BackgroundAlpha;
            return color;
        }

        // The first color on the dark (pro) skin, the second on the light one.
        private static Color Pick(Color pro, Color light)
        {
            return EditorGUIUtility.isProSkin ? pro : light;
        }
    }

}
