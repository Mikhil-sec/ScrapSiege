using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ScrapSiege.AR;
using ScrapSiege.Monetization;
using ScrapSiege.Siege;
using ScrapSiege.Terrain;
using ScrapSiege.Vantage;

namespace ScrapSiege.UI
{
    /// <summary>
    /// The one place that decides what the HUD shows. Each gameplay system already raises
    /// UnityEvents for its own state changes; this subscribes to all of them in code and drives
    /// the panels, prompts and button states from a single switch.
    ///
    /// Subscribing in code rather than wiring through the Inspector is deliberate: every
    /// UnityEvent&lt;T&gt; here (object count, lock-ready, mapped area, delete mode) passes a real
    /// value through, and the Inspector's function dropdown quietly offers a static-parameter
    /// version of the same method that bakes in a constant instead. That trap already cost this
    /// project a resource counter permanently stuck on 0. AddListener in code can't hit it.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        private enum Phase
        {
            Scan,
            Fortify,
            Placement,
            Siege
        }

        [Header("Systems")]
        [SerializeField] private PlaneLockController planeLock;
        [SerializeField] private FortifyInputController fortify;
        [SerializeField] private SiegePhaseController siege;
        [SerializeField] private SiegeOutcomeController outcome;
        [SerializeField] private UnitDeploymentController deployment;
        [SerializeField] private VantageController vantage;
        [SerializeField] private RallyController rally;
        [SerializeField] private ScrapSiege.Levels.BoardPlacementController placement;
        [SerializeField] private ScrapSiege.Levels.LevelMatchController levelMatch;

        [Header("Top bar")]
        [SerializeField] private TMP_Text phaseLabel;
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private CanvasGroup resourceChip;

        [Header("Phase panels (bottom bar)")]
        [SerializeField] private CanvasGroup scanPanel;
        [SerializeField] private CanvasGroup fortifyPanel;
        [SerializeField] private CanvasGroup placementPanel;
        [SerializeField] private CanvasGroup siegePanel;

        [Tooltip("The unit roster strip, which sits in its own row ABOVE the bottom bar rather than " +
                 "inside it. Five class chips plus a rally-scope toggle do not fit alongside the " +
                 "vantage meter, route segments and rally button on a 1920-wide canvas - authoring " +
                 "them into the same HorizontalLayoutGroup looked fine at the scene's stored 3313px " +
                 "width and would have been squashed unusable on the actual device. Faded with the " +
                 "Siege phase like every other panel.")]
        [SerializeField] private CanvasGroup rosterPanel;

        [Header("Placement phase")]
        [SerializeField] private Button confirmBoardButton;
        [SerializeField] private TMP_Text levelNameLabel;

        [Header("Scan phase")]
        [SerializeField] private Button lockPlaneButton;
        [SerializeField] private TMP_Text scanReadout;

        [Header("Fortify phase")]
        [SerializeField] private TMP_Text objectCountLabel;
        [SerializeField] private Button undoButton;
        [SerializeField] private Image deleteButtonFill;
        [SerializeField] private TMP_Text deleteButtonLabel;
        [SerializeField] private Button doneButton;

        [Header("Siege phase")]
        [SerializeField] private Image directFill;
        [SerializeField] private Image coveredFill;

        [Header("Vantage (Mechanic 1)")]
        [Tooltip("Filled bar, 0 = leaned in / precise, 1 = pulled back / overview.")]
        [SerializeField] private Image vantageFill;
        [SerializeField] private TMP_Text vantageLabel;

        [Header("Rally (high-vantage order)")]
        [SerializeField] private Button rallyButton;
        [SerializeField] private Image rallyFill;
        [SerializeField] private TMP_Text rallyLabel;

        [Header("Outcome")]
        [Tooltip("Shown for BOTH outcomes - the heading below is what distinguishes them. One panel " +
                 "rather than two because they differ by a single line of text and a colour, and a " +
                 "second near-identical panel is a second thing to keep in sync.")]
        [SerializeField] private GameObject winPanel;

