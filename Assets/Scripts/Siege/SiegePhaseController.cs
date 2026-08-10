using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ScrapSiege.Terrain;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Owns the Fortify -> Siege handoff: ends Fortify input, drops a synthetic ground quad
    /// and dummy base a fixed distance in front of the player at real table height (stand-in
    /// for a real opponent base until Week 3 Cloud Anchor sync exists), bakes the NavMesh once
    /// terrain is final, runs Muster (auto-garrison at chokepoints), wires the win-condition
    /// watcher, then turns on the resource/deployment systems.
    /// </summary>
    public class SiegePhaseController : MonoBehaviour
    {
        [SerializeField] private FortifyInputController fortify;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private NavMeshSurface navMeshSurface;
        [SerializeField] private GameObject groundQuadPrefab;
        [SerializeField] private GameObject dummyBasePrefab;
        [SerializeField] private ResourceEconomy resourceEconomy;
        [SerializeField] private UnitDeploymentController deploymentController;
        [SerializeField] private MusterPhaseController musterPhase;
        [SerializeField] private SiegeOutcomeController outcomeController;

        [Header("AR mechanics (plan.md Section 4)")]
        [Tooltip("Publishes the board surface height that the vantage mechanic measures against.")]
        [SerializeField] private ScrapSiege.Core.BoardPlane boardPlane;

        [Tooltip("Optional - the high-vantage Rally order. Enabled alongside deployment when Siege starts.")]
        [SerializeField] private RallyController rallyController;

        [SerializeField] private float dummyBaseDistance = 2f;
        [SerializeField] private float groundQuadSize = 4f;

        /// <summary>Fires once Siege is fully live - used by the HUD to swap phase panels.</summary>
        public UnityEngine.Events.UnityEvent OnSiegeStarted;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        public Transform DummyBase { get; private set; }
        public BaseHealth DummyBaseHealth { get; private set; }

        private void Awake()
        {
            // These are all required for StartSiege() to run - a missing one used to fail
            // silently or deep into a play session (the win-panel bug). Catch it at startup.
            CheckRef(fortify, nameof(fortify));
            CheckRef(arCamera, nameof(arCamera));
            CheckRef(raycastManager, nameof(raycastManager));
            CheckRef(navMeshSurface, nameof(navMeshSurface));
            CheckRef(groundQuadPrefab, nameof(groundQuadPrefab));
            CheckRef(dummyBasePrefab, nameof(dummyBasePrefab));
            CheckRef(resourceEconomy, nameof(resourceEconomy));
            CheckRef(deploymentController, nameof(deploymentController));
            CheckRef(musterPhase, nameof(musterPhase));
            CheckRef(outcomeController, nameof(outcomeController));
        }

        private void CheckRef(Object reference, string fieldName)
        {
            if (reference == null)
                Debug.LogError($"SiegePhaseController: '{fieldName}' is not assigned in the Inspector - StartSiege() will fail or behave incorrectly.", this);
        }

        /// <summary>Wire to the "Done" button - replaces the direct FinishFortify() wire.</summary>
        public void StartSiege()
        {
            ScrapSiege.Audio.GameAudio.Play(ScrapSiege.Audio.Sfx.PhaseChange);

            fortify.FinishFortify();

            Vector3 tableOrigin = FindTableReferencePoint();
            Vector3 flatForward = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up).normalized;
            // Legacy scan/Fortify path. Both distances are authored in REAL metres and land in the
            // scaled AR world, so they convert like everything else.
            Vector3 basePosition = tableOrigin + flatForward * ScrapSiege.Core.WorldScale.Metres(dummyBaseDistance);
            Vector3 groundCenter = (tableOrigin + basePosition) * 0.5f;

            var ground = Instantiate(groundQuadPrefab, groundCenter, Quaternion.Euler(90f, 0f, 0f));
            ground.transform.localScale = Vector3.one * ScrapSiege.Core.WorldScale.Metres(groundQuadSize);

            // Vantage measures the phone's height against this. Published before anything reads it
            // so the first frame of Siege already has a correct posture reading rather than
            // treating the player as fully leaned in.
            if (boardPlane != null)
                boardPlane.SetBoard(groundCenter);
            else
                Debug.LogError("SiegePhaseController: 'boardPlane' is not assigned - the vantage mechanic cannot know the table height and deploy precision will never vary.", this);

            var dummyBase = Instantiate(dummyBasePrefab, basePosition, Quaternion.identity);
            DummyBase = dummyBase.transform;
            DummyBaseHealth = dummyBase.GetComponent<BaseHealth>();

            if (DummyBaseHealth == null)
                Debug.LogError("SiegePhaseController: dummyBasePrefab's root object has no BaseHealth component - the base can never take damage or be destroyed.", dummyBase);

            navMeshSurface.BuildNavMesh();
            NavMeshAreas.ApplyGlobalCost();

            // Sentries face the player's side of the board, so their blind side is the far side -
            // reachable only by physically walking around the table.
            musterPhase.SpawnGarrison(fortify.ScannedObjects, tableOrigin);

            // Guarded rather than unconditional - previously a null DummyBaseHealth would throw
            // inside WatchBase() and abort StartSiege() before the lines below ever ran, silently
            // preventing Siege from starting at all instead of just missing the win condition.
            if (DummyBaseHealth != null)
                outcomeController.WatchBase(DummyBaseHealth);

            resourceEconomy.enabled = true;
            deploymentController.enabled = true;
            if (rallyController != null) rallyController.enabled = true;

            OnSiegeStarted?.Invoke();
        }

        /// <summary>
        /// Finds real table height via the same AR plane raycast Fortify uses for corner taps,
        /// instead of using the camera's own (much higher) position. Samples a few screen points
        /// in case the exact center isn't over tracked plane at the moment Done is tapped.
        /// </summary>
        private Vector3 FindTableReferencePoint()
        {
            Vector2[] sampleScreenPoints =
            {
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                new Vector2(Screen.width * 0.5f, Screen.height * 0.35f),
                new Vector2(Screen.width * 0.5f, Screen.height * 0.65f),
            };

            foreach (var point in sampleScreenPoints)
            {
                if (raycastManager.Raycast(point, hits, TrackableType.PlaneWithinPolygon))
                    return hits[0].pose.position;
            }

            // No tracked plane under any sample point right now - approximate table height as
            // 1m below the camera rather than spawning at camera height in mid-air.
            return arCamera.transform.position + Vector3.down * 1f;
        }
    }
}
