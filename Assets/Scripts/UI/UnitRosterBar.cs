using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ScrapSiege.Monetization;
using ScrapSiege.Siege;

namespace ScrapSiege.UI
{
    /// <summary>
    /// The deploy roster: one tappable chip per unit class, built at runtime from
    /// <see cref="UnitRoster"/>.
    ///
    /// <para><b>Built in code rather than from a scene template</b>, unlike the level cards in
    /// <see cref="MainMenuController"/>. The level list is a scrollable column of large cards where
    /// layout mistakes are obvious; this is a dense row of small chips inside an already-tight
    /// 180px bottom bar, and every hand-authored version of it would have to be re-authored the
    /// moment a sixth class ships. Generating it means a new class is an asset and a roster entry
    /// with no scene surgery - which is the same promise the level format already makes.</para>
    ///
    /// <para>Locked (Pro) classes are shown, not hidden: a chip the player can see and cannot use is
    /// an upsell, whereas a hidden one sells nothing. Tapping a locked chip opens the paywall.</para>
    /// </summary>
    public class UnitRosterBar : MonoBehaviour
    {
        [Tooltip("Where the selected class is sent. Also the source of the roster asset.")]
        [SerializeField] private UnitDeploymentController deployment;

        [Tooltip("Optional. Kept in step with the selection so a scoped Rally follows the class the " +
                 "player is currently thinking about.")]
        [SerializeField] private RallyController rally;

        [Tooltip("Shown when a locked class is tapped. Optional - without it the tap is ignored.")]
        [SerializeField] private GameObject paywallPanel;

        [Tooltip("Line under the bar describing the selected class. Optional.")]
        [SerializeField] private TMP_Text taglineLabel;

        [Header("Layout")]
        [SerializeField] private float chipSpacing = 8f;
        [SerializeField] private float chipHeight = 62f;

        private readonly List<Chip> chips = new List<Chip>();

        private struct Chip
        {
            public UnitClass Class;
            public UnityEngine.UI.Image Fill;
            public TMP_Text Label;
            public TMP_Text Cost;
        }

        private void Awake()
        {
            if (deployment == null)
            {
                Debug.LogError("UnitRosterBar: Deployment is not assigned - no chips can be built and the " +
                               "player will have no way to choose a unit.", this);
                return;
            }

            EnsureLayout();
            Build();
        }

        private void OnEnable()
        {
            ProEntitlement.Changed += OnProChanged;
            if (deployment != null) deployment.OnSelectedClassChanged.AddListener(OnSelectionChanged);
        }

        private void OnDisable()
        {
            ProEntitlement.Changed -= OnProChanged;
            if (deployment != null) deployment.OnSelectedClassChanged.RemoveListener(OnSelectionChanged);
        }

        private void Start()
        {
            // The deployment controller resolves its own default in Start too; refreshing here picks
            // that up whichever order the two run in.
            OnSelectionChanged(deployment != null ? deployment.SelectedClass : null);
        }

        /// <summary>
        /// A purchase can land mid-match, and a class that unlocks without the chip changing reads
        /// as the purchase not having worked. Same reasoning as MainMenuController's handler.
        /// </summary>
        private void OnProChanged(bool unlocked) => Refresh();

        private void EnsureLayout()
        {
            var layout = GetComponent<HorizontalLayoutGroup>();
            if (layout == null) layout = gameObject.AddComponent<HorizontalLayoutGroup>();

            layout.spacing = chipSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;
        }

        private void Build()
        {
            foreach (var chip in chips)
                if (chip.Fill != null) Destroy(chip.Fill.gameObject);
            chips.Clear();

            UnitRoster roster = deployment != null ? deployment.Roster : null;
            if (roster == null)
            {
                // Not an error: the legacy scan/Fortify flow has no roster and deploys one unit type.
                gameObject.SetActive(false);
                return;
            }

            foreach (var unitClass in roster.Ordered)
                chips.Add(BuildChip(unitClass));

            Refresh();
        }

        private Chip BuildChip(UnitClass unitClass)
        {
            var go = new GameObject($"Chip_{unitClass.displayName}", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, chipHeight);

            var fill = go.AddComponent<UnityEngine.UI.Image>();
            fill.color = UITheme.Surface;

            var button = go.AddComponent<Button>();
            button.targetGraphic = fill;

            // Captured locally so each listener refers to its own class rather than the loop
            // variable's final value - the same trap MainMenuController.PopulateCard guards.
            UnitClass captured = unitClass;
            button.onClick.AddListener(() => OnChipTapped(captured));

            var label = MakeText(go.transform, "Label", ChipName(unitClass), 30f,
                                 new Vector2(0f, 0.34f), new Vector2(1f, 1f));
            var cost = MakeText(go.transform, "Cost", CostText(unitClass), 20f,
                                new Vector2(0f, 0f), new Vector2(1f, 0.36f));

            return new Chip { Class = unitClass, Fill = fill, Label = label, Cost = cost };
        }