        [Tooltip("Heading inside the outcome panel, retitled per result. Optional: without it the " +
                 "panel still appears, it just reads the same either way.")]
        [SerializeField] private TMP_Text outcomeTitle;

        [Tooltip("Sub-line inside the outcome panel. Without it a defeat still reads 'The enemy base " +
                 "is scrap', which is the exact opposite of what happened.")]
        [SerializeField] private TMP_Text outcomeBody;

        [Header("Rally scope (selective orders)")]
        [Tooltip("Small button that widens the rally back to the whole army. Optional.")]
        [SerializeField] private Button rallyScopeButton;

        [Tooltip("Reads 'RALLY · ALL' or 'RALLY · SNP'. Optional.")]
        [SerializeField] private TMP_Text rallyScopeLabel;

        [Header("Line of sight (Mechanic 2)")]
        [Tooltip("Shows how many enemy units are currently out of sight, which is the whole nudge " +
                 "to physically move. Optional.")]
        [SerializeField] private TMP_Text unseenContactsLabel;

        [SerializeField] private ScrapSiege.Vision.LineOfSightController lineOfSight;

        [Header("Navigation")]
        [Tooltip("Scene loaded by the Menu / Main Menu buttons. Must be in Build Settings.")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Tooltip("Confirmation shown before abandoning a match in progress. Optional - without it " +
                 "the Menu button leaves immediately.")]
        [SerializeField] private GameObject quitConfirmPanel;

        [Header("Motion")]
        [SerializeField] private float panelFadeSpeed = 9f;

        [Header("Monetization")]
        [Tooltip("The in-match Go Pro button, hidden once Pro is active - offering an upgrade the " +
                 "player already owns reads as a bug. Same pattern as MainMenuController.ApplyProState.")]
        [SerializeField] private GameObject goProButton;

        private Phase phase = Phase.Scan;
        private bool deleteMode;

        private void Awake()
        {
            if (planeLock == null) Debug.LogError("HudController: Plane Lock is not assigned - the scan phase UI will never update.", this);
            if (fortify == null) Debug.LogError("HudController: Fortify is not assigned - fortify prompts and the object count will never update.", this);
            if (siege == null) Debug.LogError("HudController: Siege is not assigned - the HUD will never switch to the Siege phase.", this);

            if (winPanel != null) winPanel.SetActive(false);
        }

        private void OnEnable()
        {
            if (planeLock != null)
            {
                planeLock.OnScanStarted.AddListener(HandleScanStarted);
                planeLock.OnPlaneLocked.AddListener(HandlePlaneLocked);
                planeLock.OnLockReadyChanged.AddListener(HandleLockReadyChanged);
                planeLock.OnMappedAreaChanged.AddListener(HandleMappedAreaChanged);
            }

            if (fortify != null)
            {
                fortify.OnAwaitingFirstCorner.AddListener(HandleAwaitingFirstCorner);
                fortify.OnAwaitingSecondCorner.AddListener(HandleAwaitingSecondCorner);
                fortify.OnAwaitingHeightPick.AddListener(HandleAwaitingHeightPick);
                fortify.OnObjectCount.AddListener(HandleObjectCount);
                fortify.OnDeleteModeChanged.AddListener(HandleDeleteModeChanged);
            }

            if (siege != null) siege.OnSiegeStarted.AddListener(HandleSiegeStarted);
            if (outcome != null)
            {
                outcome.OnPlayerWon.AddListener(HandlePlayerWon);
                outcome.OnPlayerLost.AddListener(HandlePlayerLost);
            }

            if (vantage != null)
            {
                vantage.OnVantageChanged.AddListener(HandleVantageChanged);
                vantage.OnRallyAvailabilityChanged.AddListener(HandleRallyAvailabilityChanged);
            }

            if (rally != null)
            {
                rally.OnArmedChanged.AddListener(HandleRallyArmedChanged);
                rally.OnRallyIssued.AddListener(HandleRallyIssued);
                rally.OnScopeChanged.AddListener(HandleRallyScopeChanged);
            }

            if (deployment != null)
            {
                deployment.OnDeployRejected.AddListener(HandleDeployRejected);
                deployment.OnSelectedClassChanged.AddListener(HandleSelectedClassChanged);
            }

            if (placement != null)
            {
                placement.OnPlacementStarted.AddListener(HandlePlacementStarted);
                placement.OnBoardPlacedChanged.AddListener(HandleBoardPlacedChanged);
            }

            if (levelMatch != null)
            {
                levelMatch.OnLevelLoaded.AddListener(HandleLevelLoaded);
                levelMatch.OnSiegeStarted.AddListener(HandleSiegeStarted);
            }

            ProEntitlement.Changed += OnProEntitlementChanged;
        }

