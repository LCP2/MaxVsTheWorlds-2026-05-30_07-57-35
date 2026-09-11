using System;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Fire-and-forget event hub the HUD (YT-30) listens on for combat overlays, so
    /// gameplay can announce hits/kills without depending on any HUD type. Emitters
    /// (e.g. <c>RobotEnemy</c>) call the <c>Emit*</c> helpers; the <c>HudController</c>
    /// subscribes while enabled and unsubscribes on teardown. All events are null-safe
    /// with no subscribers (so headless tests that damage enemies stay silent).
    /// </summary>
    public static class HudSignals
    {
        /// <summary>A damageable took a hit. (worldPos, amount, crit)</summary>
        public static event Action<Vector3, float, bool> DamageDealt;

        /// <summary>Max himself took a hit (MV-722) — the health bar moving was the only feedback he
        /// had. (worldPos, hitDirection, isContact) — isContact is PlayerHealth's own resolved read of
        /// whether this hit landed hot on the heels of the last one (a crowd/boss/beam signature) or in
        /// isolation, so a listener can raise a different, SUBTLE effect for each without needing to
        /// know which enemy or attack dealt it.</summary>
        public static event Action<Vector3, Vector3, bool> PlayerHit;

        /// <summary>A pickup/reward dropped. (worldPos, label, colour)</summary>
        public static event Action<Vector3, string, Color> Pickup;

        /// <summary>A Supercell was collected (MV-519) — grants its cells instantly, no bank/cash-in
        /// step. (worldPos, cellsBefore, cellsAfter) — HudController drives its self-terminating burst +
        /// "+10" flyup + readout count-up event off this, since a plain floating toast (see
        /// <see cref="Pickup"/>) can't carry the readout's own before/after values.</summary>
        public static event Action<Vector3, int, int> SupercellCollected;

        /// <summary>An enemy died — HUD converts to a SPARKS pickup. (worldPos)</summary>
        public static event Action<Vector3> EnemyKilled;

        /// <summary>A real factory came online — HUD stops driving the arena tracker off kills
        /// and waits for <see cref="FactoryDestroyed"/> instead (YT-37).</summary>
        public static event Action FactoryRegistered;

        /// <summary>A factory was destroyed — HUD advances the arena tracker for real. (worldPos)</summary>
        public static event Action<Vector3> FactoryDestroyed;

        /// <summary>A real boss exists — HUD stops driving the boss bar off the kill stand-in (YT-27).</summary>
        public static event Action BossRegistered;

        /// <summary>The boss engaged — show the bar + name card. (name, phases)</summary>
        public static event Action<string, int> BossEngaged;

        /// <summary>The boss's HP changed. (normalized 0..1)</summary>
        public static event Action<float> BossHealthChanged;

        /// <summary>The boss's SPAWN LEVEL changed (MV-588) — how far the brood volley's composition
        /// has escalated. (level 1..4, progress 0..1 toward the next level)</summary>
        public static event Action<int, float> BossSpawnLevelChanged;

        /// <summary>The boss was defeated — hide the bar.</summary>
        public static event Action BossDefeated;

        /// <summary>A single boss died (MV-721). Distinct from <see cref="BossDefeated"/>, which fires
        /// only once every boss IN ITS AREA is down (MV-591): this fires for every boss's own death, so
        /// a boss that falls with company still gets its own spectacle. (worldPos) — carries where it
        /// died so a listener (<c>BossSpectacle</c>, <c>BossDebris</c>) never has to hunt for the
        /// instance via <c>FindFirstObjectByType</c>, which finds an arbitrary boss once more than one
        /// exists and finds none at all once the dying one deactivates itself.</summary>
        public static event Action<Vector3> BossKilled;

        /// <summary>The boss-death payoff has run its course (YT-152) — Max walked out through the exit
        /// gate, or the sequence timed out. This is the cue to finally show the results card, decoupled
        /// from <see cref="BossDefeated"/> so the blow-up, the flung parts and the walk-out can play
        /// first instead of the run cutting straight to results.</summary>
        public static event Action BossPayoffFinished;

        /// <summary>The final area has been cleared of everything (MV-591) — every robot dead, none
        /// still queued to arrive, and no boss there alive. This — not a boss dying — is what ends the
        /// run; a boss can fall mid-run (a12, a20) without this ever firing.</summary>
        public static event Action RunComplete;

        /// <summary>World 1's finale Weapon Core (MV-698) was just placed on the ground — the run-seal
        /// counterpart to <see cref="RunComplete"/>: <c>RunTracker</c> must not show results while a
        /// dropped core is still uncollected, so it starts waiting the moment this fires (and stops
        /// waiting once <see cref="WeaponCoreCollected"/> answers it, walk-over or the auto-collect
        /// timeout alike).</summary>
        public static event Action WeaponCoreDropped;

        /// <summary>The dropped Weapon Core was collected — a real walk-over
        /// (<c>PickupDirector.Collect</c>) or the grace-timeout auto-collect (<c>RunTracker</c>) alike,
        /// so a listener never has to care which.</summary>
        public static event Action WeaponCoreCollected;

        /// <summary>A Blinker just teleported (MV-330). (fromWorldPos, toWorldPos) — the reposition in
        /// <c>RobotEnemy.TickTeleport</c> is a same-frame snap, so this carries BOTH points rather than
        /// just one: unlike a death or a hit, the VFX has to land at two places, not one.</summary>
        public static event Action<Vector3, Vector3> BlinkerTeleported;

        /// <summary>Max himself teleported (MV-338). (fromWorldPos, toWorldPos) — same two-point shape
        /// as <see cref="BlinkerTeleported"/>, kept as a distinct event rather than reusing it: Max's own
        /// blink drives both a bigger VFX beat and a brief time-slow (<c>GameFeel</c>), neither of which
        /// should fire off an enemy's teleport.</summary>
        public static event Action<Vector3, Vector3> MaxTeleported;

        /// <summary>A homing missile detonated — a direct hit OR an out-of-fuel ground impact
        /// (MV-349). (worldPos, damage) — damage is 0 when the blast landed on empty ground, so
        /// listeners can tell a real hit from a miss without a second event.</summary>
        public static event Action<Vector3, float> MissileImpact;

        /// <summary>A homing missile just ran out of fuel and is sputtering before it drops
        /// (MV-349 AC3). (worldPos)</summary>
        public static event Action<Vector3> MissileSputtering;

        /// <summary>An out-of-fuel missile bounced off the ground (MV-349 AC3). (worldPos)</summary>
        public static event Action<Vector3> MissileBounced;

        /// <summary>The LPPE's own 4th-hit Shock pulse landed (MV-770) — (worldPos). Fired only for the
        /// hit that actually lands Shock, never a plain pulse hit, so a listener (<c>GameFeel</c>) can
        /// wire hitstop/shake to "the punctuation hit" without re-deriving <c>PulseLaser</c>'s own
        /// streak-of-4 rule.</summary>
        public static event Action<Vector3> ShockPulseLanded;

        /// <summary>A Shoulder Rack rocket detonated — a direct hit or an out-of-fuel ground impact,
        /// same "either way" shape as <see cref="MissileImpact"/> (MV-770). (worldPos, damage).</summary>
        public static event Action<Vector3, float> RocketImpact;

        /// <summary>One rocket left the tube (MV-770) — fired once per rocket in a staggered
        /// <c>ShoulderRack</c> salvo, not once per salvo, so "each with its own muzzle flash" (spec)
        /// falls out of firing this once per launch rather than needing a count parameter.
        /// (worldPos, forward)</summary>
        public static event Action<Vector3, Vector3> RocketMuzzle;

        /// <summary>The LPPE fired one pulse (MV-770) — fired once per <c>PulseLaser.FireTick</c>, the
        /// weapon-recoil hook (<c>MaxRig</c> kicks the gun back on this) rather than a HUD/VFX concern,
        /// same "one per shot" shape as <see cref="RocketMuzzle"/>. (worldPos, forward)</summary>
        public static event Action<Vector3, Vector3> LppePulseFired;

        /// <summary>Teleport's joystick started being aimed (MV-371) — (the ability's full blink
        /// distance at the current level, metres). The camera-zoom controller listens rather than
        /// taking a direct reference, so the joystick control doesn't have to know the camera zoom
        /// exists.</summary>
        public static event Action<float> TeleportAimStarted;

        /// <summary>Teleport's joystick aim ended — release (fired or aborted) or the control was
        /// disabled mid-aim.</summary>
        public static event Action TeleportAimEnded;

        /// <summary>A mobile shed (MV-548, shed roadmap stage 3) began its lift-off. (worldPos) — the
        /// hook a future VFX/audio pass hangs the dust-burst and rumble on, same decoupling as
        /// <see cref="FactoryDestroyed"/>; this greybox slice fires it and drives the body's own tint
        /// pulse directly, no particle system yet.</summary>
        public static event Action<Vector3> ShedLiftOff;

        /// <summary>A deployed Sentinel was recalled — a redeploy at the Slots cap freed its slot by
        /// recalling the furthest one instead of refusing (MV-604). (worldPos) — deliberately NOT
        /// <see cref="EnemyKilled"/>'s shape or any death signal: a recall is not a death, so listeners
        /// must give it its own despawn beat rather than reusing the kill/death VFX.</summary>
        public static event Action<Vector3> SentinelRecalled;

        /// <summary>A shed's corner weapon fitting (MV-547, shed roadmap stage 2) was destroyed —
        /// whether by its own health reaching zero or its shed going down and taking it with it.
        /// Deliberately NOT <see cref="EnemyKilled"/>: a fitting is authored structure hazard, not a
        /// robot, so it must never feed the run's kill stat or economy listeners
        /// <see cref="EnemyKilled"/> also drives — its own signal, its own small VFX beat, nothing else.</summary>
        public static event Action<Vector3> FittingDestroyed;

        /// <summary>MV-706: which word the bottom banner counter should use — true for "REPLICATORS",
        /// false for "FACTORIES". Fired once by <see cref="MaxWorlds.Arena.Map.MapRuntime.Build"/> after
        /// a level finishes building, since only the map — not the HUD, built earlier — knows whether
        /// this world's sources are sheds or Replicators.
        ///
        /// MV-741: <c>BackyardPath.Awake</c> calls <c>MapRuntime.Build</c> — which fires this — from ITS
        /// OWN <c>Awake</c>, and Unity runs every object's <c>Awake</c> before any object's <c>OnEnable</c>
        /// (where <c>HudController</c> subscribes). So this always fires before anything is listening,
        /// and a plain event alone would drop it. <see cref="LastWorldFactoryWording"/> latches the value
        /// so a subscriber that attaches after the fact can catch up instead of missing it — the bug this
        /// ticket fixes: World 1's default (false) happened to already match "FACTORIES", so nobody
        /// noticed the signal was never actually arriving until World 2 needed it to read true.</summary>
        public static event Action<bool> WorldFactoryWording;

        private static bool? _lastWorldFactoryWording;

        /// <summary>The value <see cref="WorldFactoryWording"/> most recently fired with, for a
        /// subscriber that attaches after the emit already happened (MV-741). Null before any level has
        /// ever built.</summary>
        public static bool? LastWorldFactoryWording => _lastWorldFactoryWording;

        /// <summary>MV-741: this world's Invasion Dial wording (<c>HudController.BuildInvasionDial</c>/
        /// <c>UpdateInvasionDial</c>) — a fixed noun for the current pressure and the permanent caption
        /// beneath it, read off <see cref="MaxWorlds.Arena.Map.MapData.pressureNoun"/>/
        /// <see cref="MaxWorlds.Arena.Map.MapData.pressureCaption"/>. Both empty means "not authored" —
        /// World 1's default three-band INVASION/INFESTATION/DOMINATION cycle and its caption. Fired at
        /// the same point, and subject to the exact same Awake-before-OnEnable race, as
        /// <see cref="WorldFactoryWording"/> above — see <see cref="LastPressureWording"/>.</summary>
        public static event Action<string, string> PressureWording;

        private static (string noun, string caption)? _lastPressureWording;

        /// <summary>The value <see cref="PressureWording"/> most recently fired with (MV-741) — same
        /// late-subscriber catch-up as <see cref="LastWorldFactoryWording"/>.</summary>
        public static (string noun, string caption)? LastPressureWording => _lastPressureWording;

        public static void EmitDamage(Vector3 worldPos, float amount, bool crit = false)
            => DamageDealt?.Invoke(worldPos, amount, crit);

        public static void EmitPlayerHit(Vector3 worldPos, Vector3 direction, bool isContact)
            => PlayerHit?.Invoke(worldPos, direction, isContact);

        public static void EmitPickup(Vector3 worldPos, string label, Color color)
            => Pickup?.Invoke(worldPos, label, color);

        public static void EmitSupercellCollected(Vector3 worldPos, int cellsBefore, int cellsAfter)
            => SupercellCollected?.Invoke(worldPos, cellsBefore, cellsAfter);

        public static void EmitEnemyKilled(Vector3 worldPos)
            => EnemyKilled?.Invoke(worldPos);

        public static void EmitFactoryRegistered()
            => FactoryRegistered?.Invoke();

        public static void EmitFactoryDestroyed(Vector3 worldPos)
            => FactoryDestroyed?.Invoke(worldPos);

        public static void EmitShedLiftOff(Vector3 worldPos)
            => ShedLiftOff?.Invoke(worldPos);

        public static void EmitSentinelRecalled(Vector3 worldPos)
            => SentinelRecalled?.Invoke(worldPos);

        public static void EmitFittingDestroyed(Vector3 worldPos)
            => FittingDestroyed?.Invoke(worldPos);

        public static void EmitWorldFactoryWording(bool isReplicatorWorld)
        {
            _lastWorldFactoryWording = isReplicatorWorld;
            WorldFactoryWording?.Invoke(isReplicatorWorld);
        }

        public static void EmitPressureWording(string noun, string caption)
        {
            _lastPressureWording = (noun ?? string.Empty, caption ?? string.Empty);
            PressureWording?.Invoke(noun ?? string.Empty, caption ?? string.Empty);
        }

        public static void EmitBossRegistered()
            => BossRegistered?.Invoke();

        public static void EmitBossEngaged(string name, int phases)
            => BossEngaged?.Invoke(name, phases);

        public static void EmitBossHealth(float normalized)
            => BossHealthChanged?.Invoke(normalized);

        public static void EmitBossSpawnLevel(int level, float progress01)
            => BossSpawnLevelChanged?.Invoke(level, progress01);

        public static void EmitBossDefeated()
            => BossDefeated?.Invoke();

        public static void EmitBossKilled(Vector3 worldPos)
            => BossKilled?.Invoke(worldPos);

        public static void EmitBossPayoffFinished()
            => BossPayoffFinished?.Invoke();

        public static void EmitRunComplete()
            => RunComplete?.Invoke();

        public static void EmitWeaponCoreDropped()
            => WeaponCoreDropped?.Invoke();

        public static void EmitWeaponCoreCollected()
            => WeaponCoreCollected?.Invoke();

        public static void EmitBlinkerTeleported(Vector3 from, Vector3 to)
            => BlinkerTeleported?.Invoke(from, to);

        public static void EmitMaxTeleported(Vector3 from, Vector3 to)
            => MaxTeleported?.Invoke(from, to);

        public static void EmitMissileImpact(Vector3 worldPos, float damage)
            => MissileImpact?.Invoke(worldPos, damage);

        public static void EmitMissileSputtering(Vector3 worldPos)
            => MissileSputtering?.Invoke(worldPos);

        public static void EmitMissileBounced(Vector3 worldPos)
            => MissileBounced?.Invoke(worldPos);

        public static void EmitShockPulseLanded(Vector3 worldPos)
            => ShockPulseLanded?.Invoke(worldPos);

        public static void EmitRocketImpact(Vector3 worldPos, float damage)
            => RocketImpact?.Invoke(worldPos, damage);

        public static void EmitRocketMuzzle(Vector3 worldPos, Vector3 forward)
            => RocketMuzzle?.Invoke(worldPos, forward);

        public static void EmitLppePulseFired(Vector3 worldPos, Vector3 forward)
            => LppePulseFired?.Invoke(worldPos, forward);

        public static void EmitTeleportAimStarted(float maxRangeMetres)
            => TeleportAimStarted?.Invoke(maxRangeMetres);

        public static void EmitTeleportAimEnded()
            => TeleportAimEnded?.Invoke();
    }
}
