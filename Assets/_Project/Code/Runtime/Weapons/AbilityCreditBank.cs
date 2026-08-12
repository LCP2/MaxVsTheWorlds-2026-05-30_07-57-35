using System;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// A shed pickup's reward, decoupled from the reveal (MV-358, correcting MV-357's mid-fight modal):
    /// destroying a shed banks a buildable ability credit here rather than drawing candidates and
    /// granting anything on the spot. The credit sits banked — flashing the HUD's ABILITIES button via
    /// the same badge <see cref="MaxWorlds.Pickups.PickupWallet"/>'s parts already use — until the
    /// player opens the Abilities screen and spends it on a BUILD ABILITY draw of their own choosing.
    /// Static/event-driven, same shape as <see cref="WeaponSystemState"/>: one Max, several systems
    /// (the HUD badge, the Abilities screen's button) reading the same number.
    /// </summary>
    public static class AbilityCreditBank
    {
        /// <summary>Buildable ability credits currently banked and unspent.</summary>
        public static int Banked { get; private set; }

        /// <summary>Fired whenever a credit is banked or spent, or the state is reset.</summary>
        public static event Action<int> Changed;

        /// <summary>A shed was destroyed with an ability still worth offering — bank one credit.</summary>
        public static void Bank()
        {
            Banked++;
            Changed?.Invoke(Banked);
        }

        /// <summary>Spend one banked credit (the BUILD ABILITY button). No-ops (returns false) if none
        /// are banked.</summary>
        public static bool TrySpend()
        {
            if (Banked <= 0) return false;
            Banked--;
            Changed?.Invoke(Banked);
            return true;
        }

        /// <summary>Back to a fresh run's baseline: no credits banked. Test isolation and a new run.</summary>
        public static void Reset()
        {
            Banked = 0;
            Changed?.Invoke(Banked);
        }
    }
}