        private void OnDisable()
        {
            if (planeLock != null)
            {
                planeLock.OnScanStarted.RemoveListener(HandleScanStarted);
                planeLock.OnPlaneLocked.RemoveListener(HandlePlaneLocked);
                planeLock.OnLockReadyChanged.RemoveListener(HandleLockReadyChanged);
                planeLock.OnMappedAreaChanged.RemoveListener(HandleMappedAreaChanged);
            }

            if (fortify != null)
            {
                fortify.OnAwaitingFirstCorner.RemoveListener(HandleAwaitingFirstCorner);
                fortify.OnAwaitingSecondCorner.RemoveListener(HandleAwaitingSecondCorner);
                fortify.OnAwaitingHeightPick.RemoveListener(HandleAwaitingHeightPick);
                fortify.OnObjectCount.RemoveListener(HandleObjectCount);
                fortify.OnDeleteModeChanged.RemoveListener(HandleDeleteModeChanged);
            }

            if (siege != null) siege.OnSiegeStarted.RemoveListener(HandleSiegeStarted);
            if (outcome != null)
            {
                outcome.OnPlayerWon.RemoveListener(HandlePlayerWon);
                outcome.OnPlayerLost.RemoveListener(HandlePlayerLost);
            }

            if (vantage != null)
            {
                vantage.OnVantageChanged.RemoveListener(HandleVantageChanged);
                vantage.OnRallyAvailabilityChanged.RemoveListener(HandleRallyAvailabilityChanged);
            }

            if (rally != null)
            {
                rally.OnArmedChanged.RemoveListener(HandleRallyArmedChanged);
                rally.OnRallyIssued.RemoveListener(HandleRallyIssued);
                rally.OnScopeChanged.RemoveListener(HandleRallyScopeChanged);
            }

            if (deployment != null)
            {
                deployment.OnDeployRejected.RemoveListener(HandleDeployRejected);
                deployment.OnSelectedClassChanged.RemoveListener(HandleSelectedClassChanged);
            }

            if (placement != null)
            {
                placement.OnPlacementStarted.RemoveListener(HandlePlacementStarted);
                placement.OnBoardPlacedChanged.RemoveListener(HandleBoardPlacedChanged);
            }

            if (levelMatch != null)
            {
                levelMatch.OnLevelLoaded.RemoveListener(HandleLevelLoaded);
                levelMatch.OnSiegeStarted.RemoveListener(HandleSiegeStarted);
            }

            ProEntitlement.Changed -= OnProEntitlementChanged;
        }

