using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ScrapSiege.Levels;
using ScrapSiege.Monetization;

namespace ScrapSiege.UI
{
    /// <summary>
    /// The start screen and level select.
    ///
    /// Lives in its own scene so the match scene never has to boot AR just to show a menu -
    /// ARCore session startup is slow and can fail, and neither should stand between the player
    /// and the main menu. The chosen level is handed to the match scene through
    /// <see cref="LevelCatalog.Selected"/>.
    ///
    /// Level cards are generated from the catalog rather than hand-placed, so shipping a new level
    /// is a ScriptableObject and nothing else - which is the whole point of the authored format.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private LevelCatalog catalog;

        [Tooltip("Name of the scene containing the AR match. Must be in Build Settings.")]
        [SerializeField] private string matchSceneName = "ARTest";

        [Header("Screens")]
        [SerializeField] private GameObject titleScreen;
        [SerializeField] private GameObject levelSelectScreen;

        [Header("Level select")]
        [Tooltip("Parent with a layout group; one card is instantiated per level.")]
        [SerializeField] private RectTransform levelListParent;

        [Tooltip("Template card, disabled in the scene and cloned per level.")]
        [SerializeField] private GameObject levelCardTemplate;

        [Header("Level select paging")]
        [Tooltip("Cards per page. The list is laid out in a fixed-height row inside a 1080-tall " +
                 "landscape canvas, so beyond about four the cards stop being tappable - which is " +
                 "why this is paged rather than scrolled: a page is a definite place a player can " +
                 "return to, and it needs no scroll inertia tuning on a device that is also being " +
                 "held up to a table.")]
        [SerializeField] private int levelsPerPage = 4;

        [SerializeField] private Button previousPageButton;
        [SerializeField] private Button nextPageButton;

        [Tooltip("Reads 'PAGE 1 / 3'. Optional.")]
        [SerializeField] private TMP_Text pageLabel;

        [Header("Paywall")]
        [SerializeField] private GameObject paywallPanel;

        [Tooltip("Shown only while the 'pro' entitlement is active, so the purchase has a visible " +
                 "effect that does not depend on noticing a cosmetic palette change.")]
        [SerializeField] private GameObject proActiveBadge;

        [Tooltip("The Go Pro button, hidden once Pro is active - offering an upgrade the player " +
                 "already owns reads as a bug.")]
        [SerializeField] private GameObject goProButton;

        private readonly List<GameObject> spawnedCards = new List<GameObject>();
        private readonly List<LevelDefinition> orderedLevels = new List<LevelDefinition>();
        private int pageIndex;

        private void Awake()
        {
            if (catalog == null) Debug.LogError("MainMenuController: Catalog is not assigned - the level list will be empty.", this);
            if (levelListParent == null) Debug.LogError("MainMenuController: Level List Parent is not assigned - level cards have nowhere to go.", this);
            if (levelCardTemplate == null) Debug.LogError("MainMenuController: Level Card Template is not assigned - no cards can be built.", this);
            else levelCardTemplate.SetActive(false);
        }

        private void OnEnable()
        {
            ProEntitlement.Changed += OnProEntitlementChanged;
        }

        private void OnDisable()
        {
            ProEntitlement.Changed -= OnProEntitlementChanged;
        }

        private void Start()
        {
            ShowTitle();
            ApplyProState();
        }

        /// <summary>
        /// The entitlement resolves asynchronously (RevenueCat has to reach the network), and a
        /// purchase can complete while the paywall is sitting on top of an already-built level list.
        /// Neither case re-enters <see cref="ShowLevelSelect"/>, so without this the menu keeps
        /// showing Pro levels as locked until the app is restarted.
        /// </summary>
        private void OnProEntitlementChanged(bool unlocked)
        {
            Debug.Log($"MainMenuController: Pro entitlement changed to {unlocked} - refreshing menu.");
            ApplyProState();

            // Only worth rebuilding if the list is actually on screen; ShowLevelSelect rebuilds it
            // from scratch on every entry anyway.
            if (levelSelectScreen != null && levelSelectScreen.activeInHierarchy)
                BuildLevelList();
        }

        private void ApplyProState()
        {
            bool pro = ProEntitlement.IsUnlocked;
            if (proActiveBadge != null) proActiveBadge.SetActive(pro);
            if (goProButton != null) goProButton.SetActive(!pro);
        }

        public void ShowTitle()
        {
            if (titleScreen != null) titleScreen.SetActive(true);
            if (levelSelectScreen != null) levelSelectScreen.SetActive(false);
        }

        /// <summary>Wire to the PLAY button.</summary>
        public void ShowLevelSelect()
        {
            if (titleScreen != null) titleScreen.SetActive(false);
            if (levelSelectScreen != null) levelSelectScreen.SetActive(true);

            // Rebuilt on every entry rather than cached, so returning from a purchase immediately
            // reflects newly unlocked Pro levels without a restart. Paging deliberately resets to
            // the first page: coming back from a match to page 3 with no memory of why is more
            // disorienting than a page turn is expensive.
            pageIndex = 0;
            BuildLevelList();
        }

        /// <summary>Wire to the level list's next-page button.</summary>
        public void NextPage() => GoToPage(pageIndex + 1);

        /// <summary>Wire to the level list's previous-page button.</summary>
        public void PreviousPage() => GoToPage(pageIndex - 1);

