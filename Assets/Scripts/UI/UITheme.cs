using UnityEngine;

namespace ScrapSiege.UI
{
    /// <summary>
    /// The single source of truth for HUD colors and type sizes. Everything on the Canvas is
    /// built from these so the UI reads as one designed system rather than a pile of default
    /// Unity buttons - and so a palette change is one edit, not thirty Inspector fields.
    ///
    /// Palette intent: scrapyard industrial. Near-black steel surfaces behind the camera feed,
    /// hot rust-amber for the primary action of each phase, cold steel-blue for secondary
    /// choices, so the player's next tap is always the brightest thing on screen.
    /// </summary>
    public static class UITheme
    {
        public static readonly Color Ink = Hex("0E1116F2");           // scrim behind modal cards
        public static readonly Color Surface = Hex("161B23E6");       // bars and chips over camera feed
        public static readonly Color SurfaceRaised = Hex("232B36FF"); // cards, segmented-control track
        public static readonly Color Stroke = Hex("3A4553FF");

        public static readonly Color Accent = Hex("FF8A3DFF");        // primary action
        public static readonly Color AccentPressed = Hex("D96A22FF");
        public static readonly Color Steel = Hex("4A9EFFFF");         // secondary / selected toggle
        public static readonly Color Success = Hex("3DD68CFF");
        public static readonly Color Danger = Hex("FF5F56FF");

        public static readonly Color TextPrimary = Hex("EDF1F5FF");
        public static readonly Color TextMuted = Hex("94A1B2FF");
        public static readonly Color TextOnAccent = Hex("1A1206FF");  // dark ink on amber reads better than white

        public const float TitleSize = 52f;
        public const float PhaseLabelSize = 30f;
        public const float PromptSize = 38f;
        public const float BodySize = 32f;
        public const float ButtonSize = 34f;
        public const float CaptionSize = 26f;

        /// <summary>Same fill, dimmed - used for the unselected half of a segmented control.</summary>
        public static Color Dim(Color color, float amount = 0.45f)
        {
            return new Color(color.r * amount, color.g * amount, color.b * amount, color.a);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
