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

        [Header("Paywall")]
        [SerializeField] private GameObject paywallPanel;

        private readonly List<GameObject> spawnedCards = new List<GameObject>();

        private void Awake()
        {
            if (catalog == null) Debug.LogError("MainMenuController: Catalog is not assigned - the level list will be empty.", this);
            if (levelListParent == null) Debug.LogError("MainMenuController: Level List Parent is not assigned - level cards have nowhere to go.", this);
            if (levelCardTemplate == null) Debug.LogError("MainMenuController: Level Card Template is not assigned - no cards can be built.", this);
            else levelCardTemplate.SetActive(false);
        }

        private void Start()
        {
            ShowTitle();
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
            // reflects newly unlocked Pro levels without a restart.
            BuildLevelList();
        }

        private void BuildLevelList()
        {
            foreach (var card in spawnedCards)
                if (card != null) Destroy(card);
            spawnedCards.Clear();

            if (catalog == null || levelCardTemplate == null || levelListParent == null) return;

            foreach (var level in catalog.Levels)
            {
                var card = Instantiate(levelCardTemplate, levelListParent);
                card.name = $"Card_{level.levelNumber}_{level.displayName}";
                card.SetActive(true);
                spawnedCards.Add(card);

                bool unlocked = LevelCatalog.IsUnlocked(level);
                PopulateCard(card, level, unlocked);
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
                        text.text = level.displayName;
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
