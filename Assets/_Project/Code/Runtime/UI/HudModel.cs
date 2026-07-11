using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Slice run-state the HUD (YT-30) reads: XP track, arena progress, the Ultimate
    /// charge, the auto-cycling Bomb cooldown, and the boss. HP and Energy are NOT here —
    /// those bind directly to the live <c>PlayerHealth</c> / <c>WaterBlaster.Energy</c>.
    ///
    /// Because the real economy, destructible factories, and boss fight are later tickets,
    /// this model is driven off one signal — <see cref="RegisterKill"/> — so every HUD
    /// element visibly animates on the WebGL build from actual play: kills grant XP, tick
    /// factories/sub-zones, charge the Ultimate, and (once the arena is cleared) engage and
    /// drain the slice boss. The kill→milestone maths lives here so it is unit-testable.
    /// </summary>
    public sealed class HudModel
    {
        // --- Slice tuning (kept modest so milestones are reachable in a short play) ---
        private readonly int _xpPerKill;
        private readonly int _killsPerFactory;
        private readonly float _ultimateChargePerKill;
        private readonly float _bossDamagePerKill;
        private readonly string _bossName;
        private readonly int _bossPhases;

        public XpTrack Xp { get; }
        public ArenaProgress Arena { get; }
        public BossState Boss { get; }

        /// <summary>Auto-cycling secondary ability (Bomb slot). Ticked by the HUD; retriggers
        /// itself when ready so the radial wipe is continuously demonstrated in the slice.</summary>
        public AbilityCooldown Bomb { get; }

        /// <summary>Ultimate charge, 0..1. Fills from kills; at 1 the slot glows "ready".</summary>
        public float UltimateCharge { get; private set; }

        public int Kills { get; private set; }
        private bool _bossTriggered;

        /// <summary>XP granted per kill (HUD shows it on the SPARKS pickup).</summary>
        public int XpPerKill => _xpPerKill;

        public bool UltimateReady => UltimateCharge >= 1f;

        /// <summary>Radial-wipe fill for the Ultimate slot: full while empty, emptying as it
        /// charges (mirrors the cooldown slots — a wipe that clears when the slot is ready).</summary>
        public float UltimateRadialFill => 1f - Mathf.Clamp01(UltimateCharge);

        public HudModel(
            int subZonesTotal = 1,
            int factoriesTotal = 3,
            int xpPerKill = 6,
            int killsPerFactory = 4,
            float ultimateChargePerKill = 0.12f,
            float bombCooldown = 5f,
            string bossName = "BIG BERMUDA",
            int bossPhases = 3,
            float bossDamagePerKill = 0.08f)
        {
            _xpPerKill = Mathf.Max(0, xpPerKill);
            _killsPerFactory = Mathf.Max(1, killsPerFactory);
            _ultimateChargePerKill = Mathf.Max(0f, ultimateChargePerKill);
            _bossDamagePerKill = Mathf.Max(0f, bossDamagePerKill);
            _bossName = bossName;
            _bossPhases = Mathf.Max(1, bossPhases);

            Xp = new XpTrack();
            Arena = new ArenaProgress(subZonesTotal, factoriesTotal);
            Boss = new BossState();
            Bomb = new AbilityCooldown(bombCooldown);
        }

        /// <summary>
        /// Advance the whole slice progression by one kill. Grants XP, charges the Ultimate,
        /// destroys a factory every N kills, clears a sub-zone each time all factories fall,
        /// engages the boss once the arena is fully cleared, and drains the boss thereafter.
        /// </summary>
        public void RegisterKill()
        {
            Kills++;
            Xp.Add(_xpPerKill);
            UltimateCharge = Mathf.Clamp01(UltimateCharge + _ultimateChargePerKill);

            if (Boss.Active)
            {
                Boss.Damage(_bossDamagePerKill);
                return;
            }

            // Destroy a factory every N kills; clearing all factories clears a sub-zone.
            if (Kills % _killsPerFactory == 0 && Arena.FactoriesDestroyed < Arena.FactoriesTotal)
            {
                Arena.DestroyFactory();
                if (Arena.FactoriesDestroyed >= Arena.FactoriesTotal)
                {
                    Arena.ClearSubZone();
                }
            }

            // Boss enters when the arena is fully cleared and it hasn't already run.
            if (Arena.Complete && !_bossTriggered)
            {
                _bossTriggered = true;
                Boss.Engage(_bossName, _bossPhases);
            }
        }

        /// <summary>Spend the Ultimate if charged. Returns true if it fired.</summary>
        public bool TriggerUltimate()
        {
            if (!UltimateReady) return false;
            UltimateCharge = 0f;
            return true;
        }
    }
}
