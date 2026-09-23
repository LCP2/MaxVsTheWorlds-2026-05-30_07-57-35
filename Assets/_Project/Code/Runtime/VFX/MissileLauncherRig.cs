using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>The three materials a built <see cref="MissileLauncherRig"/> wears — caller-owned, the
    /// same "palette in, no colour decided here" shape as <see cref="RobotPalette"/>, so a future caller
    /// (a World 2 Replicator, a World 3 structure) can hand this rig whatever surface treatment its own
    /// world wants without this file knowing any of them exist.</summary>
    public readonly struct MissileLauncherPalette
    {
        /// <summary>The base/turntable the whole assembly turns on.</summary>
        public readonly Material Base;
        /// <summary>The launch arm reaching up off the turret.</summary>
        public readonly Material Arm;
        /// <summary>The raised launch element at the arm's tip — the part that recoils on <see cref="MissileLauncherRig.Fire"/>.</summary>
        public readonly Material Tip;

        public MissileLauncherPalette(Material baseMaterial, Material arm, Material tip)
        {
            Base = baseMaterial; Arm = arm; Tip = tip;
        }
    }

    /// <summary>
    /// A reusable directional missile launcher (MV-913) — a turntable base and a raised launch element,
    /// built from <see cref="CharacterMeshes"/> exactly the way <see cref="RobotBodies"/> builds a robot,
    /// never from a <c>GameObject.CreatePrimitive</c> box. Replaces the shed missile fitting's old bare
    /// cube (<c>MapRuntime.BuildShedFittings</c>), and is deliberately generic — this file has no
    /// reference to sheds, <c>MowerHutch</c> or <c>ShedFitting</c> — so a future caller (World 2's
    /// Replicators, a World 3 structure) can build one in one line: <c>MissileLauncherRig.Build(root,
    /// size, palette)</c>.
    ///
    /// Facing and recoil are read/write on the built instance rather than static functions, because
    /// unlike <see cref="RobotRig"/>'s per-kind tells this rig has no host MonoBehaviour of its own to
    /// carry the recoil timer — whatever owns this rig (today, <c>ShedFitting</c>) ticks it exactly the
    /// way <c>ShedFitting.Tick</c> already ticks its own dt-parameterized state, and calls
    /// <see cref="Face"/> with wherever it is currently aiming.
    /// </summary>
    public sealed class MissileLauncherRig
    {
        private const float RecoilDistance = 0.25f;   // fraction of the tip's own reach it pulls back on fire
        private const float RecoilDecay = 6f;          // 1/seconds — fast, so the punch reads as a kick not a sag

        private readonly Transform _turret;
        private readonly Transform _tip;
        private readonly Vector3 _tipRestLocal;
        private float _recoil;

        /// <summary>The whole aiming assembly — rotate this (via <see cref="Face"/>) to point the
        /// launcher, exactly the way <see cref="RobotBodies.Body.Wheels"/> hands back transforms for its
        /// caller to drive rather than driving them itself.</summary>
        public Transform Turret => _turret;

        private MissileLauncherRig(Transform turret, Transform tip, Vector3 tipRestLocal)
        {
            _turret = turret;
            _tip = tip;
            _tipRestLocal = tipRestLocal;
        }

        /// <summary>Builds one launcher under <paramref name="root"/>, <paramref name="size"/> metres
        /// across its base, wearing <paramref name="palette"/>. Callable in one line, with no shed (or
        /// any other host) present — see <c>MV913MissileLauncherRigTests</c>.</summary>
        public static MissileLauncherRig Build(Transform root, float size, in MissileLauncherPalette palette)
        {
            float baseHeight = size * 0.34f;
            float baseRadius = size * 0.52f;

            Part(root, CharacterMeshes.Prism(8, baseRadius, baseRadius * 0.92f, baseHeight, 0.18f), palette.Base,
                new Vector3(0f, baseHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "LauncherBase");

            // The turret pivot: everything that aims lives under here, so Face() is one Quaternion write.
            var turret = new GameObject("LauncherTurret").transform;
            turret.SetParent(root, worldPositionStays: false);
            turret.localPosition = new Vector3(0f, baseHeight, 0f);

            // The arm — tapered, angled up and out along armDir, so its own long axis IS the
            // unambiguous facing cue (ticket AC2) even before the tip is added on the end of it.
            // CharacterMeshes.Beam is built along local +Y, so FromToRotation(up, armDir) aligns it
            // exactly, with no Euler-sign guessing about which way "forward" tilts.
            float armLength = size * 0.85f;
            Vector3 armDir = new Vector3(0f, 0.75f, 0.66f).normalized;   // up-and-forward
            Quaternion armRot = Quaternion.FromToRotation(Vector3.up, armDir);
            Part(turret, CharacterMeshes.Beam(armLength, size * 0.09f, size * 0.05f, 6), palette.Arm,
                armDir * (armLength * 0.5f), armRot, Vector3.one, "LauncherArm");

            // The raised launch element (ticket item 1) — a lathed pod at the arm's tip, what
            // MissileLauncherRig recoils on Fire().
            Vector3 tipRestLocal = armDir * armLength;
            var tip = Part(turret, CharacterMeshes.Sphere(16), palette.Tip,
                tipRestLocal, Quaternion.identity, Vector3.one * (size * 0.24f), "LauncherTip");

            return new MissileLauncherRig(turret, tip, tipRestLocal);
        }

        private static Transform Part(Transform parent, Mesh mesh, Material mat,
                                      Vector3 at, Quaternion rot, Vector3 scale, string name)
            => CharacterPart.Add(parent, mesh, mat, at, rot, scale, name);

        /// <summary>Turns the whole assembly to face <paramref name="worldDirection"/> — flattened to the
        /// ground plane, since the play camera is fixed top-down and a pitched facing would never read
        /// (ticket item 2). A no-op on a near-zero direction (no target yet) rather than snapping to a
        /// meaningless heading.</summary>
        public void Face(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 1e-6f) return;
            _turret.rotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
        }

        /// <summary>Kicks the launch element back to full recoil (ticket item 4) — <see cref="Tick"/>
        /// eases it back to rest. Call once per shot; calling it again mid-recoil simply restarts the
        /// kick rather than stacking, the same "resettable pulse" shape <c>RobotRig</c>'s own
        /// <c>_strikePunch</c> uses.</summary>
        public void Fire() => _recoil = 1f;

        /// <summary>Advances the recoil pulse by <paramref name="dt"/> — dt-parameterized, not
        /// <see cref="Time.deltaTime"/>, so a test (or a pooled/paused caller) can drive it without a
        /// live Update loop, the same idiom every Tick(dt) method in this project already uses.</summary>
        public void Tick(float dt)
        {
            if (_recoil > 0f) _recoil = Mathf.Max(0f, _recoil - RecoilDecay * dt);
            float pullback = _tipRestLocal.magnitude * RecoilDistance * _recoil;
            _tip.localPosition = _tipRestLocal - _tipRestLocal.normalized * pullback;
        }
    }
}
