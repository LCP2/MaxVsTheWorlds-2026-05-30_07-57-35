using System.Collections.Generic;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-1453: the ONE table of RIG node descriptions (what an upgrade does in play, no numbers - the value
    /// line shows those) and KEY flags (the "top upgrade" chip). Keyed by weapon/ability kind plus node id,
    /// because node ids are shared across worlds and the weapon kind decides their meaning
    /// (see <see cref="RigEffectLines"/>). A "p_" node resolves under the equipped primary, an "s_" node under
    /// the equipped secondary, everything else under "Shared". More KEY nodes are added here, as data.
    /// </summary>
    public static class RigDescriptions
    {
        public const int MaxLength = 60;

        public readonly struct Entry
        {
            public readonly string Text;
            public readonly bool Key;
            public Entry(string text, bool key = false) { Text = text; Key = key; }
        }

        private static readonly Dictionary<string, Entry> Table = new Dictionary<string, Entry>
        {
            // Water blaster (RCDA) - the first world.
            { "Rcda.p_dmg", new Entry("How hard the water spray hits.") },
            { "Rcda.p_rng", new Entry("How far the spray reaches.") },
            { "Rcda.p_flw", new Entry("The tank drains slower, so you spray longer.") },
            { "Rcda.p_spr", new Entry("How wide the spray fans out.") },
            // Pulse laser (LPPE).
            { "Lppe.p_dmg", new Entry("How hard each pulse hits.") },
            { "Lppe.p_rng", new Entry("How far the laser can lock on.") },
            { "Lppe.p_rof", new Entry("Pulses per second. More pulses, more hits.") },
            { "Lppe.p_frk", new Entry("Pulses arc between nearby robots.") },
            { "Lppe.p_cap", new Entry("How long you can fire before recharging.") },
            // Undertow lance.
            { "Undertow.p_dmg", new Entry("How hard the lance hits.") },
            { "Undertow.p_rng", new Entry("How far the lance reaches.") },
            { "Undertow.p_spr", new Entry("Splits the lance into more beams.") },
            { "Undertow.p_trp", new Entry("Lets you trap robots in place.") },
            { "Undertow.p_tcap", new Entry("How many traps you can have out.") },
            { "Undertow.p_trad", new Entry("How big each trap is.") },
            // Ricochet discs (the City).
            { "Ricochet.p_dmg", new Entry("How hard each disc hits.") },
            { "Ricochet.p_ric", new Entry("Bounces per disc. Each bounce hits harder.", true) },
            { "Ricochet.p_aim", new Entry("Discs curve toward robots. Fewer misses.", true) },
            { "Ricochet.p_rof", new Entry("Discs per second. More discs, more hits.", true) },
            { "Ricochet.p_cap", new Entry("How long you can fire before recharging.") },
            // Light missile (the ship).
            { "LightMissile.p_dmg", new Entry("How hard each ball hits.") },
            { "LightMissile.p_cnt", new Entry("Balls fired in each volley.") },
            { "LightMissile.p_trk", new Entry("Balls curve toward robots. Fewer misses.") },
            { "LightMissile.p_rof", new Entry("Volleys per second. More volleys, more hits.") },
            { "LightMissile.p_cap", new Entry("How long you can fire before recharging.") },
            // Water balloon.
            { "WaterBalloon.s_bal", new Entry("Throws a water balloon that splashes robots.") },
            { "WaterBalloon.s_spl", new Entry("How big the balloon splash is.") },
            { "WaterBalloon.s_lob", new Entry("How far you can throw it.") },
            { "WaterBalloon.s_aut", new Entry("Balloons throw themselves at robots.") },
            { "WaterBalloon.s_rte", new Entry("How soon you can throw again.") },
            // Shoulder rack.
            { "ShoulderRack.s_rkt", new Entry("How hard each rocket hits.") },
            { "ShoulderRack.s_spl", new Entry("How wide each rocket blast is.") },
            { "ShoulderRack.s_sal", new Entry("Rockets launched in each salvo.") },
            { "ShoulderRack.s_rld", new Entry("How fast the rack reloads.") },
            { "ShoulderRack.s_clu", new Entry("Rockets burst into small bombs.") },
            // Orbitals.
            { "Orbitals.s_orb", new Entry("Orbs circle you and hit robots they touch.") },
            { "Orbitals.s_rad", new Entry("How far the orbs swing out from you.") },
            { "Orbitals.s_odm", new Entry("How hard each orb hits on touch.") },
            { "Orbitals.s_spd", new Entry("How fast the orbs spin round you.") },
            // Systems - shared across every world.
            { "Shared.e_ff", new Entry("A shield that soaks up hits.") },
            { "Shared.e_cel", new Entry("How many parts you can carry.") },
            { "Shared.e_mag", new Entry("Pulls nearby parts toward you.") },
            { "Shared.e_cmg", new Entry("Pulls nearby cells toward you.") },
            { "Shared.m_spd", new Entry("How fast Max runs.") },
            { "Shared.m_tp", new Entry("How far a teleport jumps you.") },
            { "Shared.u_sen", new Entry("Deploys a sentinel that fights for you.") },
            { "Shared.u_dmg", new Entry("How hard the sentinel's shots hit.") },
            { "Shared.u_rng", new Entry("How far the sentinel can shoot.") },
            { "Shared.u_hp", new Entry("How much damage the sentinel can take.") },
            { "Shared.u_mov", new Entry("How fast the sentinel moves.") },
            { "Shared.u_cst", new Entry("Makes each sentinel cheaper to deploy.") },
            { "Shared.u_slt", new Entry("How many sentinels you can field at once.") },
        };

        /// <summary>The table key for <paramref name="id"/> under the equipped pair.</summary>
        public static string KeyFor(WeaponCatalog.PrimaryKind primary, SecondaryKind secondary, string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("p_")) return primary + "." + id;
            if (id.StartsWith("s_")) return secondary + "." + id;
            return "Shared." + id;
        }

        /// <summary>The description/KEY entry for a node, or false when the table has none.</summary>
        public static bool TryGet(WeaponCatalog.PrimaryKind primary, SecondaryKind secondary, string id, out Entry entry) =>
            Table.TryGetValue(KeyFor(primary, secondary, id), out entry);

        public static string Description(WeaponCatalog.PrimaryKind primary, SecondaryKind secondary, string id) =>
            TryGet(primary, secondary, id, out var e) ? e.Text : "";

        public static bool IsKey(WeaponCatalog.PrimaryKind primary, SecondaryKind secondary, string id) =>
            TryGet(primary, secondary, id, out var e) && e.Key;
    }
}
