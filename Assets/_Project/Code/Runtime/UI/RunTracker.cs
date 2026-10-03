using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Save;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Watches a Backyard run and shows the Result screen when the boss falls (YT-31). Accumulates
    /// the slice stats — run time, robots killed, factory destroyed — off <see cref="HudSignals"/>,
    /// then hands them to a <see cref="ResultScreen"/> once Victory is sealed. Code-driven and
    /// self-wiring, so it runs headlessly and on the WebGL build with no editor setup.
    ///
    /// MV-427: a death no longer seals a Defeat outcome here — Max dying now continues the run
    /// (<see cref="WorldRunner"/> handles the respawn), so the only way this run ever ends is Victory.
    ///
    /// YT-152 — the win doesn't cut straight to the card. Beating a boss <em>starts</em> the payoff (the
    /// blow-up, the flung parts, the walk-out to the exit gate), signalled by
    /// <see cref="HudSignals.BossPayoffFinished"/> from <c>BossVictoryPayoff</c> when Max steps through
    /// the gate, or when that sequence times out.
    ///
    /// MV-591 — a boss falling is no longer what ends the run. World 1 v4 authors bosses mid-run (a12,
    /// a20) as well as the final one (a30), so a boss's own payoff finishing is necessary but not
    /// sufficient on its own — the existing loot-and-walk-to-the-door beat plays out exactly as before
    /// for every boss, mid-run or final; it just no longer ends the game by itself.
    ///
    /// MV-956/MV-1013: a run's own finale — EVERY world's, including the last — drops a Weapon Core and
    /// seals on collecting it and crossing <c>WorldFinaleGate</c> once it opens, never on
    /// <see cref="HudSignals.RunComplete"/>, which required every robot in the final area dead, not just
    /// its boss(es). A boss dying anywhere else (a12, a20 mid-run) plays the same payoff beat but never
    /// seals Victory on its own — see <see cref="TrySeal"/>.
    /// </summary>
    [MaxWorlds.Core.PerfSection("hud")]
    public sealed class RunTracker : MonoBehaviour
    {
        /// <summary>MV-698: how long a dropped Weapon Core waits for a walk-over collect before it is
        /// auto-collected — the soft-lock guard the ticket asks for, so a player who never notices (or
        /// never can reach) the drop can't stall the world's finale forever.</summary>
        private const float WeaponCoreAutoCollectTimeout = 60f;

        private readonly RunStats _stats = new RunStats();

        private bool _sealed;              // outcome decided; the clock stops and nothing may override it
        private bool _shown;              // the result card has been built

        // MV-591/MV-1013: victory needs BOTH the boss payoff beat AND the finale's own weapon-core+gate
        // latch below — a boss dying anywhere else (a12, a20) sets _payoffFinished but never
        // _weaponCoreAwaited, so TrySeal never seals on it alone.
        private bool _payoffFinished;

        // MV-698: a SECOND condition, live only for a run whose finale actually dropped a Weapon Core —
        // a mid-run boss's own _weaponCoreAwaited stays false and this gate is a no-op.
        private bool _weaponCoreAwaited;
        private bool _weaponCoreCollected;
        private float _weaponCoreDropRealtime;

        // MV-915: a THIRD condition, riding the same "live only for the run that actually dropped a
        // Weapon Core" latch as the second — _payoffFinished (BossVictoryPayoff's own walk-out beat) is
        // scoped to whichever boss area clears FIRST in the whole run (a12, mid-run), so by the time
        // World 1's real finale (a30) empties, _payoffFinished is already long since true. WorldFinaleGate
        // only ever exists for the world's actual last boss area (MV-956: it opens on that area's own
        // BossDefeated, the same event the Weapon Core drops on), so gating on it here is what makes
        // "walk through the open gate" a real, necessary beat for the finale specifically, without
        // touching a12/a20's own (unrelated) payoff timing at all.
        private bool _finaleGateAwaited;
        private bool _finaleGateCrossed;

        private void OnEnable()
        {
            // MV-841: seed from whatever the checkpoint bridge already holds — 0 for a genuinely
            // fresh world/run, or a resumed session's whole-world tally-so-far if this RunTracker was
            // (re)created after a checkpoint had already been restored (e.g. a scene reload).
            _stats.Restore(RunProgressState.Elapsed, RunProgressState.Kills);
            RunProgressState.Restored += OnProgressRestored;

            HudSignals.EnemyKilled += OnKill;
            HudSignals.FactoryDestroyed += OnFactory;
            FactoryCensus.CheckpointRestored += OnCheckpointRestored;
            HudSignals.BossPayoffFinished += OnBossPayoffFinished;
            HudSignals.WeaponCoreDropped += OnWeaponCoreDropped;
            HudSignals.WeaponCoreCollected += OnWeaponCoreCollected;
            HudSignals.FinaleGateCrossed += OnFinaleGateCrossed;
        }

        private void OnDisable()
        {
            RunProgressState.Restored -= OnProgressRestored;

            HudSignals.EnemyKilled -= OnKill;
            HudSignals.FactoryDestroyed -= OnFactory;
            FactoryCensus.CheckpointRestored -= OnCheckpointRestored;
            HudSignals.BossPayoffFinished -= OnBossPayoffFinished;
            HudSignals.WeaponCoreDropped -= OnWeaponCoreDropped;
            HudSignals.WeaponCoreCollected -= OnWeaponCoreCollected;
            HudSignals.FinaleGateCrossed -= OnFinaleGateCrossed;
        }

        // MV-841: a checkpoint restore landed while this RunTracker was already alive — a RESUME tap
        // never reloads the scene, so nothing else would otherwise carry the restored elapsed
        // time/kill count back into this instance's own RunStats.
        private void OnProgressRestored(float elapsed, int kills) => _stats.Restore(elapsed, kills);

        private void Update()
        {
            if (_sealed) return;

            _stats.Tick(Time.deltaTime);
            RunProgressState.Sync(_stats.Elapsed, _stats.Kills);
            _stats.RecordDifficultyPeak(DifficultyDirector.Normalized);

            // MV-698: a dropped core nobody ever walks over must not stall Victory forever.
            if (_weaponCoreAwaited && !_weaponCoreCollected
                && Time.unscaledTime - _weaponCoreDropRealtime >= WeaponCoreAutoCollectTimeout)
            {
                MaxWorlds.Weapons.PendingMorphingModule.SetWeaponCore();
                HudSignals.EmitWeaponCoreCollected();
            }
        }

        private void OnKill(Vector3 _)
        {
            _stats.AddKill();
            RunProgressState.Sync(_stats.Elapsed, _stats.Kills);
        }
        private void OnFactory(Vector3 _) => _stats.MarkFactoryDestroyed();

        /// <summary>MV-950: a checkpoint restore just moved every already-destroyed shed/Replicator
        /// out of <see cref="FactoryCensus"/>'s standing lists — catch the Result screen's tally up
        /// to that count directly, never via <see cref="OnFactory"/> (which would replay loot/VFX).</summary>
        private void OnCheckpointRestored() =>
            _stats.RestoreFactoriesDestroyed(FactoryCensus.Destroyed + FactoryCensus.ReplicatorsDestroyed);

        // MV-698: a Weapon Core landed on the ground — Victory must wait for it (walk-over or the
        // auto-collect timeout above), the same "necessary but not sufficient on its own" shape
        // _payoffFinished already has.
        private void OnWeaponCoreDropped()
        {
            _weaponCoreAwaited = true;
            _weaponCoreCollected = false;
            _weaponCoreDropRealtime = Time.unscaledTime;
            _finaleGateAwaited = true;   // MV-915: this run is the real finale — it must be walked out of
        }

        // MV-915: Max crossed WorldFinaleGate once it opened — the other half of the finale's own seal
        // condition, alongside _weaponCoreCollected above.
        private void OnFinaleGateCrossed()
        {
            _finaleGateCrossed = true;
            TrySeal();
        }

        // MV-698: the awaited core was collected — walk-over (PickupDirector.Collect) or the timeout
        // above, either way this is the one place that clears the wait and re-attempts the seal.
        private void OnWeaponCoreCollected()
        {
            _weaponCoreCollected = true;
            TrySeal();
        }

        // A boss's payoff beat played out (Max reached the gate, or it timed out) — one half of the
        // seal condition. Fires for every boss, mid-run or final; only matters once TrySeal's other
        // condition (the finale's own core+gate) agrees.
        private void OnBossPayoffFinished()
        {
            _payoffFinished = true;
            TrySeal();
        }

        /// <summary>MV-591: the payoff beat alone used to seal the run, so any boss dying anywhere
        /// ended the game. Victory now needs the run's own finale AS WELL, not just any payoff beat
        /// finishing. MV-698 adds that finale's own condition: if this run's finale dropped a Weapon
        /// Core, it must be collected too (walk-over or the auto-collect timeout) before Victory seals.
        /// MV-915 adds a second, riding the same latch: that same run must also have crossed
        /// <c>WorldFinaleGate</c> once it opened — <see cref="_payoffFinished"/> is scoped to whichever
        /// boss area clears FIRST in the run (a12, mid-run) and is long since true by the time the real
        /// finale (a30) empties, so without this the results card would cut in the instant the last
        /// robot dies, with no walk-out of its own for the world's actual finale.
        ///
        /// MV-1013: <see cref="_weaponCoreAwaited"/> is now the ONLY other condition, for every world
        /// including the last — a boss dying anywhere that is NOT the world's own final boss area never
        /// sets it, so <c>TrySeal</c> never seals on a mid-run payoff (a12, a20) alone; a boss dying that
        /// IS the final boss area always drops a core and opens the finale door (MV-698/MV-956's own
        /// <c>BossDefeated</c>-driven latch), so there is no longer a "no next world" case that falls
        /// back to counting every robot in the level dead (<see cref="HudSignals.RunComplete"/>) — that
        /// was the World 3 bug this ticket fixes.</summary>
        private void TrySeal()
        {
            if (_sealed || !_payoffFinished) return;
            if (!_weaponCoreAwaited) return;
            if (!_weaponCoreCollected) return;
            if (_finaleGateAwaited && !_finaleGateCrossed) return;

            Seal(RunOutcome.Victory);
            ShowResults();
        }

        private void Seal(RunOutcome outcome)
        {
            if (_sealed) return;
            _sealed = true;
            _stats.Finish(outcome);

            // MV-687: capture whether this victory WILL advance the world before RecordResult actually
            // does it — the Result screen needs to know whether there's a next world to name its CTA
            // correctly. MV-921: "is there a next world" is decided by the world actually being PLAYED
            // (AreaAccumulationDirector.ActiveWorldIndex, the same active-world source ActiveWorldConfig
            // already resolves against), not a fresh SaveSlotData.WorldIndex read — a save can already
            // have gone further than the world this run replayed, and that must not read as "no further
            // worlds". playedWorldIndex is also what RecordResult below advances relative to, so a
            // replay offers the world right after the one just played, never regressing to whatever the
            // save's own WorldIndex already was.
            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            int playedWorldIndex = areaDirector != null ? areaDirector.ActiveWorldIndex : 0;
            bool advancesWorld = SaveSystem.ActiveSlot >= 0 && playedWorldIndex < WorldLibrary.Count - 1;
            _stats.SetAdvancesWorld(advancesWorld);

            // MV-698: this run's finale granted a Weapon Core (already collected by now — TrySeal
            // wouldn't have let a still-awaited one reach here) — the Result screen's cue to show the
            // "NEW WEAPON UNLOCKED" line (MV-1075).
            _stats.SetWeaponCoreGranted(_weaponCoreAwaited);

            // MV-427: deaths taken is the new personal-best discriminator — the peak-Domination %
            // stopped meaning anything once a death no longer ends the run (every player eventually
            // reaches 100%). Still only banked on a run that actually finishes (Victory); bailing out
            // early via the HOME button records nothing (YT-218).
            SaveSystem.RecordResult(SaveSystem.ActiveSlot, DeathRunState.DeathsTaken, playedWorldIndex);
        }

        private void ShowResults()
        {
            if (_shown) return;
            _shown = true;

            var go = new GameObject("Result Screen");
            go.AddComponent<ResultScreen>().Show(_stats);
        }
    }
}
