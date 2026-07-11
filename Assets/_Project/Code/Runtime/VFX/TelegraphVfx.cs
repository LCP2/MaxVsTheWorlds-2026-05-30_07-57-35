using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Bosses;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Attack telegraphs (YT-53): a ground indicator under every enemy that is winding up, and
    /// under every boss AoE that is arming.
    ///
    /// The gameplay already has the timing — <see cref="RobotEnemy"/> holds a 0.55s Telegraph state
    /// (the dodge window) and <see cref="DamageZone"/> has an arm delay before it bites. What was
    /// missing was any way to SEE it: the existing cue is a colour tint on the robot's body, and at
    /// the fixed ~72° camera with 20–30 enemies on screen a small body tint is invisible. This draws
    /// the same information on the ground, where the player is already looking.
    ///
    /// Reads state, never writes it. No AI, no timings, no damage values are touched — deleting this
    /// file changes nothing but what you can see coming.
    ///
    /// Perf: rings are pooled and re-used frame to frame, share one material, and are driven through
    /// a MaterialPropertyBlock. A crowd of telegraphing enemies costs a handful of quads.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TelegraphVfx : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<TelegraphVfx>() != null) return;
            new GameObject("TelegraphVFX").AddComponent<TelegraphVfx>();
        }

        [Tooltip("Radius of the enemy wind-up indicator, world units.")]
        [SerializeField] private float enemyRingRadius = 1.15f;
        [SerializeField] private Color warnColor = new Color(1f, 0.27f, 0.16f, 1f);
        [SerializeField] private Color armedColor = new Color(1f, 0.85f, 0.35f, 1f);

        private readonly List<GroundRing> _pool = new List<GroundRing>(32);
        private int _used;

        private void LateUpdate()
        {
            _used = 0;

            DrawEnemyWindups();
            DrawArmingZones();

            // Retire the rings nobody claimed this frame.
            for (int i = _used; i < _pool.Count; i++) _pool[i].Hide();
        }

        private void DrawEnemyWindups()
        {
            foreach (var enemy in FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive) continue;
                float p = enemy.TelegraphProgress;
                if (p <= 0f) continue;

                // The ring tightens and brightens as the strike approaches: the shrinking radius is
                // the clock. It reads as "this is closing on me" without needing a number.
                float radius = Mathf.Lerp(enemyRingRadius, enemyRingRadius * 0.55f, p);
                Color c = Color.Lerp(warnColor, armedColor, p);
                c.a = Mathf.Lerp(0.35f, 0.95f, p);

                Next().Show(Ground(enemy.transform.position), radius, c);
            }
        }

        private void DrawArmingZones()
        {
            foreach (var zone in FindObjectsByType<DamageZone>(FindObjectsSortMode.None))
            {
                if (!zone.IsArming) continue;

                // The boss's AoE keeps its own radius — the indicator has to match the real danger
                // area exactly, or it teaches the player the wrong thing.
                float p = zone.ArmProgress;
                Color c = Color.Lerp(warnColor, armedColor, p);
                c.a = Mathf.Lerp(0.3f, 0.9f, p);

                Next().Show(Ground(zone.transform.position), zone.Radius, c);
            }
        }

        private static Vector3 Ground(Vector3 p) => new Vector3(p.x, 0f, p.z);

        private GroundRing Next()
        {
            if (_used < _pool.Count) return _pool[_used++];

            var ring = GroundRing.Create("TelegraphRing");
            ring.transform.SetParent(transform, worldPositionStays: false);
            _pool.Add(ring);
            _used++;
            return ring;
        }
    }
}
