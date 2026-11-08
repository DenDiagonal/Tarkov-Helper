using System.Windows.Media;

namespace TarkovHelper.Common
{
    /// <summary>
    /// Centralized cache of frozen SolidColorBrush instances for the 5-level tactical dark palette
    /// and tinted status HUD badges inspired by Escape from Tarkov and TarkovTracker.
    /// Eliminates runtime heap allocations in code-behind rendering and list virtualization.
    /// </summary>
    public static class TacticalBrushes
    {
        // ---------------------------------------------------------------------
        // 1. Surface Elevation System (5 Levels)
        // ---------------------------------------------------------------------

        /// <summary>Level 0 (#0D0E11): Deep screen canvas, backdrop behind cards</summary>
        public static readonly SolidColorBrush Surface0 = Freeze(new SolidColorBrush(Color.FromRgb(0x0D, 0x0E, 0x11)));

        /// <summary>Level 1 (#131519): Page toolbars, top header bar, map background canvas</summary>
        public static readonly SolidColorBrush Surface1 = Freeze(new SolidColorBrush(Color.FromRgb(0x13, 0x15, 0x19)));

        /// <summary>Level 2 (#1A1D23): Main task cards, dialog cards, elevated panels</summary>
        public static readonly SolidColorBrush Surface2 = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x23)));

        /// <summary>Level 3 (#21252D): Sub-objective insets, item turn-in boxes, search boxes</summary>
        public static readonly SolidColorBrush Surface3 = Freeze(new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2D)));

        /// <summary>Level 4 (#292E38): Interactive stepper buttons, dropdown popups, active hover states</summary>
        public static readonly SolidColorBrush Surface4 = Freeze(new SolidColorBrush(Color.FromRgb(0x29, 0x2E, 0x38)));

        // ---------------------------------------------------------------------
        // 2. Tactical Borders
        // ---------------------------------------------------------------------

        /// <summary>Subtle delimiter (#242832): Inset borders, internal dividers</summary>
        public static readonly SolidColorBrush BorderSubtle = Freeze(new SolidColorBrush(Color.FromRgb(0x24, 0x28, 0x32)));

        /// <summary>Medium border (#323846): Card outlines, inputs, comboboxes</summary>
        public static readonly SolidColorBrush BorderMedium = Freeze(new SolidColorBrush(Color.FromRgb(0x32, 0x38, 0x46)));

        /// <summary>Highlight border (#4A5368): Focus outlines, hover state borders</summary>
        public static readonly SolidColorBrush BorderHighlight = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x53, 0x68)));

        /// <summary>Tactical Gold border (#C5A84A): Active quest selection, primary focus</summary>
        public static readonly SolidColorBrush BorderGold = Freeze(new SolidColorBrush(Color.FromRgb(0xC5, 0xA8, 0x4A)));

        /// <summary>Subtle Gold border (#6A5A2B): FIR warning boxes, subtle quest alerts</summary>
        public static readonly SolidColorBrush BorderSubtleGold = Freeze(new SolidColorBrush(Color.FromRgb(0x6A, 0x5A, 0x2B)));

        // ---------------------------------------------------------------------
        // 3. Status Tinted Badges (Background + Border + Foreground)
        // ---------------------------------------------------------------------

        // Active / Available
        public static readonly SolidColorBrush ActiveBg = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x25, 0x18)));
        public static readonly SolidColorBrush ActiveBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x4D, 0x2C)));
        public static readonly SolidColorBrush ActiveFg = Freeze(new SolidColorBrush(Color.FromRgb(0x8F, 0xC0, 0x60)));
        public static readonly SolidColorBrush ActiveBadgeBg = ActiveBg;
        public static readonly SolidColorBrush ActiveBadgeBorder = ActiveBorder;
        public static readonly SolidColorBrush ActiveBadgeFg = ActiveFg;

        // Completed / Done
        public static readonly SolidColorBrush DoneBg = Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x20, 0x2E)));
        public static readonly SolidColorBrush DoneBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x45, 0x63)));
        public static readonly SolidColorBrush DoneFg = Freeze(new SolidColorBrush(Color.FromRgb(0x82, 0xBC, 0xE8)));
        public static readonly SolidColorBrush CompletedBg = DoneBg;
        public static readonly SolidColorBrush CompletedBorder = DoneBorder;
        public static readonly SolidColorBrush CompletedFg = DoneFg;
        public static readonly SolidColorBrush DoneBadgeBg = DoneBg;
        public static readonly SolidColorBrush DoneBadgeBorder = DoneBorder;
        public static readonly SolidColorBrush DoneBadgeFg = DoneFg;

        // Locked
        public static readonly SolidColorBrush LockedBg = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x24)));
        public static readonly SolidColorBrush LockedBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x35, 0x3B, 0x47)));
        public static readonly SolidColorBrush LockedFg = Freeze(new SolidColorBrush(Color.FromRgb(0x8C, 0x93, 0xA0)));
        public static readonly SolidColorBrush LockedBadgeBg = LockedBg;
        public static readonly SolidColorBrush LockedBadgeBorder = LockedBorder;
        public static readonly SolidColorBrush LockedBadgeFg = LockedFg;

        // Level Locked
        public static readonly SolidColorBrush LevelLockedBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x1E, 0x10)));
        public static readonly SolidColorBrush LevelLockedBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0x3E, 0x1B)));
        public static readonly SolidColorBrush LevelLockedFg = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xB2, 0x4D)));
        public static readonly SolidColorBrush LevelLockedBadgeBg = LevelLockedBg;
        public static readonly SolidColorBrush LevelLockedBadgeBorder = LevelLockedBorder;
        public static readonly SolidColorBrush LevelLockedBadgeFg = LevelLockedFg;

        // Failed
        public static readonly SolidColorBrush FailedBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x14, 0x14)));
        public static readonly SolidColorBrush FailedBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x23, 0x22)));
        public static readonly SolidColorBrush FailedFg = Freeze(new SolidColorBrush(Color.FromRgb(0xEB, 0x78, 0x73)));
        public static readonly SolidColorBrush FailedBadgeBg = FailedBg;
        public static readonly SolidColorBrush FailedBadgeBorder = FailedBorder;
        public static readonly SolidColorBrush FailedBadgeFg = FailedFg;

        // FIR (Found in Raid)
        public static readonly SolidColorBrush FirBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x1A, 0x0E)));
        public static readonly SolidColorBrush FirBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x3B, 0x18)));
        public static readonly SolidColorBrush FirFg = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x9A, 0x3E)));
        public static readonly SolidColorBrush FirBadgeBg = FirBg;
        public static readonly SolidColorBrush FirBadgeBorder = FirBorder;
        public static readonly SolidColorBrush FirBadgeFg = FirFg;

        // Kappa
        public static readonly SolidColorBrush KappaBg = Freeze(new SolidColorBrush(Color.FromRgb(0x23, 0x15, 0x35)));
        public static readonly SolidColorBrush KappaBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x51, 0x2E, 0x7B)));
        public static readonly SolidColorBrush KappaFg = Freeze(new SolidColorBrush(Color.FromRgb(0xD1, 0xA8, 0xFF)));
        public static readonly SolidColorBrush KappaBadgeBg = KappaBg;
        public static readonly SolidColorBrush KappaBadgeBorder = KappaBorder;
        public static readonly SolidColorBrush KappaBadgeFg = KappaFg;

        // Story
        public static readonly SolidColorBrush StoryBg = Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x1E, 0x2E)));
        public static readonly SolidColorBrush StoryBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x43, 0x66)));
        public static readonly SolidColorBrush StoryFg = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0xAE, 0xE8)));
        public static readonly SolidColorBrush StoryBadgeBg = StoryBg;
        public static readonly SolidColorBrush StoryBadgeBorder = StoryBorder;
        public static readonly SolidColorBrush StoryBadgeFg = StoryFg;

        // Keys
        public static readonly SolidColorBrush KeysRequiredBg = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x28, 0x28)));
        public static readonly SolidColorBrush KeysRequiredBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x4E, 0x4E)));
        public static readonly SolidColorBrush KeysRequiredFg = Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0xB4, 0xB4)));

        public static readonly SolidColorBrush KeyInStashBg = Freeze(new SolidColorBrush(Color.FromRgb(0x17, 0x2A, 0x22)));
        public static readonly SolidColorBrush KeyInStashBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x59, 0x43)));
        public static readonly SolidColorBrush KeyInStashFg = Freeze(new SolidColorBrush(Color.FromRgb(0x78, 0xC9, 0xA0)));

        public static readonly SolidColorBrush KeyMissingBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x19, 0x19)));
        public static readonly SolidColorBrush KeyMissingBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x59, 0x30, 0x30)));
        public static readonly SolidColorBrush KeyMissingFg = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x82, 0x82)));

        // ---------------------------------------------------------------------
        // 4. Typography
        // ---------------------------------------------------------------------

        /// <summary>Primary text (#EDEDED): High contrast headings, values, titles</summary>
        public static readonly SolidColorBrush TextPrimary = Freeze(new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED)));

        /// <summary>Secondary text (#9EA4B0): Subtitles, trader names, metadata</summary>
        public static readonly SolidColorBrush TextSecondary = Freeze(new SolidColorBrush(Color.FromRgb(0x9E, 0xA4, 0xB0)));

        /// <summary>Muted text (#686E7C): Disabled labels, placeholders, inactive hints</summary>
        public static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(0x68, 0x6E, 0x7C)));

        /// <summary>Inverted text (#101114): Text on solid Gold or bright accents</summary>
        public static readonly SolidColorBrush TextInverted = Freeze(new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)));

        // ---------------------------------------------------------------------
        // 5. Tactical Accents
        // ---------------------------------------------------------------------

        /// <summary>EFT Tactical Gold (#C5A84A)</summary>
        public static readonly SolidColorBrush TacticalGold = Freeze(new SolidColorBrush(Color.FromRgb(0xC5, 0xA8, 0x4A)));

        /// <summary>EFT Tactical Gold Hover (#D8BB5C)</summary>
        public static readonly SolidColorBrush TacticalGoldHover = Freeze(new SolidColorBrush(Color.FromRgb(0xD8, 0xBB, 0x5C)));

        /// <summary>Tactical Military Olive / Success (#7FA655)</summary>
        public static readonly SolidColorBrush TacticalSuccess = Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0xA6, 0x55)));

        /// <summary>Tactical Burnt Amber / Warning (#E5A138)</summary>
        public static readonly SolidColorBrush TacticalWarning = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x38)));

        /// <summary>Tactical Danger / Blood Red (#E05D58)</summary>
        public static readonly SolidColorBrush TacticalDanger = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x5D, 0x58)));

        /// <summary>Tactical UN Blue / Info (#6AA9D9)</summary>
        public static readonly SolidColorBrush TacticalInfo = Freeze(new SolidColorBrush(Color.FromRgb(0x6A, 0xA9, 0xD9)));

        // ---------------------------------------------------------------------
        // 6. Interactive State Surfaces
        // ---------------------------------------------------------------------

        public static readonly SolidColorBrush ButtonNormalBg = Surface2;
        public static readonly SolidColorBrush ButtonHoverBg = Surface4;
        public static readonly SolidColorBrush ButtonPressedBg = BorderMedium;
        public static readonly SolidColorBrush InactiveBg = Surface3;

        // ---------------------------------------------------------------------
        // Helper
        // ---------------------------------------------------------------------

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }
            return brush;
        }
    }
}