        private void Start()
        {
            // PlaneLockController raises OnScanStarted and OnLockReadyChanged(false) from its own
            // OnEnable, which can run before this component subscribes - and it won't re-raise
            // them, because from its point of view nothing changed. Driving the same handlers
            // here makes the opening state independent of script execution order; without this,
            // Lock would sit tappable with no plane found yet.
            HandleScanStarted();
            SetSegmentSelected(direct: true);
            HandleObjectCount(0);

            // Same reason as HandleScanStarted above: these controllers only raise their events on
            // *change*, so a HUD that subscribes late would show a stale default until the player
            // happened to move. Drive the opening state explicitly instead.
            HandleVantageChanged(vantage != null ? vantage.Vantage01 : 0f);
            HandleRallyAvailabilityChanged(vantage != null && vantage.IsRallyReady);

            HandleRallyScopeChanged(rally != null ? rally.ScopeLabel : "ALL");
            if (quitConfirmPanel != null) quitConfirmPanel.SetActive(false);

            ApplyProState();

            SnapPanelAlphas();
        }

        private void Update()
        {
            Fade(scanPanel, phase == Phase.Scan);
            Fade(fortifyPanel, phase == Phase.Fortify);
            Fade(placementPanel, phase == Phase.Placement);
            Fade(siegePanel, phase == Phase.Siege);
            Fade(rosterPanel, phase == Phase.Siege);
            Fade(resourceChip, phase == Phase.Siege);

            TickTransientPrompt();
            TickUnseenContacts();
        }

        // --- Line of sight readout ----------------------------------------------------------

        /// <summary>
        /// The one piece of UI that exists purely to make the AR mechanic legible.
        ///
        /// <para>A hidden enemy is, by definition, something the player cannot see - so with no
        /// readout there is nothing on screen to distinguish "the board is clear" from "there are
        /// three units behind that wall". The count does not say where they are (the drifting
        /// ghosts do that, badly and on purpose); it only says that moving is worth it. That is
        /// deliberately the minimum information needed to motivate the physical action, and no
        /// more - a minimap would answer the question the leaning is supposed to answer.</para>
        /// </summary>
        private void TickUnseenContacts()
        {
            if (unseenContactsLabel == null) return;

            if (phase != Phase.Siege || lineOfSight == null)
            {
                unseenContactsLabel.text = string.Empty;
                return;
            }

            int hidden = lineOfSight.HiddenTargetCount;
            if (hidden <= 0)
            {
                unseenContactsLabel.text = "ALL CONTACTS VISIBLE";
                unseenContactsLabel.color = UITheme.TextMuted;
                return;
            }

            unseenContactsLabel.text = hidden == 1
                ? "1 CONTACT UNSEEN · MOVE TO LOOK"
                : $"{hidden} CONTACTS UNSEEN · MOVE TO LOOK";
            unseenContactsLabel.color = UITheme.Accent;
        }

        // --- Scan phase -------------------------------------------------------------------

        private void HandleScanStarted()
        {
            SetPhase(Phase.Scan);
            SetPrompt("Sweep your phone slowly across the table.");
            HandleMappedAreaChanged(0f);
            HandleLockReadyChanged(false);
        }

        private void HandleLockReadyChanged(bool ready)
        {
            if (lockPlaneButton != null) lockPlaneButton.interactable = ready;
        }

        private void HandleMappedAreaChanged(float area)
        {
            if (scanReadout == null) return;

            scanReadout.text = area > 0f
                ? $"Surface found  ·  {area:0.00} m²"
                : "Looking for a flat surface…";
            scanReadout.color = area > 0f ? UITheme.Success : UITheme.TextMuted;
        }

        // --- Fortify phase ----------------------------------------------------------------

        private void HandlePlaneLocked()
        {
            // With the authored-level flow active, BoardPlacementController.OnEnable has ALREADY
            // moved the HUD to the Placement phase - PlaneLockController enables placement and only
            // then raises OnPlaneLocked. Setting Fortify here would clobber that, showing the player
            // a dead "tap the corners of an object" panel while FortifyInputController is (correctly)
            // disabled and eating none of their taps. Only the legacy scan flow should land here.
            if (placement != null) return;

            SetPhase(Phase.Fortify);
            SetPrompt("Tap one corner of a real object.");
        }

