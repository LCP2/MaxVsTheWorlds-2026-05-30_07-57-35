using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The Water Balloon joystick's touch handling (WV-240, spec §6a): "press-and-hold shows an arc
    /// and a landing circle; the joystick controls the landing circle's direction and distance;
    /// release throws." <see cref="AbilityControlArt.BuildJoystick"/> (WV-241) only drew the control's
    /// rings/knob at rest — this drives the knob from the drag, builds the world-space aim visuals
    /// from the ability's REAL throw distance (<see cref="AbilityTuning.WaterBalloonDistance"/>, never
    /// a shape someone liked the look of — the same rule <see cref="MaxWorlds.VFX.AimReticleMesh"/>
    /// set for the blaster's own reticle), and hands the release off to
    /// <see cref="PlayerAbilities.TryThrowWaterBalloon"/>.
    ///
    /// A press is simply ignored while unowned or on cooldown (spec: "every control ... is disabled
    /// during cooldown") — the aim visuals never appear for a throw that can't happen.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class WaterBalloonJoystickControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>Drag distance, screen px, for the full authored throw distance — matches
        /// <c>HudController.AddOnScreenStick</c>'s own 90 px movementRange so the two sticks feel
        /// the same weight under a thumb.</summary>
        public const float DragRadiusPixels = 90f;

        /// <summary>How far the visual knob itself travels, px — matches the move/aim knobs'
        /// own 26 px offset at full deflection (<c>HudController.UpdateJoysticks</c>).</summary>
        public const float KnobRadiusPixels = 26f;

        private RectTransform _knob;
        private Transform _origin;
        private PlayerAbilities _abilities;

        private bool _dragging;
        private Vector2 _pressScreenPos;
        private Vector3 _direction = Vector3.forward;
        private float _distanceFraction;

        private GameObject _arcGo;
        private GameObject _circleGo;

        /// <summary>Wired by <c>HudController</c> right after construction — the same self-attach
        /// hand-off shape <see cref="PlayerAbilities"/> itself uses.</summary>
        public void Init(RectTransform knob, Transform origin, PlayerAbilities abilities)
        {
            _knob = knob;
            _origin = origin;
            _abilities = abilities;
        }

        /// <summary>True while the balloon is being aimed — a test reads this without simulating a
        /// real drag.</summary>
        public bool IsAiming => _dragging;

        /// <summary>The direction the current (or most recently released) drag would throw toward.</summary>
        public Vector3 Direction => _direction;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_abilities == null || !_abilities.WaterBalloonReady) return;

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

            // A tap with no real drag has no direction to throw toward — the joystick just closes.
            if (_distanceFraction > 0.05f) _abilities?.TryThrowWaterBalloon(_direction);
        }

        private void OnDisable()
        {
            _dragging = false;
            HideAim();
        }

        private void OnDestroy()
        {
            if (_arcGo != null) Destroy(_arcGo);
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
            _arcGo.SetActive(true);
            _circleGo.SetActive(true);
        }

        private void HideAim()
        {
            if (_arcGo != null) _arcGo.SetActive(false);
            if (_circleGo != null) _circleGo.SetActive(false);
        }

        private void EnsureVisuals()
        {
            if (_arcGo == null) _arcGo = NewAimMesh("Water Balloon Arc");
            if (_circleGo == null) _circleGo = NewAimMesh("Water Balloon Landing Circle");
        }

        private static GameObject NewAimMesh(string name)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            // Marks the renderer as owned so neither RuntimeSurfaceDirector nor CharacterSkinDirector
            // repaints it as scenery or a character's skin (the exact bug AimReticle's own remarks
            // describe) — see KeepsOwnMaterial's own doc comment.
            go.AddComponent<KeepsOwnMaterial>();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>Rebuilds the arc + landing circle for the current drag — distance is the
        /// ability's REAL throw distance at the current level, scaled by how far the drag has
        /// come, so the picture never promises a throw the ability doesn't have.</summary>
        private void UpdateAimVisual()
        {
            if (_origin == null || _arcGo == null) return;

            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.WaterBalloon));
            float baseDistance = DevTuning.Or(DevTuning.WaterBalloonBaseDistance, AbilityTuning.DefaultWaterBalloonBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.WaterBalloonDistancePerLevel, AbilityTuning.DefaultWaterBalloonDistancePerLevel);
            float maxDistance = AbilityTuning.WaterBalloonDistance(level, baseDistance, perLevel);
            float distance = Mathf.Max(0.15f, maxDistance * _distanceFraction);

            _arcGo.transform.SetPositionAndRotation(_origin.position, Quaternion.LookRotation(_direction, Vector3.up));
            _arcGo.GetComponent<MeshFilter>().sharedMesh = WaterBalloonAimMesh.Build(distance);

            Vector3 landing = _origin.position + _direction * distance;
            _circleGo.transform.SetPositionAndRotation(
                new Vector3(landing.x, 0.01f, landing.z), Quaternion.identity);
            _circleGo.GetComponent<MeshFilter>().sharedMesh =
                WaterBalloonAimMesh.BuildLandingCircle(PlayerAbilities.SplashRadius);
        }
    }
}
