using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ScrapSiege.AR;
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

        [Header("Top bar")]
        [SerializeField] private TMP_Text phaseLabel;
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private CanvasGroup resourceChip;

        [Header("Phase panels (bottom bar)")]
        [SerializeField] private CanvasGroup scanPanel;
        [SerializeField] private CanvasGroup fortifyPanel;
        [SerializeField] private CanvasGroup siegePanel;

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
        [SerializeField] private GameObject winPanel;

        [Header("Motion")]
        [SerializeField] private float panelFadeSpeed = 9f;

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
            if (outcome != null) outcome.OnPlayerWon.AddListener(HandlePlayerWon);

            if (vantage != null)
            {
                vantage.OnVantageChanged.AddListener(HandleVantageChanged);
                vantage.OnRallyAvailabilityChanged.AddListener(HandleRallyAvailabilityChanged);
            }

            if (rally != null)
            {
                rally.OnArmedChanged.AddListener(HandleRallyArmedChanged);
                rally.OnRallyIssued.AddListener(HandleRallyIssued);
            }
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
            if (outcome != null) outcome.OnPlayerWon.RemoveListener(HandlePlayerWon);

            if (vantage != null)
            {
                vantage.OnVantageChanged.RemoveListener(HandleVantageChanged);
                vantage.OnRallyAvailabilityChanged.RemoveListener(HandleRallyAvailabilityChanged);
            }

            if (rally != null)
            {
                rally.OnArmedChanged.RemoveListener(HandleRallyArmedChanged);
                rally.OnRallyIssued.RemoveListener(HandleRallyIssued);
            }
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

            SnapPanelAlphas();
        }

        private void Update()
        {
            Fade(scanPanel, phase == Phase.Scan);
            Fade(fortifyPanel, phase == Phase.Fortify);
            Fade(siegePanel, phase == Phase.Siege);
            Fade(resourceChip, phase == Phase.Siege);
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

        // --- Siege phase ------------------------------------------------------------------

        private void HandleSiegeStarted()
        {
            SetPhase(Phase.Siege);
            SetPrompt("Tap the table to deploy. Break the enemy base.");
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
                    : "Tap the table to deploy. Break the enemy base.");
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

        // --- Outcome ----------------------------------------------------------------------

        private void HandlePlayerWon()
        {
            if (winPanel != null) winPanel.SetActive(true);
            SetPrompt("Base destroyed.");
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
                case Phase.Siege:
                    phaseLabel.text = "STEP 3 · SIEGE";
                    phaseLabel.color = UITheme.Danger;
                    break;
            }
        }

        private void SetPrompt(string text)
        {
            if (promptLabel != null) promptLabel.text = text;
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
            SnapPanel(siegePanel, phase == Phase.Siege);
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