        private void HandleAwaitingFirstCorner()
        {
            if (phase != Phase.Fortify) return;
            SetPrompt(deleteMode ? "Tap a piece to remove it." : "Tap one corner of a real object.");
        }

        private void HandleAwaitingSecondCorner()
        {
            if (phase != Phase.Fortify) return;
            SetPrompt("Now tap the opposite corner.");
        }

        private void HandleAwaitingHeightPick()
        {
            if (phase != Phase.Fortify) return;
            SetPrompt("How tall is it?");
        }

        private void HandleObjectCount(int count)
        {
            if (objectCountLabel != null)
                objectCountLabel.text = count == 1 ? "1 piece placed" : $"{count} pieces placed";

            // Nothing to undo, and an empty board makes for a pointless siege.
            if (undoButton != null) undoButton.interactable = count > 0;
            if (doneButton != null) doneButton.interactable = count > 0;
        }

        private void HandleDeleteModeChanged(bool enabled)
        {
            deleteMode = enabled;

            if (deleteButtonFill != null)
                deleteButtonFill.color = enabled ? UITheme.Danger : UITheme.SurfaceRaised;
            if (deleteButtonLabel != null)
                deleteButtonLabel.color = enabled ? UITheme.TextPrimary : UITheme.TextMuted;

            if (phase == Phase.Fortify)
                SetPrompt(enabled ? "Tap a piece to remove it." : "Tap one corner of a real object.");
        }

        /// <summary>Wire to the Delete toggle button so the HUD owns the on/off visual state.</summary>
        public void ToggleDeleteMode()
        {
            if (fortify != null) fortify.ToggleDeleteMode();
        }

        // --- Placement phase --------------------------------------------------------------

        private void HandlePlacementStarted()
        {
            SetPhase(Phase.Placement);
            SetPrompt("Tap the table to drop the board.");
            HandleBoardPlacedChanged(false);
        }

        /// <summary>
        /// Confirm stays disabled until a board actually exists, so the player can't skip straight
        /// past placement into a siege with no battlefield.
        /// </summary>
        private void HandleBoardPlacedChanged(bool placed)
        {
            if (confirmBoardButton != null) confirmBoardButton.interactable = placed;

            if (phase == Phase.Placement)
                SetPrompt(placed
                    ? "Drag to move · pinch to resize · twist to turn."
                    : "Tap the table to drop the board.");
        }

        private void HandleLevelLoaded(string levelName)
        {
            if (levelNameLabel != null) levelNameLabel.text = levelName;
        }

        /// <summary>Wire to the Confirm button on the placement panel.</summary>
        public void ConfirmBoard()
        {
            if (placement != null) placement.ConfirmPlacement();
        }

        // --- Siege phase ------------------------------------------------------------------

        /// <summary>
        /// The standing Siege instruction. It used to read "Tap the table to deploy", which stopped
        /// being true when deployment was restricted to the player's own lines - and a prompt that
        /// contradicts the rule is worse than no prompt, because it turns a refused tap into
        /// evidence that the game is broken. Named rather than repeated, since it is shown from two
        /// places and the two had to be kept in step by hand.
        /// </summary>
        private const string SiegePrompt = "Deploy inside the blue zone. Break the enemy base.";

        private void HandleSiegeStarted()
        {
            SetPhase(Phase.Siege);
            SetPrompt(SiegePrompt);
        }

        /// <summary>Wire to the "Direct" segment.</summary>
        public void SelectDirect()
        {
            if (deployment != null) deployment.SelectDirectMode();
            SetSegmentSelected(direct: true);
        }

        /// <summary>Wire to the "Covered" segment.</summary>
        public void SelectCovered()
        {
            if (deployment != null) deployment.SelectCoveredMode();
            SetSegmentSelected(direct: false);
        }

        private void SetSegmentSelected(bool direct)
        {
            if (directFill != null) directFill.color = direct ? UITheme.Steel : UITheme.SurfaceRaised;
            if (coveredFill != null) coveredFill.color = direct ? UITheme.SurfaceRaised : UITheme.Steel;
        }

