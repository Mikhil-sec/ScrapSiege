using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Terrain;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// plan.md's Muster phase: "starting garrison auto-populates based on the chokepoints
    /// created." Runs once, after the NavMesh is baked and before Siege's resource/deploy
    /// systems turn on, so terrain-building itself pays off strategically - more chokepoints
    /// built during Fortify means more free defenders holding them at the start of Siege.
    /// Garrison units are stationary for now (GarrisonSentry gives them a detect-and-damage
    /// role); nothing yet moves them, since there's no combat AI to react to.
    /// </summary>
    public class MusterPhaseController : MonoBehaviour
    {
        [SerializeField] private GameObject garrisonUnitPrefab;
        [SerializeField] private int maxGarrisonUnits = 3;

        [Tooltip("Supplies the board length that every spawned sentry's reach is scaled against.")]
        [SerializeField] private ScrapSiege.Core.BoardPlane boardPlane;

        [Tooltip("How far from a chokepoint to look for walkable ground, as a fraction of board " +
                 "length. The old absolute 0.2m was a third of a 0.60m board, so a sentry could be " +
                 "placed a long way from the chokepoint it was supposed to be holding.")]
        [SerializeField] private float navMeshSnapFraction = 0.04f;

        [Tooltip("Used only when no board has been established (the legacy scan/Fortify flow).")]
        [SerializeField] private float navMeshSnapFallback = 0.05f;

        [Tooltip("Degrees of random yaw added to each sentry's facing. Without this every sentry " +
                 "covers the identical bearing, so one walking position flanks all of them at once " +
                 "and the flanking mechanic collapses into a single correct answer.")]
        [SerializeField] private float facingJitterDegrees = 35f;

        /// <summary>
        /// Overrides the garrison cap for the level about to be played. Authored levels tune how
        /// many free defenders their layout deserves, which is a per-map balance decision rather
        /// than a scene-wide constant.
        /// </summary>
        public void SetMaxGarrisonUnits(int cap) => maxGarrisonUnits = Mathf.Max(0, cap);

        /// <summary>
        /// Spawns the free starting garrison. <paramref name="threatOrigin"/> is where the player's
        /// units advance from - sentries turn to face it, which puts their blind side away from the
        /// attacker and makes walking around the table to reach that side a real tactic.
        /// </summary>
        public void SpawnGarrison(IReadOnlyList<TerrainObjectData> terrainObjects, Vector3 threatOrigin)
        {
            if (garrisonUnitPrefab == null)
            {
                Debug.LogError("MusterPhaseController: Garrison Unit Prefab is not assigned - no garrison will spawn.", this);
                return;
            }

            int spawned = 0;
            float boardLength = boardPlane != null ? boardPlane.Length : 0f;
            float snapDistance = boardLength > 0f
                ? navMeshSnapFraction * boardLength
                : ScrapSiege.Core.WorldScale.Metres(navMeshSnapFallback);

            foreach (var obj in terrainObjects)
            {
                if (spawned >= maxGarrisonUnits) break;

                bool isChokepoint = obj.Archetype == TerrainArchetype.SpireChokepoint
                    || obj.Archetype == TerrainArchetype.Watchtower;
                if (!isChokepoint) continue;

                if (!NavMesh.SamplePosition(obj.Center, out NavMeshHit hit, snapDistance, NavMesh.AllAreas))
                    continue;

                var sentryObject = Instantiate(garrisonUnitPrefab, hit.position, FacingToward(hit.position, threatOrigin));

                // Between Awake and Start, which is why SentryArcVisualizer draws its fan in Start -
                // otherwise the wedge would advertise the unscaled fallback range.
                var sentry = sentryObject.GetComponent<GarrisonSentry>();
                if (sentry != null)
                {
                    if (boardLength > 0f) sentry.ConfigureForBoard(boardLength);
                    sentry.SetVantage(VantageOf(obj, hit.position));
                }

                spawned++;
            }
        }

        /// <summary>
        /// The point this sentry watches from: directly above its anchor's centre, at the anchor's
        /// own measured top.
        ///
        /// <para>Needed because a sentry stands on the ground BESIDE the chokepoint it garrisons -
        /// the anchor carves a NavMesh hole, so <c>NavMesh.SamplePosition(obj.Center)</c> snaps
        /// outside its footprint. Once <see cref="GarrisonSentry"/> gained a real line-of-sight test
        /// (2026-08-13), a ground-level eye would have been blocked by the sentry's own tower, which
        /// would have silently disabled every sentry in the game and broken Blind Spire outright.
        /// Watching from the top of the tower is both the fix and what the fiction already claimed.
        /// </para>
        ///
        /// <para>Measured off the spawned visual's renderers rather than computed from the height
        /// category, per the project-wide rule that sizes come from the model. Falls back to the
        /// anchor's own centre if the piece somehow has no visual, which degrades to "sees what a
        /// unit standing there would see" rather than to nothing.</para>
        /// </summary>
        private static Vector3 VantageOf(TerrainObjectData anchor, Vector3 fallback)
        {
            if (anchor?.Visual == null) return fallback;

            bool any = false;
            Bounds bounds = default;

            foreach (var renderer in anchor.Visual.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!any) return fallback;

            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        private Quaternion FacingToward(Vector3 from, Vector3 target)
        {
            Vector3 toThreat = target - from;
            toThreat.y = 0f;

            // Degenerate case: sentry spawned exactly on the threat origin. Any facing is as valid
            // as another, so keep identity rather than feeding a zero vector to LookRotation.
            if (toThreat.sqrMagnitude < 0.0001f) return Quaternion.identity;

            Quaternion look = Quaternion.LookRotation(toThreat.normalized, Vector3.up);
            float jitter = Random.Range(-facingJitterDegrees, facingJitterDegrees);
            return look * Quaternion.Euler(0f, jitter, 0f);
        }
    }
}
