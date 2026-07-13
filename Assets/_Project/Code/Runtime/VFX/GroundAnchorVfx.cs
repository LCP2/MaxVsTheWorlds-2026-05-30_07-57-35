using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The always-on ground-anchor layer (YT-85): a coloured ring and a contact shadow under every
    /// living actor, every frame.
    ///
    /// Two jobs, and they are not the same job. The RING is figure-ground — it separates an actor
    /// from the lawn and says whose side it is on, which is how you find yourself in a crowd without
    /// hunting. The SHADOW is physicality — the sun already casts real shadows (YT-76), but at a 40°
    /// key they rake off sideways, and a shadow that isn't under a thing doesn't anchor it; the eye
    /// reads the actor as hovering. A small dark contact patch directly beneath the feet is the cue
    /// that puts it on the floor. Brawl Stars runs both for exactly this reason, and it matters more
    /// now than it did last week, because YT-82 pulled the camera back and every actor got ~18%
    /// smaller.
    ///
    /// Reads state, never writes it. No AI, no damage, no timings are touched — delete this file and
    /// the game plays identically, it just becomes harder to look at.
    ///
    /// WHY A CENTRAL DIRECTOR, and not a component on each actor — two traps, both already sprung in
    /// this repo once:
    ///   * <see cref="CharacterSkinDirector"/> sweeps every MeshRenderer under an
    ///     <see cref="IDamageable"/> each frame and repaints it. A ring parented to Max or a robot
    ///     would have its material quietly overwritten with the character material.
    ///   * A robot's root transform IS its scaled body (a rusher is 0.8). Anything childed to it
    ///     inherits that scale — which is YT-74, the bug where robots inherited the factory's scale
    ///     and became walls.
    /// Rings owned by this director are parented to the director, so neither can reach them.
    ///
    /// Perf: rings are pooled and re-used frame to frame, share one material per texture, and are
    /// tinted through a MaterialPropertyBlock — a full arena of 30 actors is 60 quads and two draw
    /// setups, which is the same bargain <see cref="TelegraphVfx"/> already makes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundAnchorVfx : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<GroundAnchorVfx>() != null) return;
            new GameObject("GroundAnchorVFX").AddComponent<GroundAnchorVfx>();
        }

        private readonly List<GroundRing> _shadows = new List<GroundRing>(32);
        private readonly List<GroundRing> _rings = new List<GroundRing>(32);
        private int _usedShadows;
        private int _usedRings;

        private void LateUpdate()
        {
            _usedShadows = 0;
            _usedRings = 0;

            // One rule for every actor there is or will be: it has a CharacterController (that's
            // what makes it a thing that walks) and an IDamageable (that's what makes it a fighter).
            // Enumerating concrete types instead — robot, boss, Max — is how the next actor gets
            // added and silently ships with no shadow. Team decides the colour, so a new hostile is
            // orange the day it exists, without anyone remembering to come back here.
            foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            {
                if (!cc.TryGetComponent<IDamageable>(out var actor) || !actor.IsAlive) continue;

                float footprint = GroundAnchorTuning.FootprintRadius(cc);
                if (footprint <= 0f) continue;

                Vector3 ground = Ground(cc.transform.position);
                bool isPlayer = actor.Team == Team.Player;

                NextShadow().Show(ground,
                    footprint * GroundAnchorTuning.ShadowRadiusScale,
                    GroundAnchorTuning.ContactShadow);

                NextRing().Show(ground,
                    footprint * GroundAnchorTuning.RingRadiusScale,
                    isPlayer ? GroundAnchorTuning.PlayerRing : GroundAnchorTuning.EnemyRing);
            }

            // Retire whatever nobody claimed. A robot that died this frame is already deactivated,
            // so it claims nothing and its anchors go out with it — which is the whole reason the
            // pool is re-walked from zero every frame rather than tracked per actor.
            for (int i = _usedShadows; i < _shadows.Count; i++) _shadows[i].Hide();
            for (int i = _usedRings; i < _rings.Count; i++) _rings[i].Hide();
        }

        /// <summary>Flatten to the lawn. Actors' origins sit at different heights — Max's is his
        /// capsule's centre, a robot's is half its collider — and a ground mark that inherited that
        /// would float at a different height under each one.</summary>
        private static Vector3 Ground(Vector3 p) => new Vector3(p.x, 0f, p.z);

        private GroundRing NextShadow()
        {
            if (_usedShadows < _shadows.Count) return _shadows[_usedShadows++];

            var s = GroundRing.Create("ContactShadow", VfxMaterials.Glow());
            s.Lift = GroundAnchorTuning.ShadowLift;
            s.transform.SetParent(transform, worldPositionStays: false);
            _shadows.Add(s);
            _usedShadows++;
            return s;
        }

        private GroundRing NextRing()
        {
            if (_usedRings < _rings.Count) return _rings[_usedRings++];

            var r = GroundRing.Create("AnchorRing", VfxMaterials.Annulus());
            r.Lift = GroundAnchorTuning.RingLift;
            r.transform.SetParent(transform, worldPositionStays: false);
            _rings.Add(r);
            _usedRings++;
            return r;
        }
    }
}
