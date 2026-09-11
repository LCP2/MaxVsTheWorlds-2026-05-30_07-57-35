using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// A built grate's own short vertical shudder as its Grate Lurker rises through it (MV-773, change
    /// 3) — the physical answer to <see cref="MaxWorlds.VFX.TelegraphVfx"/>'s RATTLE ring, which already
    /// tells the player something is about to happen at this spot but never moves the grate itself.
    ///
    /// Timing is inlined here rather than through <c>MaxWorlds.Feel.AnimSequence</c> — that type lives
    /// in the Gameplay assembly, which itself references THIS one (<see cref="StormdrainKit"/>), so
    /// depending on it back would be circular. The eased rise-and-settle curve is the same shape
    /// (<see cref="Mathf.Sin"/> over a clamped 0..1 ramp); this MonoBehaviour owns applying it to the
    /// bars <see cref="StormdrainKit.BuildGrate"/> built it with, ticked by ordinary <see cref="Update"/>
    /// — no Animator, per the ticket's own instruction.
    /// </summary>
    public sealed class GrateShudder : MonoBehaviour
    {
        private const float Duration = 0.35f;
        private const float Amplitude = 0.03f;

        private Transform[] _bars;
        private float[] _barBaseY;
        private float _timer = -1f; // negative: not running

        /// <summary>Whether a shudder is currently mid-flight — read-only, for a caller (or a test) that
        /// needs to know without reaching into the timer itself.</summary>
        public bool IsShuddering => _timer >= 0f;

        public void Configure(Transform[] bars)
        {
            _bars = bars;
            _barBaseY = new float[bars.Length];
            for (int i = 0; i < bars.Length; i++) _barBaseY[i] = bars[i].localPosition.y;
        }

        /// <summary>Starts (or restarts) the shudder — called once per RATTLE.</summary>
        public void Trigger() => _timer = 0f;

        private void Update()
        {
            if (_timer < 0f) return;

            _timer += Time.deltaTime;
            Apply(_timer / Duration);
            if (_timer >= Duration) _timer = -1f;
        }

        /// <summary>Writes eased progress <paramref name="p"/> onto every bar's local Y, offset above its
        /// own resting height and back down — separated from <see cref="Update"/> so a test can drive it
        /// directly without a live Unity time step.</summary>
        public void Apply(float p)
        {
            float offset = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI) * Amplitude;
            for (int i = 0; i < _bars.Length; i++)
            {
                Vector3 pos = _bars[i].localPosition;
                pos.y = _barBaseY[i] + offset;
                _bars[i].localPosition = pos;
            }
        }

        // ---------------------------------------------------------------- registry

        /// <summary>Every grate built this level, keyed by its own authored MIN corner (MV-773) — the
        /// same tile-containment convention <see cref="MaxWorlds.Arena.MapValidation"/>'s
        /// WorldLurkerGrates rule already validates a garrisoned Lurker against, so a Lurker that
        /// validated onto its grate at author time always finds that same grate here at runtime.</summary>
        private static readonly List<(Vector2 corner, GrateShudder shudder)> _live = new(32);

        public static void Register(Vector2 corner, GrateShudder shudder) => _live.Add((corner, shudder));

        /// <summary>Clears every registered grate — same "a fresh map owns nothing left over from the
        /// last one" contract every other per-level registry in <c>MapRuntime.Build</c> already
        /// carries.</summary>
        public static void ClearRegistry() => _live.Clear();

        /// <summary>Triggers whichever registered grate's 1x1 tile contains <paramref name="worldPos"/>'s
        /// XZ, if any — a Lurker's own submerged position sits somewhere on its grate's tile (its origin
        /// or its centre, per MV-724), never off it.</summary>
        public static void TriggerNear(Vector3 worldPos)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                Vector2 c = _live[i].corner;
                bool inX = worldPos.x >= c.x - 0.01f && worldPos.x <= c.x + 1f + 0.01f;
                bool inZ = worldPos.z >= c.y - 0.01f && worldPos.z <= c.y + 1f + 0.01f;
                if (inX && inZ) { _live[i].shudder.Trigger(); return; }
            }
        }
    }
}