        // --- Vantage & Rally ----------------------------------------------------------------

        /// <summary>
        /// The vantage readout is intentionally a posture *description*, not a number. The player
        /// should learn "lean in to place precisely" from their own body, not by watching a value.
        /// </summary>
        private void HandleVantageChanged(float vantage01)
        {
            if (vantageFill != null)
            {
                vantageFill.fillAmount = vantage01;
                vantageFill.color = Color.Lerp(UITheme.Success, UITheme.Accent, vantage01);
            }

            if (vantageLabel == null) return;

            if (vantage01 < 0.3f) vantageLabel.text = "LEANED IN · precise";
            else if (vantage01 < 0.6f) vantageLabel.text = "MID · steady";
            else vantageLabel.text = "PULLED BACK · overview";
        }

        private void HandleRallyAvailabilityChanged(bool available)
        {
            if (rallyButton != null) rallyButton.interactable = available;
            if (rallyFill != null) rallyFill.color = available ? UITheme.Steel : UITheme.SurfaceRaised;
            if (rallyLabel != null) rallyLabel.color = available ? UITheme.TextPrimary : UITheme.TextMuted;

            if (phase == Phase.Siege && available)
                SetPrompt("Rally ready — tap Rally, then a lane.");
        }

        private void HandleRallyArmedChanged(bool armed)
        {
            if (rallyFill != null && rallyButton != null && rallyButton.interactable)
                rallyFill.color = armed ? UITheme.Accent : UITheme.Steel;

            if (phase == Phase.Siege)
                SetPrompt(armed
                    ? "Tap a lane to redirect every unit."
                    : SiegePrompt);
        }

        private void HandleRallyIssued(int unitsRedirected)
        {
            if (phase != Phase.Siege) return;

            SetPrompt(unitsRedirected == 1
                ? "1 unit redirected."
                : $"{unitsRedirected} units redirected.");
        }

        /// <summary>Wire to the Rally button.</summary>
        public void ToggleRally()
        {
            if (rally != null) rally.ToggleArmed();
        }

        /// <summary>
        /// Wire to the rally scope button. Cycles between commanding the whole army and commanding
        /// only the class currently selected for deployment.
        ///
        /// <para>Deliberately a two-state toggle rather than a picker that walks the whole roster.
        /// The player already chose a class on the deploy bar a second ago, so the scoped state can
        /// simply mean "that one" - and one extra tap is the entire cost of the feature. A separate
        /// class picker would be a second thing to keep in sync with the first for no added
        /// expressiveness.</para>
        /// </summary>
        public void ToggleRallyScope()
        {
            if (rally == null) return;

            bool wasScoped = rally.Scope != null;
            rally.SetScope(wasScoped ? null : (deployment != null ? deployment.SelectedClass : null));

            // SetScope early-returns when nothing changed (e.g. scoping to a null selection), so
            // the label is refreshed here rather than relying only on the event.
            HandleRallyScopeChanged(rally.ScopeLabel);
        }

        private void HandleRallyScopeChanged(string label)
        {
            if (rallyScopeLabel != null)
            {
                rallyScopeLabel.text = $"RALLY · {label}";
                rallyScopeLabel.color = rally != null && rally.Scope != null
                    ? UITheme.TextPrimary
                    : UITheme.TextMuted;
            }

            bool scoped = rally != null && rally.Scope != null;
            if (rallyScopeButton != null)
            {
                // Forced on every refresh, not just once. The scene shipped with this button
                // authored non-interactable, which silently disabled the whole selective-rally
                // feature: the label read "RALLY · ALL" forever because the only control that can
                // change the scope could never be tapped. Nothing in the design ever wants this
                // button disabled - widening an order back to the whole army is always legal - so
                // the correct state is asserted here rather than trusted to the scene.
                rallyScopeButton.interactable = true;

                if (rallyScopeButton.targetGraphic is Image graphic)
                    graphic.color = scoped ? UITheme.Accent : UITheme.SurfaceRaised;
            }
        }

