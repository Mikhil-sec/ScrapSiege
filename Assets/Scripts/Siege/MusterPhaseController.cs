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
        [SerializeField] private float navMeshSnapDistance = 0.2f;

        public void SpawnGarrison(IReadOnlyList<TerrainObjectData> terrainObjects)
        {
            if (garrisonUnitPrefab == null)
            {
                Debug.LogError("MusterPhaseController: Garrison Unit Prefab is not assigned - no garrison will spawn.", this);
                return;
            }

            int spawned = 0;

            foreach (var obj in terrainObjects)
            {
                if (spawned >= maxGarrisonUnits) break;

                bool isChokepoint = obj.Archetype == TerrainArchetype.SpireChokepoint
                    || obj.Archetype == TerrainArchetype.Watchtower;
                if (!isChokepoint) continue;

                if (!NavMesh.SamplePosition(obj.Center, out NavMeshHit hit, navMeshSnapDistance, NavMesh.AllAreas))
                    continue;

                Instantiate(garrisonUnitPrefab, hit.position, Quaternion.identity);
                spawned++;
            }
        }
    }
}
