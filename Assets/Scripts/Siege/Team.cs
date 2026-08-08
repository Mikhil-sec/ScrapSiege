namespace ScrapSiege.Siege
{
    /// <summary>
    /// Which side a combatant belongs to.
    ///
    /// <para>Introduced with the AI commander. Before it there was only one mobile army - the
    /// player's - so <see cref="SiegeUnit.Active"/> being a single flat static list was harmless and
    /// <see cref="GarrisonSentry"/> could damage everything in it unconditionally. The moment the AI
    /// deploys its own units into that same list, "everything in it" is wrong in two separate
    /// places: sentries would shoot their own side, and a Rally order would redirect the AI's
    /// attackers on the player's behalf. Both are filtered on this enum.</para>
    ///
    /// <para><see cref="Player"/> deliberately sorts first so <c>default(Team)</c> is the player -
    /// anything spawned by the existing deploy path without an explicit call keeps its old
    /// behaviour rather than silently defecting.</para>
    /// </summary>
    public enum Team
    {
        Player = 0,
        Enemy = 1,
    }

    public static class TeamExtensions
    {
        public static Team Opponent(this Team team) => team == Team.Player ? Team.Enemy : Team.Player;
    }
}
