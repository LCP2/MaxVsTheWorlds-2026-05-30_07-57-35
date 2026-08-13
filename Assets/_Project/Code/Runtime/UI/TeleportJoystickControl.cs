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

        /// <summary>Wired by <c>HudController</c> right after construction — the same self-attach
        /// hand-off shape <see cref="WaterBalloonJoystickControl"/> uses.</summary>
        public void Init(RectTransform knob, Transform origin, PlayerAbilities abilities, Image rings = null)
        {
            _origin = origin;
            _abilities = abilities;
            InitBase(knob, origin, rings);
        }

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
        }

        protected override void HideAimVisuals()
        {
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

        /// <summary>Rebuilds the landing reticle for the current drag — distance previews toward the
        /// ability's REAL blink distance at the current level, scaled by how far the drag has come, so
        /// the picture never promises a blink further than the ability actually goes. Also tints the
        /// reticle to the current armed state (MV-372 AC5).</summary>
        protected override void RebuildAimVisual()
        {
            if (_origin == null || _circleGo == null) return;

            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.Teleport));
            float baseDistance = DevTuning.Or(DevTuning.TeleportBaseDistance, AbilityTuning.DefaultTeleportBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.TeleportDistancePerLevel, AbilityTuning.DefaultTeleportDistancePerLevel);
            float maxDistance = AbilityTuning.TeleportDistance(level, baseDistance, perLevel);
            float distance = Mathf.Max(0.15f, maxDistance * DistanceFraction);

            Vector3 landing = _origin.position + Direction * distance;
            _circleGo.transform.SetPositionAndRotation(
                new Vector3(landing.x, 0.01f, landing.z), Quaternion.identity);
            _circleGo.GetComponent<MeshFilter>().sharedMesh = WaterBalloonAimMesh.BuildLandingCircle(LandingRadius);

            ApplyArmedTint(_circleGo, IsArmed);
        }
    }
}
