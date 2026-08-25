using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Core;
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
        private Mesh _circleMesh;

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

        /// <summary>MV-545 diagnostic: where the landing circle currently sits, world space. Zero if
        /// it doesn't exist yet — matches <see cref="LandingCircleVertexCount"/>'s "not built" reading.</summary>
        public Vector3 LandingCircleWorldPosition => _circleGo != null ? _circleGo.transform.position : Vector3.zero;

        /// <summary>MV-545 diagnostic: <see cref="Renderer.isVisible"/> off the circle's own
        /// <see cref="MeshRenderer"/> — true only if it was actually inside a camera's frustum and drawn
        /// last frame. An object that reads active + non-empty mesh + this true, yet the player reports
        /// seeing nothing, is not a lifecycle bug: something else is drawing over it.</summary>
        public bool LandingCircleRendererIsVisible =>
            _circleGo != null && _circleGo.TryGetComponent<MeshRenderer>(out var r) && r.isVisible;

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

        protected override void HideAimVisuals()
        {
            if (_circleGo != null) _circleGo.SetActive(false);
            HudSignals.EmitTeleportAimEnded();
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
            // MV-545: a dedicated above-everything queue, not the shared ground-VFX baseline — see
            // VfxMaterials.AlphaBlendOnTop's own doc comment for why the baseline queue loses the
            // sort the moment a decal lands on the same spot.
            renderer.sharedMaterial = VfxMaterials.AlphaBlendOnTop(VfxMaterials.Solid());
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
            _circleMesh = WaterBalloonAimMesh.BuildLandingCircle(LandingRadius, reuse: _circleMesh);
            _circleGo.GetComponent<MeshFilter>().sharedMesh = _circleMesh;

            ApplyArmedTint(_circleGo, IsArmed, AbilityReady);
        }
    }
}