        private void HandleSelectedClassChanged(ScrapSiege.Siege.UnitClass unitClass)
        {
            // A scoped rally follows the deploy selection, so the label has to follow it too.
            if (rally != null && rally.Scope != null) HandleRallyScopeChanged(rally.ScopeLabel);
        }

        // --- Transient prompts ---------------------------------------------------------------

        private string standingPrompt = string.Empty;
        private float transientPromptRemaining;

        /// <summary>
        /// A refused deploy has to say why, and line-of-sight refusals will be the most common
        /// thing a new player hits - but the message must not stick, or the prompt line stops
        /// describing what the player should be doing. Shown for a couple of seconds, then the
        /// standing prompt returns.
        /// </summary>
        private void HandleDeployRejected(string reason)
        {
            if (promptLabel == null) return;

            transientPromptRemaining = 2f;
            promptLabel.text = reason;
            promptLabel.color = UITheme.Danger;
        }

        private void TickTransientPrompt()
        {
            if (transientPromptRemaining <= 0f) return;

            transientPromptRemaining -= Time.unscaledDeltaTime;
            if (transientPromptRemaining > 0f) return;

            if (promptLabel != null)
            {
                promptLabel.text = standingPrompt;
                promptLabel.color = UITheme.TextPrimary;
            }
        }

        // --- Navigation -----------------------------------------------------------------------

        /// <summary>
        /// Wire to the in-match Menu button and to the outcome card's "Main Menu" button.
        ///
        /// <para>Without this there was genuinely no way out of a level: finishing one left the
        /// player on the outcome card with only "Play Again", and abandoning one needed the OS back
        /// gesture. Loading the menu scene (rather than trying to tear the match down in place) is
        /// the same reasoning <see cref="RestartMatch"/> already uses - match state is spread across
        /// the plane lock, the baked NavMesh, spawned terrain, the garrison and both armies, and a
        /// second hand-written teardown path would be a second thing to keep correct.</para>
        /// </summary>
        public void ReturnToMainMenu()
        {
            if (string.IsNullOrEmpty(mainMenuSceneName))
            {
                Debug.LogError("HudController: Main Menu Scene Name is empty - the Menu button cannot go anywhere.", this);
                return;
            }

            // Deliberately no GameAudio.Play here - see MainMenuController.GoToPage. UIButtonMotion
            // already sounds every button press, so a second UiTap from the handler played as a
            // double click.
            UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
        }

        /// <summary>
        /// Wire to the in-match Menu button when a confirmation is wanted. Falls straight through to
        /// <see cref="ReturnToMainMenu"/> if no confirm panel is assigned, so the button always works.
        /// </summary>
        public void RequestReturnToMainMenu()
        {
            // Nothing to lose once the match is decided - the outcome card is already showing, so a
            // "are you sure you want to quit?" over the top of "VICTORY" is pure friction.
            bool matchOver = winPanel != null && winPanel.activeSelf;

            if (quitConfirmPanel == null || matchOver)
            {
                ReturnToMainMenu();
                return;
            }

            quitConfirmPanel.SetActive(true);
        }

        /// <summary>Wire to the confirmation panel's Cancel button.</summary>
        public void CancelReturnToMainMenu()
        {
            if (quitConfirmPanel != null) quitConfirmPanel.SetActive(false);
        }

        // --- Outcome ----------------------------------------------------------------------

        private void HandlePlayerWon()
        {
            ShowOutcome("VICTORY", UITheme.TextPrimary, "The enemy base is scrap.");
            SetPrompt("Base destroyed.");
        }

