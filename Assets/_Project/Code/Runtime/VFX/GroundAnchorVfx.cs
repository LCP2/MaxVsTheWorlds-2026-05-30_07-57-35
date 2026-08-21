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
    ///
    /// DISCOVERY (MV-532): this used to be a per-frame <c>FindObjectsByType&lt;CharacterController&gt;</c>
    /// scan — a fresh, GC-allocating scene-wide array every frame, the MV-527 regression shape. MV-527
    /// itself evaluated and reverted a registry keyed to the three known actor types (Max, the boss,
    /// <c>RobotEnemy</c>) after finding that <c>GroundAnchorPlayTests.cs</c> proves a genuinely
    /// type-agnostic contract — a synthetic <c>FakeActor</c>, wired to nothing, still gets anchored.
    /// A self-registering component would reinstate exactly that trap: it has to be added at every
    /// actor's construction site, so <c>FakeActor</c> — which registers nothing and cannot be edited to
    /// (MV-532 forbids touching that test) — would silently stop being anchored, which is precisely the
    /// "next actor type ships with no shadow" failure this system exists to prevent.
    ///
    /// So discovery moved to <see cref="Physics.OverlapSphereNonAlloc"/> against a reused static buffer
    /// — the exact non-allocating pattern <see cref="MaxWorlds.Combat.WaterBlaster"/>,
    /// <see cref="MaxWorlds.Arena.Sentinel"/> and <see cref="MaxWorlds.Weapons.PlayerAbilities"/> already
    /// use to find <see cref="IDamageable"/> targets, and already proven in this codebase to report a
    /// <c>CharacterController</c> (that comment thread notes a greybox robot's <c>CharacterController</c>
    /// and its primitive collider are reported as two
    /// separate hits). It costs one array walk of whatever overlaps a generous sphere, not a scan of
    /// every loaded object of the type, and it needs zero wiring at any actor's creation site — any
    /// <c>CharacterController</c> + <see cref="IDamageable"/>, anywhere, is found the same way FindObjectsByType
    /// found it, which is what keeps <c>FakeActor</c> and AC3's hypothetical new actor type working.
    ///
    /// The trade: <see cref="ScanRadius"/> and <see cref="s_hits"/>'s length bound what this can see,
    /// where <c>FindObjectsByType</c> had no such bound. Phase B is one Backyard sub-zone path — both
    /// constants carry generous headroom over that scale. If a later phase's world genuinely exceeds a
    /// 1 km span or a couple hundred colliders live inside it at once, widen them here rather than
    /// re-adding a scene-wide scan.
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

        /// <summary>Comfortably beyond a single Backyard sub-zone path; see the class doc comment.</summary>
        private const float ScanRadius = 1000f;

        // Reused every frame, never reallocated — the buffer PlayerAbilities/Sentinel/WaterBlaster
        // already reuse for the same kind of OverlapSphereNonAlloc call. Sized well past the
        // documented ~30-actor arena to leave room for walls/cover/props sharing the query.
        private static readonly Collider[] s_hits = new Collider[256];

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
            int count = Physics.OverlapSphereNonAlloc(
                Vector3.zero, ScanRadius, s_hits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (!(s_hits[i] is CharacterController cc)) continue;
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
