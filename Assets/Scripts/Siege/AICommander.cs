using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using ScrapSiege.Levels;
using ScrapSiege.Terrain;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// The opposing commander (plan.md Section 5). Rule-based utility scoring over an explicit
    /// action set, no learned model of any kind.
    ///
    /// <para><b>Action set: Push / Intercept / Hold.</b> The original plan said "reinforce a
    /// threatened lane", which assumed spawning extra sentries. <b>Intercept replaces it</b> and is
    /// the better behaviour anyway: it needs no sentry work (that system is awaiting its overhaul)
    /// and it directly creates the frontage contest unit combat is built around.</para>
    ///
    /// <para><b>Both offensive actions deploy from the AI's own edge</b>, never next to the player's
    /// units. Intercept picks the <i>lane</i> of the biggest threat rather than the position, so it
    /// reads as "the enemy is reinforcing the lane I committed to" instead of "the enemy teleported a
    /// blocker in front of me". That distinction is the whole difference between a defender that
    /// feels smart and one that feels like it is cheating - and the player's counter is Rally, which
    /// is finally worth its cost now that something reacts to where they went.</para>
    ///
    /// <para><b>Symmetric economy.</b> It drives an ordinary <see cref="ResourceEconomy"/>, the same
    /// component the player uses, at a rate <see cref="AICommanderProfile"/> scales. Difficulty is
    /// decision quality and reaction delay, not free resources.</para>
    /// </summary>
    public class AICommander : MonoBehaviour
    {
        private enum Action
        {
            Hold,
            Push,
            Intercept,
        }

        [Header("Data")]
        [SerializeField] private AICommanderProfile profile;

        [Tooltip("The AI's own resource pool - a SEPARATE ResourceEconomy instance from the player's, " +
                 "not the same component. Sharing one would let the player's spending starve the AI.")]
        [SerializeField] private ResourceEconomy resourceEconomy;

        [Header("Scene")]
        [SerializeField] private LevelMatchController levelMatch;

        [Tooltip("The enemy-team unit prefab. Needs a VisionTarget so line of sight applies to it - " +
                 "the player's own unit prefab deliberately has none, since you always see your army.")]
        [SerializeField] private GameObject enemyUnitPrefab;

        [Header("Events")]
        /// <summary>Fires when a wave is committed, before it appears. The HUD's incoming-attack warning hangs off this.</summary>
        public UnityEvent<Vector3> OnWaveTelegraphed;

        /// <summary>Fires with the number of units actually deployed once a telegraphed wave lands.</summary>
        public UnityEvent<int> OnWaveLanded;

        private readonly List<SiegeUnit> threatBuffer = new List<SiegeUnit>();

        private float threatHeldSeconds;
        private float pendingSpawnTimer;
        private bool hasPendingWave;
        private Vector3 pendingSpawnPoint;
        private int pendingUnitCount;

        private void Awake()
        {
            // Mirrors every other siege system: nothing runs until the match actually starts.
            enabled = false;

            if (profile == null) Debug.LogError("AICommander: Profile is not assigned - the commander cannot make decisions and will stay idle.", this);
            if (resourceEconomy == null) Debug.LogError("AICommander: Resource Economy is not assigned - the commander has nothing to spend and will never act.", this);
            if (levelMatch == null) Debug.LogError("AICommander: Level Match is not assigned - the commander cannot find the bases or the board size.", this);
            if (enemyUnitPrefab == null) Debug.LogError("AICommander: Enemy Unit Prefab is not assigned - the commander can never deploy anything.", this);
            else if (enemyUnitPrefab.GetComponent<SiegeUnit>() == null)
                Debug.LogError("AICommander: Enemy Unit Prefab has no SiegeUnit component.", this);
        }

        /// <summary>
        /// Overrides the Inspector profile with the one the level asked for. Call BEFORE enabling -
        /// <see cref="OnEnable"/> reads the profile to set its tick rate and income, so a profile
        /// applied afterwards would not take effect until the commander was toggled again.
        ///
        /// A null argument deliberately keeps the serialized fallback rather than clearing it, so a
        /// level that opts into an AI but forgets to pick a tier still gets a working opponent.
        /// </summary>
        public void ApplyProfile(AICommanderProfile levelProfile)
        {
            if (levelProfile == null)
            {
                if (profile == null)
                    Debug.LogError("AICommander: the level requested a commander but neither it nor this component has a profile - the AI will stay idle.", this);
                return;
            }

            profile = levelProfile;
        }

        private void OnEnable()
        {
            if (profile == null || resourceEconomy == null) return;

            // Applied before enabling, because ResourceEconomy starts its InvokeRepeating in OnEnable
            // and would otherwise run one full cycle at the player's rate first.
            resourceEconomy.ConfigureTickInterval(resourceEconomy.TickIntervalSeconds * profile.resourceIntervalMultiplier);
            resourceEconomy.enabled = true;

            threatHeldSeconds = 0f;
            hasPendingWave = false;

            InvokeRepeating(nameof(Decide), profile.decisionTickSeconds, profile.decisionTickSeconds);
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(Decide));

            // The match is over (SiegeOutcomeController disables this) - stop the AI's income too,
            // or it keeps ticking behind the results panel.
            if (resourceEconomy != null) resourceEconomy.enabled = false;
        }

        private void Update()
        {
            if (!hasPendingWave) return;

            pendingSpawnTimer -= Time.deltaTime;
            if (pendingSpawnTimer > 0f) return;

            hasPendingWave = false;
            int deployed = SpawnWave(pendingSpawnPoint, pendingUnitCount);
            OnWaveLanded?.Invoke(deployed);
        }

        /// <summary>
        /// One decision. Scores each action and takes the best - deliberately a small, readable
        /// if-ladder rather than a generic utility system, because the point is that a human can look
        /// at this and predict what the AI will do.
        /// </summary>
        private void Decide()
        {
            if (profile == null || levelMatch == null) return;
            if (levelMatch.EnemyBase == null || levelMatch.PlayerBase == null) return;

            // One wave in flight at a time. Committing a second while the first is still telegraphed
            // would spend the bank twice and land two waves on top of each other.
            if (hasPendingWave) return;

            float boardLength = levelMatch.BoardLength;
            if (boardLength <= 0f) return;

            float threat = EvaluateThreat(boardLength);

            // Reaction delay: the threat has to PERSIST. A player unit that briefly strays forward
            // and is then rallied away should not be able to bait a full response.
            if (threat >= profile.interceptThreatThreshold)
                threatHeldSeconds += profile.decisionTickSeconds;
            else
                threatHeldSeconds = 0f;

            if (LiveUnitCount(Team.Enemy) >= profile.maxLiveUnits) return;

            switch (ChooseAction(threat))
            {
                case Action.Intercept:
                    CommitWave(InterceptLateral(boardLength), profile.interceptCost, units: 1);
                    break;

                case Action.Push:
                    CommitWave(WeakestLaneLateral(boardLength), profile.pushCost, UnitsAffordable());
                    break;

                default:
                    break; // Hold - bank and wait.
            }
        }

        private Action ChooseAction(float threat)
        {
            int banked = resourceEconomy != null ? resourceEconomy.CurrentResources : 0;

            // Intercept outranks Push: a wave sent while the player is already at the gates does
            // nothing to stop them losing you the match.
            bool threatIsReal = threatHeldSeconds >= profile.reactionDelaySeconds;
            if (threatIsReal && banked >= profile.interceptCost) return Action.Intercept;

            // Otherwise commit only once the bank is deep enough for a wave worth telegraphing.
            if (banked >= profile.holdBankTarget && banked >= profile.pushCost) return Action.Push;

            return Action.Hold;
        }

        private int UnitsAffordable()
        {
            int banked = resourceEconomy != null ? resourceEconomy.CurrentResources : 0;
            int cost = Mathf.Max(1, profile.pushCost);

            // Leave nothing back: a push is the payoff for having held, and a wave of two reads far
            // better than two waves of one.
            int affordable = banked / cost;
            int headroom = profile.maxLiveUnits - LiveUnitCount(Team.Enemy);
            return Mathf.Clamp(affordable, 1, Mathf.Max(1, headroom));
        }

        /// <summary>
        /// How far the player's most advanced unit has pushed, as 0..1 of the board. 1 means it is on
        /// top of the AI's base. Deliberately the single most advanced unit rather than an average -
        /// the thing that loses the AI the match is one unit arriving, not the mean position of six.
        /// </summary>
        private float EvaluateThreat(float boardLength)
        {
            Vector3 target = levelMatch.EnemyBase.position;
            float nearest = float.MaxValue;

            foreach (var unit in SiegeUnit.Active)
            {
                if (unit == null || !unit.IsAlive) continue;
                if (unit.Team != Team.Player) continue;

                float distance = Vector3.Distance(unit.transform.position, target);
                if (distance < nearest) nearest = distance;
            }

            if (nearest == float.MaxValue) return 0f;

            return Mathf.Clamp01(1f - nearest / boardLength);
        }

        private static int LiveUnitCount(Team team)
        {
            int count = 0;
            foreach (var unit in SiegeUnit.Active)
                if (unit != null && unit.IsAlive && unit.Team == team) count++;

            return count;
        }

        // --- Lane geometry -------------------------------------------------------------------
        //
        // Everything is derived from the two base transforms rather than the board root, so this
        // needs no knowledge of the board's placement, rotation or normalised authoring space. The
        // advance axis is simply "from my base toward theirs".

        private Vector3 AdvanceDirection()
        {
            Vector3 advance = levelMatch.PlayerBase.position - levelMatch.EnemyBase.position;
            advance.y = 0f;
            return advance.sqrMagnitude < 1e-6f ? Vector3.forward : advance.normalized;
        }

        private Vector3 LateralAxis() => Vector3.Cross(Vector3.up, AdvanceDirection()).normalized;

        /// <summary>Signed offset of a world point across the board, relative to the AI's base.</summary>
        private float LateralOffsetOf(Vector3 worldPoint)
        {
            return Vector3.Dot(worldPoint - levelMatch.EnemyBase.position, LateralAxis());
        }

        /// <summary>The lane of the player's most advanced unit - "reinforce where they committed".</summary>
        private float InterceptLateral(float boardLength)
        {
            Vector3 target = levelMatch.EnemyBase.position;
            float nearest = float.MaxValue;
            float lateral = 0f;

            foreach (var unit in SiegeUnit.Active)
            {
                if (unit == null || !unit.IsAlive) continue;
                if (unit.Team != Team.Player) continue;

                float distance = Vector3.Distance(unit.transform.position, target);
                if (distance >= nearest) continue;

                nearest = distance;
                lateral = LateralOffsetOf(unit.transform.position);
            }

            return lateral;
        }

        /// <summary>
        /// The lane with the fewest player units near it - plan.md's "push the weakest-defended
        /// approach". Sampled across the board's real width, which comes from the level's own aspect
        /// ratio so it is correct on any table size.
        /// </summary>
        private float WeakestLaneLateral(float boardLength)
        {
            float aspect = levelMatch.ActiveLevel != null ? levelMatch.ActiveLevel.boardAspect : 0.6f;
            float halfWidth = boardLength * aspect * 0.5f;

            int samples = Mathf.Max(3, profile.laneSamples);
            float bestLateral = 0f;
            float bestScore = float.MaxValue;
            float laneRadius = boardLength * 0.15f;

            for (int i = 0; i < samples; i++)
            {
                // Inset from the very edge so a "lane" is never a strip of board the units cannot
                // actually walk down.
                float t = samples == 1 ? 0.5f : i / (float)(samples - 1);
                float lateral = Mathf.Lerp(-halfWidth * 0.8f, halfWidth * 0.8f, t);

                float score = PlayerPresenceNear(lateral, laneRadius);
                if (score >= bestScore) continue;

                bestScore = score;
                bestLateral = lateral;
            }

            return bestLateral;
        }

        private float PlayerPresenceNear(float lateral, float laneRadius)
        {
            float presence = 0f;

            foreach (var unit in SiegeUnit.Active)
            {
                if (unit == null || !unit.IsAlive) continue;
                if (unit.Team != Team.Player) continue;

                float distance = Mathf.Abs(LateralOffsetOf(unit.transform.position) - lateral);
                if (distance < laneRadius) presence += 1f - distance / laneRadius;
            }

            return presence;
        }

        // --- Commitment and spawning --------------------------------------------------------

        /// <summary>
        /// Charges the wave and telegraphs it. Resources are spent at COMMIT time, not at spawn time,
        /// so the bank cannot be double-committed during the telegraph window.
        /// </summary>
        private void CommitWave(float lateral, int costPerUnit, int units)
        {
            units = Mathf.Max(1, units);

            int total = costPerUnit * units;
            if (resourceEconomy == null || !resourceEconomy.TrySpend(total))
            {
                // Afford what we can rather than dropping the decision entirely.
                units = 1;
                if (resourceEconomy == null || !resourceEconomy.TrySpend(costPerUnit)) return;
            }

            float boardLength = levelMatch.BoardLength;
            Vector3 spawn = levelMatch.EnemyBase.position
                            + AdvanceDirection() * boardLength * profile.spawnOffsetFraction
                            + LateralAxis() * lateral;

            pendingSpawnPoint = spawn;
            pendingUnitCount = units;
            pendingSpawnTimer = Mathf.Max(0f, profile.telegraphLeadSeconds);
            hasPendingWave = true;

            OnWaveTelegraphed?.Invoke(spawn);
        }

        private int SpawnWave(Vector3 origin, int units)
        {
            if (enemyUnitPrefab == null || levelMatch.PlayerBase == null) return 0;

            float boardLength = levelMatch.BoardLength;
            float scatter = boardLength * 0.04f;
            int deployed = 0;

            for (int i = 0; i < units; i++)
            {
                Vector2 offset = Random.insideUnitCircle * scatter;
                Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);

                // Same rule the player's deploy follows: never spawn a unit onto ground its own agent
                // cannot stand on, or it reports off-mesh and the zero-remainingDistance trap fires.
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, boardLength * 0.08f, NavMesh.AllAreas))
                    continue;

                var go = Instantiate(enemyUnitPrefab, hit.position, Quaternion.identity);
                var unit = go.GetComponent<SiegeUnit>();
                if (unit == null) continue;

                unit.SetTeam(Team.Enemy);
                NavMeshAreas.ApplyCoverPreference(unit.Agent, Random.value < profile.coveredPreferenceChance);
                unit.ConfigureForBoard(boardLength);
                unit.SetTarget(levelMatch.PlayerBase.position, levelMatch.PlayerBaseHealth);

                deployed++;
            }

            if (deployed == 0)
                Debug.LogWarning("AICommander: a committed wave found no walkable spawn point - check the level's enemy end is on the NavMesh.", this);

            return deployed;
        }
    }
}