        private static TMP_Text MakeText(Transform parent, string name, string content, float size,
                                         Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = size;

            // A chip is smaller than a fingertip; a label that eats the tap would make the whole
            // bar feel broken in exactly the way "I tap and nothing happens" already has once here.
            text.raycastTarget = false;

            return text;
        }

        /// <summary>
        /// The full class name, not <c>shortLabel</c>.
        ///
        /// <para>The chips were originally authored as three-letter codes ("TRP", "MKS") on the
        /// assumption that five of them plus a cost had to share a phone-width bar. Measured on the
        /// real 1920-reference canvas that assumption is simply wrong: with the roster in its own
        /// row above the bottom bar, each chip is over 200px wide, which fits the longest name
        /// ("Marksman") at full size with room to spare. Codes cost a new player a translation step
        /// on every single tap, in a game whose whole pitch is that you can read the battlefield at
        /// a glance - and they read as placeholder art on camera, which matters for the demo video.
        /// <c>shortLabel</c> is kept on the asset for genuinely narrow surfaces (the rally scope
        /// chip), not deleted.</para>
        /// </summary>
        private static string ChipName(UnitClass unitClass)
        {
            if (unitClass == null) return string.Empty;

            string name = string.IsNullOrWhiteSpace(unitClass.displayName)
                ? unitClass.shortLabel
                : unitClass.displayName;

            return name.ToUpperInvariant();
        }

        /// <summary>
        /// "1 SCRAP" rather than a bare "1". The number alone sat under a name with no unit, which
        /// reads as a quantity of units to deploy rather than a price - the one genuinely ambiguous
        /// thing on the bar.
        /// </summary>
        private static string CostText(UnitClass unitClass)
            => unitClass == null ? string.Empty : $"{unitClass.cost} SCRAP";

        private void OnChipTapped(UnitClass unitClass)
        {
            ScrapSiege.Audio.GameAudio.Play(ScrapSiege.Audio.Sfx.ClassSelect, 0.6f);

            if (!UnitRoster.IsUnlocked(unitClass))
            {
                if (paywallPanel != null) paywallPanel.SetActive(true);
                else Debug.Log($"UnitRosterBar: '{unitClass.displayName}' is Pro-locked and no paywall panel is assigned.");
                return;
            }

            if (deployment != null) deployment.SelectClass(unitClass);
        }

        private void OnSelectionChanged(UnitClass unitClass)
        {
            // Rally follows the deploy selection by default, so "the thing I am building" and "the
            // thing I am commanding" are the same without a second control to manage. The HUD's
            // scope toggle can still widen it back to ALL.
            if (rally != null && rally.Scope != null) rally.SetScope(unitClass);

            if (taglineLabel != null)
                taglineLabel.text = unitClass != null ? unitClass.tagline : string.Empty;

            Refresh();
        }

        private void Refresh()
        {
            UnitClass selected = deployment != null ? deployment.SelectedClass : null;

            foreach (var chip in chips)
            {
                if (chip.Fill == null) continue;

                bool unlocked = UnitRoster.IsUnlocked(chip.Class);
                bool isSelected = chip.Class == selected;

                chip.Fill.color = isSelected ? UITheme.Steel
                                : unlocked ? UITheme.SurfaceRaised
                                : UITheme.Surface;

                if (chip.Label != null)
                {
                    // The name stays visible when locked rather than becoming a padlock glyph -
                    // knowing there is a "TURRET" behind the paywall is the upsell; a padlock is
                    // not, and a glyph outside the TMP font asset's character set would render as a
                    // blank box anyway.
                    chip.Label.text = ChipName(chip.Class);
                    chip.Label.color = unlocked ? UITheme.TextPrimary : UITheme.TextMuted;
                }

                if (chip.Cost != null)
                {
                    chip.Cost.text = unlocked ? CostText(chip.Class) : "PRO";
                    chip.Cost.color = isSelected ? UITheme.TextPrimary : UITheme.TextMuted;
                }
            }
        }
    }
}
