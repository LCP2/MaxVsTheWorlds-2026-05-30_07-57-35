using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.VFX
{
    /// <summary>The materials a built <see cref="ShedTurretRig"/> wears — caller-owned, the same
    /// "palette in, no colour decided here" shape as <see cref="RobotPalette"/>, so this file never
    /// has to know which world, shed or fitting kind it is dressing.</summary>
    public readonly struct ShedTurretPalette
    {
        /// <summary>The fixed base ring the whole assembly turns on.</summary>
        public readonly Material Base;
        /// <summary>The dome head that yaws to face the current target.</summary>
        public readonly Material Dome;
        /// <summary>The barrel/launch-pod housing — dark for Spiker/Laser, the same red as
        /// <see cref="Dome"/> for Missile's launch pods (the caller decides which tone to hand in).</summary>
        public readonly Material Barrel;
        /// <summary>The bright always-on glow accent: a muzzle collar, an emitter lens, a pod tip.</summary>
        public readonly Material Glow;

        public ShedTurretPalette(Material baseMaterial, Material dome, Material barrel, Material glow)
        {
            Base = baseMaterial; Dome = dome; Barrel = barrel; Glow = glow;
        }
    }

    /// <summary>
    /// A shed corner turret (MV-1058) — a fixed base ring, a yawing red dome, and a kind-specific
    /// barrel, built from <see cref="CharacterMeshes"/> exactly the way <see cref="RobotBodies"/>
    /// builds a robot, never from a <c>GameObject.CreatePrimitive</c> primitive. Replaces the old
    /// per-kind split (<c>MissileLauncherRig</c> for Missile only, a bare tinted cube for Spiker and
    /// Laser) that left most shed turrets reading as "plain cubes sitting on the corners" (Lee, live
    /// build, 2026-10-01). Deliberately generic — this file has no reference to sheds,
    /// <c>MowerHutch</c> or <c>ShedFitting</c> — so a future caller (a World 2 Replicator, a World 3
    /// structure) can build one in one line: <c>ShedTurretRig.Build(root, kind, palette)</c>.
    ///
    /// Facing and recoil are read/write on the built instance rather than static functions: unlike a
    /// per-kind rig helper, this has no host <c>MonoBehaviour</c> of its own to carry the recoil
    /// timer, so whatever owns it (today, <see cref="MaxWorlds.Factories.ShedFitting"/>) ticks it the
    /// same dt-parameterized way it already ticks its own state, and calls <see cref="Face"/> with
    /// wherever it is currently aiming.
    /// </summary>
    public sealed class ShedTurretRig
    {
        // ---------------------------------------------------------------- ticket MV-1058's own numbers
        private const float BaseDiameter = 1.0f;
        private const float BaseHeight = 0.32f;
        private const float DomeDiameter = 0.86f;      // ticket: "≈ 0.85 m" — a hair over, so a rounded
                                                          // mesh sample never dips under the AC threshold
        private const float BarrelLength = 1.3f;
        private const float BarrelRadius = 0.09f;
        private const float CollarRadius = 0.16f;
        private const float CollarThickness = 0.14f;
        private const float LaserBarrelSide = 0.17f;
        private const float LensDiameter = 0.26f;
        private const float PodRadius = 0.085f;
        private const float PodSeparation = 0.22f;      // lateral offset of each missile pod from centre
        private const float PodTipDiameter = 0.28f;

        private const float RecoilDistance = 0.22f;      // fraction of a part's own reach pulled back on fire
        private const float RecoilDecay = 6f;            // 1/seconds — a kick, not a sag

        private static readonly Quaternion UpToForward = Quaternion.FromToRotation(Vector3.up, Vector3.forward);

        private readonly Transform _turret;
        private readonly Transform[] _recoilParts;
        private readonly Vector3[] _recoilRestLocal;
        private float _recoil;

        /// <summary>The whole aiming assembly — rotate this (via <see cref="Face"/>) to point the
        /// turret, the same "hand back transforms for the caller to drive" shape
        /// <see cref="RobotBodies.Body.Wheels"/> already uses.</summary>
        public Transform Turret => _turret;

        private ShedTurretRig(Transform turret, Transform[] recoilParts, Vector3[] recoilRestLocal)
        {
            _turret = turret;
            _recoilParts = recoilParts;
            _recoilRestLocal = recoilRestLocal;
        }

        /// <summary>Builds one turret under <paramref name="root"/>, wearing <paramref name="palette"/>
        /// — callable with no shed (or any other host) present; see <c>MV1058ShedTurretTests</c>.</summary>
        public static ShedTurretRig Build(Transform root, ShedFittingKind kind, in ShedTurretPalette palette)
        {
            float baseRadius = BaseDiameter * 0.5f;
            Part(root, CharacterMeshes.Prism(8, baseRadius, baseRadius * 0.88f, BaseHeight, 0.16f), palette.Base,
                new Vector3(0f, BaseHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "TurretBase");

            // The yaw pivot: dome and barrel both live under here, so Face() is one Quaternion write.
            // Base stays under root, fixed, per the ticket's own "base stays fixed" line.
            var turret = new GameObject("Turret").transform;
            turret.SetParent(root, worldPositionStays: false);
            turret.localPosition = new Vector3(0f, BaseHeight, 0f);

            Part(turret, CharacterMeshes.Sphere(16), palette.Dome,
                Vector3.zero, Quaternion.identity, Vector3.one * DomeDiameter, "TurretDome");

            float domeRadius = DomeDiameter * 0.5f;
            return kind switch
            {
                ShedFittingKind.Laser => BuildLaser(turret, domeRadius, palette),
                ShedFittingKind.Missile => BuildMissile(turret, domeRadius, palette),
                _ => BuildSpiker(turret, domeRadius, palette),
            };
        }

        /// <summary>One round barrel, pointing straight out from the dome (ticket item 2), ending in a
        /// glowing red muzzle collar.</summary>
        private static ShedTurretRig BuildSpiker(Transform turret, float domeRadius, in ShedTurretPalette palette)
        {
            float barrelMid = domeRadius + BarrelLength * 0.5f;
            Transform barrel = Part(turret, CharacterMeshes.Beam(BarrelLength, BarrelRadius, BarrelRadius * 0.82f, 8),
                palette.Barrel, Vector3.forward * barrelMid, UpToForward, Vector3.one, "SpikerBarrel");

            Vector3 collarRest = Vector3.forward * (domeRadius + BarrelLength);
            Transform collar = Part(turret, CharacterMeshes.Ring(BarrelRadius * 0.95f, CollarRadius, CollarThickness, 16),
                palette.Glow, collarRest, UpToForward, Vector3.one, "SpikerMuzzleCollar");

            return new ShedTurretRig(turret, new[] { barrel, collar }, new[] { barrel.localPosition, collarRest });
        }

        /// <summary>One square barrel (<see cref="CharacterMeshes.Bevelled"/>, never a Unity cube),
        /// ending in a glowing red emitter lens.</summary>
        private static ShedTurretRig BuildLaser(Transform turret, float domeRadius, in ShedTurretPalette palette)
        {
            float barrelMid = domeRadius + BarrelLength * 0.5f;
            var barrelSize = new Vector3(LaserBarrelSide, LaserBarrelSide, BarrelLength);
            Transform barrel = Part(turret, CharacterMeshes.Bevelled(barrelSize, CharacterMeshes.DefaultBevel(barrelSize)),
                palette.Barrel, Vector3.forward * barrelMid, Quaternion.identity, Vector3.one, "LaserBarrel");

            Vector3 lensRest = Vector3.forward * (domeRadius + BarrelLength);
            Transform lens = Part(turret, CharacterMeshes.Sphere(12), palette.Glow,
                lensRest, Quaternion.identity, Vector3.one * LensDiameter, "LaserEmitterLens");

            return new ShedTurretRig(turret, new[] { barrel, lens }, new[] { barrel.localPosition, lensRest });
        }

        /// <summary>Twin launch pods (ticket: "replaces the current rig's look" — MV-913's single
        /// up-angled arm is gone), each a red pod ending in a glowing tip.</summary>
        private static ShedTurretRig BuildMissile(Transform turret, float domeRadius, in ShedTurretPalette palette)
        {
            var parts = new Transform[4];
            var rest = new Vector3[4];
            int i = 0;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 lateral = Vector3.right * (side * PodSeparation);
                Vector3 podMid = lateral + Vector3.forward * (domeRadius + BarrelLength * 0.5f);
                string tag = side < 0f ? "L" : "R";

                parts[i] = Part(turret, CharacterMeshes.Beam(BarrelLength, PodRadius, PodRadius * 0.85f, 8),
                    palette.Barrel, podMid, UpToForward, Vector3.one, "MissilePod" + tag);
                rest[i] = podMid;
                i++;

                Vector3 tipRest = lateral + Vector3.forward * (domeRadius + BarrelLength);
                parts[i] = Part(turret, CharacterMeshes.Sphere(12), palette.Glow,
                    tipRest, Quaternion.identity, Vector3.one * PodTipDiameter, "MissileTip" + tag);
                rest[i] = tipRest;
                i++;
            }

            return new ShedTurretRig(turret, parts, rest);
        }

        private static Transform Part(Transform parent, Mesh mesh, Material mat,
                                      Vector3 at, Quaternion rot, Vector3 scale, string name)
            => CharacterPart.Add(parent, mesh, mat, at, rot, scale, name);

        /// <summary>Yaws the turret to face <paramref name="worldDirection"/> — flattened to the ground
        /// plane, since the play camera is fixed top-down and a pitched facing would never read (ticket
        /// item 3). A no-op on a near-zero direction (no target yet) rather than snapping to a
        /// meaningless heading.</summary>
        public void Face(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 1e-6f) return;
            _turret.rotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
        }

        /// <summary>Kicks every recoiling part back to full recoil — <see cref="Tick"/> eases it back to
        /// rest. Call once per shot; calling it again mid-recoil restarts the kick rather than
        /// stacking, the same resettable-pulse shape <c>RobotRig</c>'s own <c>_strikePunch</c> uses.</summary>
        public void Fire() => _recoil = 1f;

        /// <summary>Advances the recoil pulse by <paramref name="dt"/> — dt-parameterized, not
        /// <see cref="Time.deltaTime"/>, so a test (or a pooled/paused caller) can drive it without a
        /// live Update loop, the idiom every Tick(dt) method in this project already uses.</summary>
        public void Tick(float dt)
        {
            if (_recoil > 0f) _recoil = Mathf.Max(0f, _recoil - RecoilDecay * dt);
            for (int i = 0; i < _recoilParts.Length; i++)
            {
                Vector3 rest = _recoilRestLocal[i];
                float pullback = rest.magnitude * RecoilDistance * _recoil;
                _recoilParts[i].localPosition = rest - rest.normalized * pullback;
            }
        }
    }
}
