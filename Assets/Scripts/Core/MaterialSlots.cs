using System;
using UnityEngine;

namespace ScrapSiege.Core
{
    /// <summary>
    /// How an authored model's material slots are repainted at runtime.
    ///
    /// Gameplay colour has to win over whatever colour the model was authored with - it is what
    /// tells the player which archetype a piece is, which side a base belongs to, and it is where
    /// the Pro cosmetic palette lives. But repainting *every* slot with one flat colour (which is
    /// what the terrain spawner used to do) erases all the shape-reading detail the models carry and
    /// leaves each piece an unreadable single-colour blob.
    ///
    /// So models are authored with slots named for their role, and only the body slot takes the
    /// gameplay colour. Accents take a darkened version of it so the piece still reads as one
    /// object, while stands and bare metal stay neutral - which is what makes a base plate read as
    /// "the stand this thing sits on" consistently across every archetype and both teams.
    /// </summary>
    public static class MaterialSlots
    {
        public enum Role
        {
            /// <summary>Takes the gameplay colour directly.</summary>
            Body,

            /// <summary>A darker shade of the gameplay colour - secondary shapes and trim.</summary>
            Accent,

            /// <summary>The dark stand under a piece. Never tinted.</summary>
            Plate,

            /// <summary>Bare steel - rails, mounts, banding. Never tinted.</summary>
            Metal,

            /// <summary>
            /// A fixed high-contrast highlight - the unit crest and shield. Deliberately never
            /// tinted: it is the thing that stays legible when a 5cm figure is seen from across a
            /// table, and tinting it would make it vanish into the team colour it sits on.
            /// </summary>
            Trim
        }

        private static readonly Color PlateColor = new Color(0.13f, 0.13f, 0.15f);
        private static readonly Color MetalColor = new Color(0.42f, 0.44f, 0.47f);
        private static readonly Color TrimColor = new Color(0.98f, 0.62f, 0.16f);

        /// <summary>
        /// Resolves a slot's role from its name. Terrain models use "SS_Body" / "SS_Accent" /
        /// "SS_Plate" / "SS_Metal"; the unit model uses "U_Body" / "U_Dark" / "U_Metal" / "U_Crest",
        /// so "Dark" and "Crest" are matched as well rather than forcing a re-export just to rename
        /// slots. Anything unrecognised falls back to Body, so an un-migrated model still renders in
        /// its gameplay colour rather than turning neutral.
        /// </summary>
        public static Role RoleForSlot(Material source)
        {
            string name = source != null ? source.name : string.Empty;
            if (Has(name, "Plate")) return Role.Plate;
            if (Has(name, "Metal")) return Role.Metal;
            if (Has(name, "Crest") || Has(name, "Trim")) return Role.Trim;
            if (Has(name, "Accent") || Has(name, "Dark")) return Role.Accent;
            return Role.Body;
        }

        private static bool Has(string name, string token)
        {
            return name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Color ColorForRole(Role role, Color body)
        {
            switch (role)
            {
                case Role.Plate: return PlateColor;
                case Role.Metal: return MetalColor;
                case Role.Trim: return TrimColor;
                case Role.Accent: return new Color(body.r * 0.55f, body.g * 0.55f, body.b * 0.55f, 1f);
                default: return body;
            }
        }

        /// <summary>
        /// Repaints every renderer under <paramref name="root"/> slot by slot, instancing from
        /// <paramref name="template"/> so the result always uses the active render pipeline's shader
        /// (a model's imported material, or a primitive's built-in default, may not).
        /// </summary>
        public static void Repaint(GameObject root, Material template, Color body)
        {
            if (root == null || template == null) return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var sources = renderer.sharedMaterials;
                var painted = new Material[sources.Length];
                for (int i = 0; i < sources.Length; i++)
                    painted[i] = new Material(template) { color = ColorForRole(RoleForSlot(sources[i]), body) };

                renderer.sharedMaterials = painted;
            }
        }
    }
}
