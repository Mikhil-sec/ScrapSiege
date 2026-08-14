using UnityEngine;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// The two numbers a match result is graded on: how long it took, and how many of the player's
    /// units died getting there. Fed to <see cref="ScrapSiege.Levels.LevelDefinition.StarsFor"/>.
    ///
    /// <para><b>Why static.</b> The counters have to be written from
    /// <see cref="SiegeUnit.Die"/>, which happens on hundreds of short-lived objects that have no
    /// reason to know a scorekeeper exists and no sane way to be handed a reference to one. A static
    /// counter is a two-line write from there instead of a lookup, an Inspector field on the unit
    /// prefab, or a scene singleton that has to be found. The cost of a static is that it survives
    /// between matches - which is exactly why <see cref="Begin"/> resets everything explicitly
    /// rather than relying on a fresh scene load to do it, since re-entering a level does not
    /// reconstruct static state.</para>
    ///
    /// <para>Clock reads <see cref="Time.time"/>, not <c>Time.unscaledTime</c>: if the game is ever
    /// paused by setting the timescale to zero, a paused match should not be accruing a worse time.</para>
    /// </summary>
    public static class MatchStats
    {
        private static float startTime;
        private static float stoppedElapsed;
        private static bool running;

        /// <summary>Player units that have died this match.</summary>
        public static int PlayerUnitsLost { get; private set; }

        /// <summary>Enemy units the player has killed this match. Not graded; shown on the card.</summary>
        public static int EnemyUnitsLost { get; private set; }

        /// <summary>Seconds since <see cref="Begin"/>, frozen once <see cref="Stop"/> is called.</summary>
        public static float ElapsedSeconds => running ? Time.time - startTime : stoppedElapsed;

        /// <summary>Called when the siege actually starts - not at scene load, which is before the
        /// player has placed the board and could add a minute of AR setup to their time.</summary>
        public static void Begin()
        {
            startTime = Time.time;
            stoppedElapsed = 0f;
            running = true;
            PlayerUnitsLost = 0;
            EnemyUnitsLost = 0;
        }

        /// <summary>Freezes the clock. Safe to call more than once - only the first stop counts.</summary>
        public static void Stop()
        {
            if (!running) return;
            stoppedElapsed = Time.time - startTime;
            running = false;
        }

        /// <summary>Called from <see cref="SiegeUnit.Die"/>. A no-op before <see cref="Begin"/>.</summary>
        public static void ReportUnitLost(Team team)
        {
            if (!running) return;

            if (team == Team.Player) PlayerUnitsLost++;
            else EnemyUnitsLost++;
        }

        /// <summary>"1:23". Minutes and seconds, because a match is authored in the 60-180s range.</summary>
        public static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