        private void GoToPage(int index)
        {
            int pages = PageCount();
            int clamped = Mathf.Clamp(index, 0, Mathf.Max(0, pages - 1));
            if (clamped == pageIndex) return;

            pageIndex = clamped;

            // No click sound here. UIButtonMotion.OnPointerDown already plays Sfx.UiTap for every
            // button in the game, so playing it again from the handler fired two identical taps a
            // few milliseconds apart - which reads as a stutter/bug, not as feedback. The rule for
            // this project: button *press* audio belongs to UIButtonMotion and nowhere else; a
            // handler may only add a sound that is different from the tap.
            BuildLevelList();
        }

        private int PerPage() => Mathf.Max(1, levelsPerPage);

        private int PageCount()
        {
            int count = orderedLevels.Count;
            if (count == 0) return 1;

            return Mathf.CeilToInt(count / (float)PerPage());
        }

        private void BuildLevelList()
        {
            foreach (var card in spawnedCards)
                if (card != null) Destroy(card);
            spawnedCards.Clear();

            if (catalog == null || levelCardTemplate == null || levelListParent == null) return;

            // Re-read the catalog each build rather than caching at Awake, so a level added to the
            // asset while the menu is open still appears - and so the page count can never be based
            // on a list that has since changed length.
            orderedLevels.Clear();
            foreach (var level in catalog.Levels)
                if (level != null) orderedLevels.Add(level);

            // A catalog that shrinks (or a levelsPerPage edited upward) can leave pageIndex past the
            // end, which would render an empty page with no obvious way back.
            pageIndex = Mathf.Clamp(pageIndex, 0, Mathf.Max(0, PageCount() - 1));

            int first = pageIndex * PerPage();
            int last = Mathf.Min(first + PerPage(), orderedLevels.Count);

            for (int i = first; i < last; i++)
            {
                LevelDefinition level = orderedLevels[i];

                var card = Instantiate(levelCardTemplate, levelListParent);
                card.name = $"Card_{level.levelNumber}_{level.displayName}";
                card.SetActive(true);
                spawnedCards.Add(card);

                bool unlocked = LevelCatalog.IsUnlocked(level);
                PopulateCard(card, level, unlocked);
            }

            ApplyPagingState();
        }

        private void ApplyPagingState()
        {
            int pages = PageCount();

            if (pageLabel != null)
                pageLabel.text = pages <= 1 ? string.Empty : $"PAGE {pageIndex + 1} / {pages}";

            // Left interactable-but-disabled rather than hidden. A control that appears and vanishes
            // as the player pages reflows the row it sits in, which on a fixed-height bar moves
            // everything else under the player's thumb between taps.
            if (previousPageButton != null)
            {
                previousPageButton.gameObject.SetActive(pages > 1);
                previousPageButton.interactable = pageIndex > 0;
            }

            if (nextPageButton != null)
            {
                nextPageButton.gameObject.SetActive(pages > 1);
                nextPageButton.interactable = pageIndex < pages - 1;
            }
        }

        private void PopulateCard(GameObject card, LevelDefinition level, bool unlocked)
        {
            var texts = card.GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                switch (text.gameObject.name)
                {
                    case "Number":
                        text.text = level.levelNumber.ToString("00");
                        break;
                    case "Title":
                        // Best rating rides on the title rather than getting a row of its own, so
                        // progress shows up with no change to the card prefab - a scene edit is the
                        // step most likely to half-ship here. Only on a level that has actually been
                        // beaten: three hollow stars on every unplayed card reads as a chore list.
                        int best = unlocked ? ScrapSiege.Levels.LevelProgress.BestStars(level) : 0;
                        text.text = best > 0
                            ? $"{level.displayName}   {ScrapSiege.Levels.LevelProgress.StarGlyphs(best)}"
                            : level.displayName;
                        text.color = unlocked ? UITheme.TextPrimary : UITheme.TextMuted;
                        break;
                    case "Briefing":
                        text.text = unlocked ? level.briefing : "Unlock with Scrap Siege Pro.";
                        text.color = UITheme.TextMuted;
                        break;
                }
            }

            var fill = card.GetComponent<Image>();
            if (fill != null) fill.color = unlocked ? UITheme.SurfaceRaised : UITheme.Surface;

            var button = card.GetComponent<Button>();
            if (button == null) return;

            button.onClick.RemoveAllListeners();

            // Captured locally so each card's listener refers to its own level rather than the
            // loop variable's final value.
            LevelDefinition captured = level;
            if (unlocked)
                button.onClick.AddListener(() => PlayLevel(captured));
            else
                button.onClick.AddListener(ShowPaywall);
        }

        private void PlayLevel(LevelDefinition level)
        {
            LevelCatalog.Select(level);

            if (string.IsNullOrEmpty(matchSceneName))
            {
                Debug.LogError("MainMenuController: Match Scene Name is empty - cannot start a match.", this);
                return;
            }

            SceneManager.LoadScene(matchSceneName);
        }

        /// <summary>Wire to the Go Pro button, and used by locked level cards.</summary>
        public void ShowPaywall()
        {
            if (paywallPanel != null) paywallPanel.SetActive(true);
            else Debug.Log("MainMenuController: no paywall panel assigned in this scene.");
        }

        public void HidePaywall()
        {
            if (paywallPanel != null) paywallPanel.SetActive(false);
        }

        /// <summary>Wire to a Quit button. No-op in the Editor, which cannot quit a play session.</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