        /// <summary>
        /// Only reachable on levels with an AI commander - nothing else in the game can damage the
        /// player's base, which is why this had no counterpart until now.
        /// </summary>
        private void HandlePlayerLost()
        {
            ShowOutcome("DEFEAT", new Color(0.90f, 0.35f, 0.30f), "Your base has been overrun.");
            SetPrompt("Your base has fallen.");
        }

        private void ShowOutcome(string title, Color titleColor, string body)
        {
            if (winPanel != null) winPanel.SetActive(true);

            if (outcomeTitle != null)
            {
                outcomeTitle.text = title;
                outcomeTitle.color = titleColor;
            }

            if (outcomeBody != null) outcomeBody.text = body;
        }

        /// <summary>
        /// Wire to the win card's "Play Again" button. A full scene reload is the honest reset
        /// here - phase state lives across the plane lock, the baked NavMesh, spawned terrain,
        /// garrison and units, and re-running all of that by hand would be a second, parallel
        /// teardown path to keep correct. Needs an on-device check that AR tracking re-acquires
        /// cleanly after the reload.
        /// </summary>
        public void RestartMatch()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.SceneManager.LoadScene(active.buildIndex);
        }

        // --- Monetization -------------------------------------------------------------------

        /// <summary>
        /// Purchase can complete mid-match, and the player is looking at the HUD, not the main
        /// menu, when it does - same reasoning as MainMenuController.OnProEntitlementChanged.
        /// </summary>
        private void OnProEntitlementChanged(bool unlocked)
        {
            ApplyProState();
        }

        private void ApplyProState()
        {
            if (goProButton != null) goProButton.SetActive(!ProEntitlement.IsUnlocked);
        }

        // --- Shared -----------------------------------------------------------------------

        private void SetPhase(Phase next)
        {
            phase = next;
            if (phaseLabel == null) return;

            switch (phase)
            {
                case Phase.Scan:
                    phaseLabel.text = "STEP 1 · FIND THE TABLE";
                    phaseLabel.color = UITheme.Steel;
                    break;
                case Phase.Fortify:
                    phaseLabel.text = "STEP 2 · FORTIFY";
                    phaseLabel.color = UITheme.Accent;
                    break;
                case Phase.Placement:
                    phaseLabel.text = "STEP 2 · PLACE THE BOARD";
                    phaseLabel.color = UITheme.Accent;
                    break;
                case Phase.Siege:
                    phaseLabel.text = "STEP 3 · SIEGE";
                    phaseLabel.color = UITheme.Danger;
                    break;
            }
        }

        private void SetPrompt(string text)
        {
            standingPrompt = text;

            // A transient rejection message owns the prompt line until it expires. Without this a
            // vantage tick or a phase change would wipe "No line of sight" off the screen before
            // the player had a chance to read the one thing explaining why their tap did nothing.
            if (transientPromptRemaining > 0f) return;

            if (promptLabel == null) return;
            promptLabel.text = text;
            promptLabel.color = UITheme.TextPrimary;
        }

        /// <summary>
        /// Panels stay active and fade by alpha rather than toggling SetActive, so a phase swap
        /// reads as a transition instead of a hard pop. Raycast blocking is switched with the
        /// fade so a faded-out panel can never eat a tap meant for the table underneath it.
        /// </summary>
        private void Fade(CanvasGroup group, bool visible)
        {
            if (group == null) return;

            float target = visible ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * panelFadeSpeed);
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }

        private void SnapPanelAlphas()
        {
            SnapPanel(scanPanel, phase == Phase.Scan);
            SnapPanel(fortifyPanel, phase == Phase.Fortify);
            SnapPanel(placementPanel, phase == Phase.Placement);
            SnapPanel(siegePanel, phase == Phase.Siege);
            SnapPanel(rosterPanel, phase == Phase.Siege);
            SnapPanel(resourceChip, phase == Phase.Siege);
        }

        private static void SnapPanel(CanvasGroup group, bool visible)
        {
            if (group == null) return;

            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }
    }
}
