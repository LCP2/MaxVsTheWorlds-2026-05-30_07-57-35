using UnityEngine;
using UnityEngine.EventSystems;
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
    /// A press is simply ignored while unowned or on cooldown, the same gating language Water
    /// Balloon's own control uses.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class TeleportJoystickControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>Same drag weight as Water Balloon's own stick (WV-240) — two ability joysticks
        /// stacked on the same column must feel identical under a thumb.</summary>
        public const float DragRadiusPixels = WaterBalloonJoystickControl.DragRadiusPixels;
        public const float KnobRadiusPixels = WaterBalloonJoystickControl.KnobRadiusPixels;

        /// <summary>The reticle radius, world metres — roughly Max's own footprint, so it reads as
        /// "you will stand here", not an arbitrary ring.</summary>
        private const float LandingRadius = 0.6f;

        private RectTransform _knob;
        private Transform _origin;
        private PlayerAbilities _abilities;

        private bool _dragging;
        private Vector2 _pressScreenPos;
        private Vector3 _direction = Vector3.forward;
        private float _distanceFraction;

        private GameObject _circleGo;

        /// <summary>Wired by <c>HudController</c> right after construction — the same self-attach
        /// hand-off shape <see cref="WaterBalloonJoystickControl"/> uses.</summary>
        public void Init(RectTransform knob, Transform origin, PlayerAbilities abilities)
        {
            _knob = knob;
            _origin = origin;
            _abilities = abilities;
        }

        /// <summary>True while the blink is being aimed — a test reads this without simulating a
        /// real drag.</summary>
        public bool IsAiming => _dragging;

        /// <summary>The direction the current (or most recently released) drag would blink toward.</summary>
        public Vector3 Direction => _direction;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_abilities == null || !_abilities.TeleportReady) return;

            _dragging = true;
            _pressScreenPos = eventData.position;
            _direction = InitialFacing();
            _distanceFraction = 0f;
            ShowAim();
            UpdateAimVisual();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging) return;

            Vector2 delta = eventData.position - _pressScreenPos;
            _distanceFraction = Mathf.Clamp01(delta.magnitude / DragRadiusPixels);

            if (delta.sqrMagnitude > 1f)
            {
                Vector2 dir2 = delta.normalized;
                _direction = new Vector3(dir2.x, 0f, dir2.y);
                if (_knob != null) _knob.anchoredPosition = dir2 * (_distanceFraction * KnobRadiusPixels);
            }

            UpdateAimVisual();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_dragging) return;

            _dragging = false;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            HideAim();

            // A tap with no real drag has no direction to blink toward — the joystick just closes.
            if (_distanceFraction > 0.05f) _abilities?.TryTeleport(_direction);
        }

        private void OnDisable()
        {
            _dragging = false;
            HideAim();
        }

        private void OnDestroy()
        {
            if (_circleGo != null) Destroy(_circleGo);
        }

        private Vector3 InitialFacing()
        {
            if (_origin == null) return Vector3.forward;
            Vector3 f = _origin.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        private void ShowAim()
        {
            EnsureVisuals();
            _circleGo.SetActive(true);
        }

        private void HideAim()
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
        /// the picture never promises a blink further than the ability actually goes.</summary>
        private void UpdateAimVisual()
        {
            if (_origin == null || _circleGo == null) return;

            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.Teleport));
            float baseDistance = DevTuning.Or(DevTuning.TeleportBaseDistance, AbilityTuning.DefaultTeleportBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.TeleportDistancePerLevel, AbilityTuning.DefaultTeleportDistancePerLevel);
            float maxDistance = AbilityTuning.TeleportDistance(level, baseDistance, perLevel);
            float distance = Mathf.Max(0.15f, maxDistance * _distanceFraction);

            Vector3 landing = _origin.position + _direction * distance;
            _circleGo.transform.SetPositionAndRotation(
                new Vector3(landing.x, 0.01f, landing.z), Quaternion.identity);
            _circleGo.GetComponent<MeshFilter>().sharedMesh = WaterBalloonAimMesh.BuildLandingCircle(LandingRadius);
        }
    }
}
