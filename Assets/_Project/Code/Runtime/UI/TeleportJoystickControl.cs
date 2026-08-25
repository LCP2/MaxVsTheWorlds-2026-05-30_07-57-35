using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Teleport's joystick input (MV-338: "Teleport's button needs to work the same way as Water
    /// Balloon — a direction and distance joystick"). Mirrors
    /// <see cref="WaterBalloonJoystickControl"/> beat for beat — press-and-hold shows a destination
    /// reticle, the drag aims it, release blinks — except there is no arc to draw: a blink isn't
    /// thrown, so only a landing circle marks where Max will land. The reticle previews toward the
    /// ability's REAL blink distance (<see cref="AbilityTuning.TeleportDistance"/>) as the drag comes
    /// out, but same as Water Balloon's own throw, the actual blink in
    /// <see cref="PlayerAbilities.TryTeleport"/> always goes the ability's full distance for the
    /// level — the drag aims a DIRECTION, it doesn't shorten the blink.
    ///
    /// The press/drag/release input and the arm/disarm abort (MV-372) live in
    /// <see cref="AbilityJoystickControlBase"/>, the same shared layer Water Balloon uses.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class TeleportJoystickControl : AbilityJoystickControlBase
    {
        /// <summary>The reticle radius, world metres — roughly Max's own footprint, so it reads as
        /// "you will stand here", not an arbitrary ring.</summary>
        private const float LandingRadius = 0.6f;

        private Transform _origin;
        private PlayerAbilities _abilities;

        private GameObject _circleGo;

        /// <summary>Wired by <c>HudController</c> right after construction — the same self-attach
        /// hand-off shape <see cref="WaterBalloonJoystickControl"/> uses.</summary>
        public void Init(RectTransform knob, Transform origin, PlayerAbilities abilities, Image rings = null)
        {
            _origin = origin;
            _abilities = abilities;
            InitBase(knob, origin, rings);
        }

        /// <summary>True while the landing circle is actually showing — the same test hook
        /// <see cref="WaterBalloonJoystickControl.LandingCircleVisible"/> exposes (MV-356: a control can
        /// read as "aiming" via <see cref="AbilityJoystickControlBase.IsAiming"/> while the mesh it's
        /// supposed to show never actually went active, and only checking the flag would miss that).
        /// MV-385: Lee's playtest found no landing-target indicator during Teleport aim — this lets a
        /// test assert the circle itself, not just the aiming flag, closing the same gap for Teleport
        /// that Water Balloon already had covered.</summary>
        public bool LandingCircleVisible => _circleGo != null && _circleGo.activeSelf;

        /// <summary>Vertex count of the landing circle's current mesh — "active but empty" reads as
        /// invisible to the player exactly like "not active" does, same reasoning as
        /// <see cref="WaterBalloonJoystickControl.LandingCircleVertexCount"/>.</summary>
        public int LandingCircleVertexCount
        {
            get
            {
                if (_circleGo == null) return 0;
                var mesh = _circleGo.GetComponent<MeshFilter>().sharedMesh;
                return mesh != null ? mesh.vertexCount : 0;
            }
        }

        protected override bool IsOwned => WeaponSystemState.IsAcquired(AbilityKind.Teleport);

        protected override bool AbilityReady => _abilities != null && _abilities.TeleportReady;

        protected override void Fire(Vector3 direction) => _abilities?.TryTeleport(direction);

        private void OnDestroy()
        {
            if (_circleGo != null) Destroy(_circleGo);
        }

        protected override void ShowAimVisuals()
        {
            EnsureVisuals();
            _circleGo.SetActive(true);
            // MV-371: announce the ability's full blink range so the camera can zoom out to fit it,
            // if it doesn't already. Decoupled hand-off — see HudSignals.TeleportAimStarted.
            HudSignals.EmitTeleportAimStarted(CurrentMaxDistance());
        }

        /// <summary>Seconds the landing circle stays up, still tinted refusal red, after a release
        /// that landed on an illegal destination — MV-544 item 4: "a rejected release does not feel
        /// like a dropped input". Every legal release still hides instantly, unchanged.</summary>
        private const float RefusalFlashSeconds = 0.15f;

        protected override void HideAimVisuals()
        {
            if (_circleGo != null)
            {
                if (_lastLandingIllegal && Application.isPlaying) StartCoroutine(FlashRefusalThenHide());
                else _circleGo.SetActive(false);
            }
            HudSignals.EmitTeleportAimEnded();
        }

        private IEnumerator FlashRefusalThenHide()
        {
            yield return new WaitForSeconds(RefusalFlashSeconds);
            if (_circleGo != null) _circleGo.SetActive(false);
        }

        private void EnsureVisuals()
        {
            if (_circleGo == null) _circleGo = NewAimMesh("Teleport Landing Circle");
        }

        private static GameObject NewAimMesh(string name)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            // Marks the renderer as owned so neither RuntimeSurfaceDirector nor CharacterSkinDirector
            // repaints it — same guard WaterBalloonJoystickControl's own aim meshes carry.
            go.AddComponent<KeepsOwnMaterial>();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>The ability's REAL blink distance at the current level — what both the reticle
        /// preview (scaled by drag) and the MV-371 camera-zoom signal (unscaled, the ability's full
        /// reach) size themselves from.</summary>
        private static float CurrentMaxDistance()
        {
            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.Teleport));
            float baseDistance = DevTuning.Or(DevTuning.TeleportBaseDistance, AbilityTuning.DefaultTeleportBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.TeleportDistancePerLevel, AbilityTuning.DefaultTeleportDistancePerLevel);
            return AbilityTuning.TeleportDistance(level, baseDistance, perLevel);
        }

        /// <summary>True for the landing point <see cref="RebuildAimVisual"/> most recently computed —
        /// MV-544: read at aim time every frame, driving both the marker's refusal-red tint and
        /// whether release briefly flashes it before hiding.</summary>
        private bool _lastLandingIllegal;

        /// <summary>Rebuilds the landing reticle for the current drag — distance previews toward the
        /// ability's REAL blink distance at the current level, scaled by how far the drag has come, so
        /// the picture never promises a blink further than the ability actually goes. Also tints the
        /// reticle to the current armed state (MV-372 AC5).</summary>
        protected override void RebuildAimVisual()
        {
            if (_origin == null || _circleGo == null) return;

            float maxDistance = CurrentMaxDistance();
            float distance = Mathf.Max(0.15f, maxDistance * DistanceFraction);

            Vector3 landing = _origin.position + Direction * distance;
            _circleGo.transform.SetPositionAndRotation(
                new Vector3(landing.x, 0.01f, landing.z), Quaternion.identity);
            _circleGo.GetComponent<MeshFilter>().sharedMesh = WaterBalloonAimMesh.BuildLandingCircle(LandingRadius);

            // MV-544: the same reachability test TryTeleport commits with, so the preview never
            // promises (or refuses) a destination the actual blink would disagree with.
            _lastLandingIllegal = !PlayerAbilities.IsLegalTeleportDestination(
                EnemyNavigation.Map, _origin.position, landing, EnemyNavigation.IsGateOpen);

            ApplyArmedTint(_circleGo, IsArmed, AbilityReady, _lastLandingIllegal);
        }
    }
}
