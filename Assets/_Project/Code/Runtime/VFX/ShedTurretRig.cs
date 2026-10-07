using UnityEngine;
using UnityEngine.Rendering;
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
        // MV-1072: Lee, phone, World 1 — "these turrets have been created massively larger than before.
        // They look ridiculous." MV-1058 sized the dome at 0.86 m (38% of the 2.25 m shed side); this
        // shrinks the WHOLE rig uniformly so the dome reads at 0.45 m (20% of the shed's side) instead,
        // applied as one scale on a dedicated child of the rig's root rather than rewriting every
        // constant below, so every part (and the recoil travel, which is itself a fraction of a part's
        // own local-space reach) stays in the same proportion MV-1058 authored.
        private const float ShrinkFactor = 0.45f / 0.86f;

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

        // ---------------------------------------------------------------- MV-1097: laser telegraph/beam
        // and the Spiker/Missile muzzle flash — Lee, device, World 1 area 18: "these turrets are clearly
        // doing damage but there is nothing coming out of them". Same visual language as the Gunner's own
        // laser (RobotRig.UpdateBeamVfx): one LineRenderer, world-space, reused across Telegraph and Beam.

        /// <summary>AC1: the telegraph aim line's resolved width must land in [0.03, 0.08] m — pulses
        /// between these two bounds rather than holding the ticket's nominal 0.05 m fixed.</summary>
        private const float TelegraphMinWidth = 0.03f;
        private const float TelegraphMaxWidth = 0.08f;
        private const float TelegraphPulseHz = 4f;

        /// <summary>Ticket item 2: "0.20 m wide" — AC1 only requires >= 0.18 m, this is the authored
        /// nominal figure itself.</summary>
        private const float BeamWidth = 0.20f;

        private static readonly Color LaserBeamColor = new Color(1f, 0.12f, 0.1f);

        private const float MuzzleFlashDuration = 0.15f;   // ticket item 5
        private const float MuzzleFlashDiameter = 0.45f;   // ticket item 5: "at least 0.4 m across"
        private static readonly Color MuzzleFlashColor = new Color(1f, 0.92f, 0.75f);

        private static readonly Quaternion UpToForward = Quaternion.FromToRotation(Vector3.up, Vector3.forward);

        private readonly Transform _turret;
        private readonly Transform _muzzleTip;
        private readonly Transform[] _recoilParts;
        private readonly Vector3[] _recoilRestLocal;
        private float _recoil;

        private LineRenderer _beamLine;
        private CameraFacingFlare _muzzleFlare;
        private float _muzzleFlashTimer;

        /// <summary>The whole aiming assembly — rotate this (via <see cref="Face"/>) to point the
        /// turret, the same "hand back transforms for the caller to drive" shape
        /// <see cref="RobotBodies.Body.Wheels"/> already uses.</summary>
        public Transform Turret => _turret;

        /// <summary>MV-1097: the muzzle point every kind fires/aims from — the same forward distance
        /// (domeRadius + BarrelLength) the Spiker collar and Laser lens already sit at, centred rather
        /// than offset to either missile pod, so one point serves the laser beam's origin and every
        /// kind's own muzzle flash alike. Built in <see cref="Build"/>, before the kind split.</summary>
        public Transform MuzzleTip => _muzzleTip;

        /// <summary>MV-1097 test-only accessor — the laser's own LineRenderer, lazily built on first
        /// <see cref="ShowTelegraph"/>/<see cref="ShowBeam"/> call, null before then.</summary>
        public LineRenderer LaserBeamLineForTests => _beamLine;

        private ShedTurretRig(Transform turret, Transform muzzleTip, Transform[] recoilParts, Vector3[] recoilRestLocal)
        {
            _turret = turret;
            _muzzleTip = muzzleTip;
            _recoilParts = recoilParts;
            _recoilRestLocal = recoilRestLocal;
        }

        /// <summary>Builds one turret under <paramref name="root"/>, wearing <paramref name="palette"/>
        /// — callable with no shed (or any other host) present; see <c>MV1058ShedTurretTests</c>.</summary>
        public static ShedTurretRig Build(Transform root, ShedFittingKind kind, in ShedTurretPalette palette)
        {
            // MV-1072: a dedicated scaled child, not root.localScale directly — root is the caller's own
            // metre-space transform (see ShedFitting.BuildTurretVisual), and this rig must shrink only
            // what it builds under it, not reach back and rescale whatever space the caller handed in.
            var rig = new GameObject("TurretRig").transform;
            rig.SetParent(root, worldPositionStays: false);
            rig.localScale = Vector3.one * ShrinkFactor;

            float baseRadius = BaseDiameter * 0.5f;
            Part(rig, CharacterMeshes.Prism(8, baseRadius, baseRadius * 0.88f, BaseHeight, 0.16f), palette.Base,
                new Vector3(0f, BaseHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "TurretBase");

            // The yaw pivot: dome and barrel both live under here, so Face() is one Quaternion write.
            // Base stays under rig, fixed, per the ticket's own "base stays fixed" line.
            var turret = new GameObject("Turret").transform;
            turret.SetParent(rig, worldPositionStays: false);
            turret.localPosition = new Vector3(0f, BaseHeight, 0f);

            Part(turret, CharacterMeshes.Sphere(16), palette.Dome,
                Vector3.zero, Quaternion.identity, Vector3.one * DomeDiameter, "TurretDome");

            float domeRadius = DomeDiameter * 0.5f;

            // MV-1097: one shared muzzle point, centred rather than kind-specific — the laser beam's
            // origin and every kind's own muzzle flash all fire from here.
            var muzzleTip = new GameObject("MuzzleTip").transform;
            muzzleTip.SetParent(turret, worldPositionStays: false);
            muzzleTip.localPosition = Vector3.forward * (domeRadius + BarrelLength);

            return kind switch
            {
                ShedFittingKind.Laser => BuildLaser(turret, muzzleTip, domeRadius, palette),
                ShedFittingKind.Missile => BuildMissile(turret, muzzleTip, domeRadius, palette),
                _ => BuildSpiker(turret, muzzleTip, domeRadius, palette),
            };
        }

        /// <summary>One round barrel, pointing straight out from the dome (ticket item 2), ending in a
        /// glowing red muzzle collar.</summary>
        private static ShedTurretRig BuildSpiker(Transform turret, Transform muzzleTip, float domeRadius, in ShedTurretPalette palette)
        {
            float barrelMid = domeRadius + BarrelLength * 0.5f;
            Transform barrel = Part(turret, CharacterMeshes.Beam(BarrelLength, BarrelRadius, BarrelRadius * 0.82f, 8),
                palette.Barrel, Vector3.forward * barrelMid, UpToForward, Vector3.one, "SpikerBarrel");

            Vector3 collarRest = Vector3.forward * (domeRadius + BarrelLength);
            Transform collar = Part(turret, CharacterMeshes.Ring(BarrelRadius * 0.95f, CollarRadius, CollarThickness, 16),
                palette.Glow, collarRest, UpToForward, Vector3.one, "SpikerMuzzleCollar");

            return new ShedTurretRig(turret, muzzleTip, new[] { barrel, collar }, new[] { barrel.localPosition, collarRest });
        }

        /// <summary>One square barrel (<see cref="CharacterMeshes.Bevelled"/>, never a Unity cube),
        /// ending in a glowing red emitter lens.</summary>
        private static ShedTurretRig BuildLaser(Transform turret, Transform muzzleTip, float domeRadius, in ShedTurretPalette palette)
        {
            float barrelMid = domeRadius + BarrelLength * 0.5f;
            var barrelSize = new Vector3(LaserBarrelSide, LaserBarrelSide, BarrelLength);
            Transform barrel = Part(turret, CharacterMeshes.Bevelled(barrelSize, CharacterMeshes.DefaultBevel(barrelSize)),
                palette.Barrel, Vector3.forward * barrelMid, Quaternion.identity, Vector3.one, "LaserBarrel");

            Vector3 lensRest = Vector3.forward * (domeRadius + BarrelLength);
            Transform lens = Part(turret, CharacterMeshes.Sphere(12), palette.Glow,
                lensRest, Quaternion.identity, Vector3.one * LensDiameter, "LaserEmitterLens");

            return new ShedTurretRig(turret, muzzleTip, new[] { barrel, lens }, new[] { barrel.localPosition, lensRest });
        }

        /// <summary>Twin launch pods (ticket: "replaces the current rig's look" — MV-913's single
        /// up-angled arm is gone), each a red pod ending in a glowing tip.</summary>
        private static ShedTurretRig BuildMissile(Transform turret, Transform muzzleTip, float domeRadius, in ShedTurretPalette palette)
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

            return new ShedTurretRig(turret, muzzleTip, parts, rest);
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
        /// stacking, the same resettable-pulse shape <c>RobotRig</c>'s own <c>_strikePunch</c> uses.
        /// MV-1097 ticket item 5: also arms the muzzle flash — every caller of <c>Fire()</c> is a Spiker
        /// or Missile shot (the Laser never calls this; see <see cref="ShedFitting.Fire"/>), so the flash
        /// and the recoil always land on the same shot, "together", exactly as the ticket asks.</summary>
        public void Fire()
        {
            _recoil = 1f;
            _muzzleFlashTimer = MuzzleFlashDuration;
        }

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

            UpdateMuzzleFlash(dt);
        }

        private void UpdateMuzzleFlash(float dt)
        {
            if (_muzzleFlashTimer <= 0f)
            {
                _muzzleFlare?.Hide();
                return;
            }

            _muzzleFlashTimer = Mathf.Max(0f, _muzzleFlashTimer - dt);
            if (_muzzleTip == null) return;

            if (_muzzleFlare == null) _muzzleFlare = CameraFacingFlare.Create("ShedTurretMuzzleFlash");
            Color c = MuzzleFlashColor;
            c.a = _muzzleFlashTimer / MuzzleFlashDuration;
            _muzzleFlare.Show(_muzzleTip.position, MuzzleFlashDiameter, c);
        }

        // ---------------------------------------------------------------- MV-1097: laser telegraph/beam

        /// <summary>Ticket item 1: a thin red aim line, pulsing between <see cref="TelegraphMinWidth"/>
        /// and <see cref="TelegraphMaxWidth"/>, from the barrel tip to wherever <paramref name="targetPos"/>
        /// currently is (it still tracks, since the direction only locks once <see cref="ShowBeam"/> is
        /// called) — called every Telegraph tick by <see cref="ShedFitting.Tick"/>.</summary>
        public void ShowTelegraph(Vector3 targetPos, float elapsedSeconds)
        {
            if (_muzzleTip == null) return;
            LineRenderer line = EnsureBeamLine();
            line.enabled = true;

            Vector3 origin = _muzzleTip.position;
            line.SetPosition(0, origin);
            line.SetPosition(1, targetPos);

            float pulse = 0.5f + 0.5f * Mathf.Sin(elapsedSeconds * TelegraphPulseHz * Mathf.PI * 2f);
            line.widthMultiplier = Mathf.Lerp(TelegraphMinWidth, TelegraphMaxWidth, pulse);
            Color c = LaserBeamColor;
            c.a = Mathf.Lerp(0.35f, 0.9f, pulse);
            line.startColor = c;
            line.endColor = c;
        }

        /// <summary>Ticket item 2: the committed beam, solid and full width, from the barrel tip along
        /// <paramref name="direction"/> (locked the instant Beam begins — see
        /// <see cref="ShedFitting.TickBeam"/>) out to <paramref name="range"/>. Called every Beam tick so
        /// the line keeps drawing from wherever the (static) turret's muzzle actually sits.</summary>
        public void ShowBeam(Vector3 direction, float range)
        {
            if (_muzzleTip == null) return;
            LineRenderer line = EnsureBeamLine();
            line.enabled = true;

            Vector3 origin = _muzzleTip.position;
            line.SetPosition(0, origin);
            line.SetPosition(1, origin + direction * range);

            line.widthMultiplier = BeamWidth;
            Color c = Color.Lerp(LaserBeamColor, Color.white, 0.5f);
            c.a = 1f;
            line.startColor = c;
            line.endColor = c;
        }

        /// <summary>Ends a laser attack (Beam phase timed out, or the fitting died mid-attack) — a no-op
        /// if the line was never built (every non-Laser kind).</summary>
        public void HideBeam()
        {
            if (_beamLine != null) _beamLine.enabled = false;
        }

        /// <summary>The laser beam/telegraph LineRenderer, built once and reused — same "built lazily,
        /// only for the one kind that needs it" shape as <c>RobotRig.BuildBeamLine</c>. World-space
        /// positions, so it draws correctly regardless of <see cref="_turret"/>'s own live yaw.</summary>
        private LineRenderer EnsureBeamLine()
        {
            if (_beamLine != null) return _beamLine;

            var go = new GameObject("LaserBeam");
            go.transform.SetParent(_turret, worldPositionStays: false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 0;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.material = VfxMaterials.Additive(VfxMaterials.Glow());
            lr.enabled = false;
            _beamLine = lr;
            return lr;
        }
    }
}
