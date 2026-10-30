using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A repair bay (MV-1319) — a player-triggered, one-shot full heal: Max standing inside the ring
    /// while READY heals him to full HP and tops the Force Field's absorb budget back to its current cap
    /// (never charges a field that isn't already up — ticket: "refill only what the existing force field
    /// state supports"), plays a green upward pulse and the pickup-style "REPAIRED" flyup, then goes dark
    /// for good. Same "one attempt, saved used" shape as <see cref="ChallengePad"/> (MV-1318), but with no
    /// wave/reward machinery — a bay has exactly one thing to do and does it on contact.
    /// </summary>
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class RepairBay : MonoBehaviour
    {
        public enum RepairState { Ready, Used }

        // --- Ticket's own numbers ---
        private const float BayRadius = 2f;                  // "radius 2.0 m"
        private const float IconHeight = 2f;                  // "2 m above it"
        private const float IconDiameter = 0.8f;               // ">= 32 px" at 48 px/m (docs/DECISIONS.md), same constant ChallengePad uses
        private const float OuterRingRadiusFraction = 1f;
        private const float InnerRingRadiusFraction = 0.65f;   // the image's double-ring look
        private const float PulseLifeSeconds = 0.3f;           // "a green upward pulse (>= 10 frames)" — 18 frames at 60fps
        private const float PulseWidth = 1.2f;
        private const float PulseHeight = 2.5f;

        private static readonly Color ReadyColor = new Color(0.22f, 0.92f, 0.42f);  // green, matches the design image
        private static readonly Color UsedColor = new Color(0.22f, 0.22f, 0.24f);    // dark grey, same as ChallengePad.UsedColor

        private GroundRing _ringOuter;
        private GroundRing _ringInner;
        private CameraFacingFlare _icon;

        private RepairState _state = RepairState.Ready;
        private Transform _player;

        /// <summary>This bay's stable id (MV-1319) — same "a checkpoint can persist/restore by id"
        /// reasoning as <see cref="ChallengePad.Id"/>.</summary>
        public string Id { get; private set; } = string.Empty;

        public void SetId(string id) => Id = id ?? string.Empty;

        /// <summary>Post-build readout, never an authored constant — same "Tier 2" idiom as
        /// <see cref="ChallengePad.State"/>.</summary>
        public RepairState State => _state;

        // --- Static registry — same "the map builds them, Build() registers them, a fresh level wipes
        // them" shape as ChallengePad.Registered. ---
        private static readonly List<RepairBay> Registered = new List<RepairBay>(4);

        public static void ResetRegistry() => Registered.Clear();

        /// <summary>The stable ids of every registered bay already USED this run — what a mid-run
        /// checkpoint capture persists. Same shape as <see cref="ChallengePad.WonIds"/>.</summary>
        public static string[] UsedIds()
        {
            var ids = new List<string>();
            foreach (RepairBay b in Registered)
                if (b != null && b._state == RepairState.Used && !string.IsNullOrEmpty(b.Id)) ids.Add(b.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded USED set onto whichever currently-
        /// registered, still-READY bays carry a matching <see cref="Id"/> — called by
        /// <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME. Same shape as
        /// <see cref="ChallengePad.ApplyCheckpointWonIds"/>. A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointUsedIds(IReadOnlyList<string> usedIds)
        {
            if (usedIds == null || usedIds.Count == 0) return;

            foreach (RepairBay b in Registered)
            {
                if (b == null || b._state != RepairState.Ready || string.IsNullOrEmpty(b.Id)) continue;
                for (int i = 0; i < usedIds.Count; i++)
                {
                    if (usedIds[i] != b.Id) continue;
                    b._state = RepairState.Used;
                    b.UpdateVisual();
                    break;
                }
            }
        }

        private void Awake() => Build();

        /// <summary>Construct the greybox ring/icon visuals. Exposed publicly, same reasoning as
        /// <see cref="ChallengePad.Build"/> — <c>Awake</c> never runs as a side effect of
        /// <c>AddComponent</c> outside Play mode, so an EditMode test calls this directly.</summary>
        public void Build()
        {
            if (_ringOuter != null) return; // already built

            _ringOuter = GroundRing.Create($"{name} Ring Outer");
            _ringInner = GroundRing.Create($"{name} Ring Inner");
            _icon = CameraFacingFlare.Create($"{name} Icon");

            if (!Registered.Contains(this)) Registered.Add(this);
            UpdateVisual();
        }

        private void OnDestroy() => Registered.Remove(this);

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The per-frame step, pulled out of <see cref="Update"/> with an explicit <paramref name="dt"/>
        /// — same "Tick(dt), not a bare Update()" shape as <see cref="ChallengePad.Tick"/>, so an EditMode
        /// test can drive this with no live Unity frame loop. <paramref name="dt"/> is unused today (the
        /// repair itself is instantaneous), kept for the same signature/shape as every other factory's
        /// tick.</summary>
        public void Tick(float dt)
        {
            if (_state != RepairState.Ready) return;

            Transform player = ResolvePlayer();
            if (player == null) return;

            float d = Vector3.Distance(Flat(player.position), Flat(transform.position));
            if (d <= BayRadius) Repair(player);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private Transform ResolvePlayer()
        {
            if (_player != null) return _player;
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            _player = p != null ? p.transform : null;
            return _player;
        }

        private void Repair(Transform player)
        {
            _state = RepairState.Used;

            PlayerHealth health = player.GetComponentInParent<PlayerHealth>();
            health?.Revive(); // full heal — same shape as a fresh Awake (PlayerHealth.Revive's own doc comment)

            PlayerAbilities abilities = player.GetComponentInParent<PlayerAbilities>();
            abilities?.RefillForceField();

            PlayPulse();
            HudSignals.EmitPickup(IconCenter, "REPAIRED", ReadyColor);

            UpdateVisual();
        }

        private void PlayPulse()
        {
            LightPillar pillar = LightPillar.Create($"{name} Pulse");
            pillar.Show(transform.position, PulseWidth, PulseHeight, ReadyColor);
            pillar.gameObject.AddComponent<PulseLifetimeVfx>().Begin(pillar, PulseLifeSeconds);
        }

        // ---------------------------------------------------------------- Visual (ticket Change 2)

        private void UpdateVisual()
        {
            switch (_state)
            {
                case RepairState.Ready:
                    ShowRings(ReadyColor);
                    _icon.Show(IconCenter, IconDiameter, ReadyColor);
                    break;
                case RepairState.Used:
                    ShowRings(UsedColor);
                    _icon.Hide();
                    break;
            }
        }

        private Vector3 IconCenter => transform.position + Vector3.up * IconHeight;

        private void ShowRings(Color c)
        {
            _ringOuter.Show(transform.position, BayRadius * OuterRingRadiusFraction, c);
            _ringInner.Show(transform.position, BayRadius * InnerRingRadiusFraction, c);
        }

        /// <summary>Grows/fades an upward column of green light then destroys its own GameObject —
        /// self-contained, same shape as <see cref="ChallengePad"/>'s own <c>BeamLifetimeVfx</c>.</summary>
        [MaxWorlds.Core.PerfSection("factories")]
        private sealed class PulseLifetimeVfx : MonoBehaviour
        {
            private LightPillar _pillar;
            private float _life;
            private float _t;

            public void Begin(LightPillar pillar, float life)
            {
                _pillar = pillar;
                _life = life;
            }

            private void Update()
            {
                _t += Time.deltaTime;
                if (_t >= _life)
                {
                    if (_pillar != null) _pillar.Hide();
                    Destroy(gameObject);
                }
            }
        }
    }
}
