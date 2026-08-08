namespace ScrapSiege.Core
{
    /// <summary>
    /// How many Unity metres one real-world metre occupies.
    ///
    /// <para><b>Why this exists.</b> Unity hard-clamps <c>NavMeshBuildSettings.agentRadius</c> to a
    /// floor of 0.05 m — writes below that read back as exactly 0.05, for every agent type, and the
    /// clamp happens on load, which is why it used to look like the settings file was being
    /// rewritten. On a real 0.33 m-wide table that erosion is about a sixth of the board, and a
    /// connectivity solve over the shipped levels showed all three severed: The Narrows would need a
    /// 0.76 m board, Blind Spire 1.14 m, Two Lanes 0.72 m, just for a hairline path.</para>
    ///
    /// <para>So the simulation is scaled up instead of the NavMesh being scaled down. The XR Origin
    /// carries a uniform scale of <see cref="Scale"/>, which means one real metre of table becomes
    /// <see cref="Scale"/> Unity metres and the 0.05 floor costs only <c>0.05 / Scale</c> real
    /// metres. At 5 that is 1 cm — near enough the trooper's own ~1.2 cm base, which is what the
    /// levels were designed around in the first place.</para>
    ///
    /// <para><b>How to use it.</b> Keep serialized fields authored in <i>real</i> metres, so the
    /// Inspector stays human-readable ("20 cm above the table" means something; "1.0" does not), and
    /// convert at the point of use with <see cref="Metres"/>. Anything already expressed as a
    /// fraction of <c>BoardPlane.Length</c> needs no conversion at all — it scales for free, which
    /// is most of the gameplay tuning after the 2026-08-08 board-relative pass.</para>
    ///
    /// <para><b>Areas scale by the square</b> — use <see cref="SquareMetres"/>, not
    /// <see cref="Metres"/>, or a plane-area threshold will be 5x too permissive.</para>
    ///
    /// <para>Set this back to 1 and the game returns to true 1:1 world scale (and to a severed
    /// NavMesh). It is the single knob for the whole thing.</para>
    /// </summary>
    public static class WorldScale
    {
        /// <summary>Unity metres per real metre. Must match the XR Origin's uniform localScale.</summary>
        public const float Scale = 5f;

        /// <summary>Converts a length authored in real metres into Unity metres.</summary>
        public static float Metres(float realMetres) => realMetres * Scale;

        /// <summary>Converts an area authored in real square metres into Unity square metres.</summary>
        public static float SquareMetres(float realSquareMetres) => realSquareMetres * Scale * Scale;

        /// <summary>Converts a Unity-space length back into real metres, for diagnostics and HUD text.</summary>
        public static float ToRealMetres(float unityMetres) => unityMetres / Scale;
    }
}
