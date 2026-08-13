using UnityEngine;
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
    /// The press/drag/release input and the arm/disarm abort (MV-372) live in
    /// <see cref="AbilityJoystickControlBase"/> — this only answers "is the ability ready" and "what do
    /// the arc + landing circle look like".
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class WaterBalloonJoystickControl : AbilityJoystickControlBase
    {
        private Transform _origin;
        private PlayerAbilities _abilities;

        private GameObject _arcGo;
        private GameObject _circleGo;

        /// <summary>Wired by <c>HudController</c> right after construction — the same self-attach
        /// hand-off shape <see cref="PlayerAbilities"/> itself uses.</summary>
        public void Init(RectTransform knob, Transform origin, PlayerAbilities abilities, Image rings = null)
        {
            _origin = origin;
            _abilities = abilities;
            InitBase(knob, origin, rings);
        }

        /// <summary>True while the landing circle is actually showing — MV-356 (it shipped WV-241
        /// showing on the first aim of a run and never again). A test reads this without a screenshot.</summary>
        public bool LandingCircleVisible => _circleGo != null && _circleGo.activeSelf;

        /// <summary>Vertex count of the landing circle's current mesh. "Active but empty" reads as
        /// invisible to the player exactly like "not active" does, so a test checking only
        /// <see cref="LandingCircleVisible"/> could pass against a circle that draws nothing.</summary>
        public int LandingCircleVertexCount
        {
            get
            {
                if (_circleGo == null) return 0;
                var mesh = _circleGo.GetComponent<MeshFilter>().sharedMesh;
                return mesh != null ? mesh.vertexCount : 0;
            }
        }

        protected override bool AbilityReady => _abilities != null && _abilities.WaterBalloonReady;

        protected override void Fire(Vector3 direction) => _abilities?.TryThrowWaterBalloon(direction);

        private void OnDestroy()
        {
            if (_arcGo != null) Destroy(_arcGo);
            if (_circleGo != null) Destroy(_circleGo);
        }

        protected override void ShowAimVisuals()
        {
            EnsureVisuals();
            _arcGo.SetActive(true);
            _circleGo.SetActive(true);
        }

        protected override void HideAimVisuals()
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
        /// come, so the picture never promises a throw the ability doesn't have. Also tints both
        /// meshes to the current armed state (MV-372 AC5).</summary>
        protected override void RebuildAimVisual()
        {
            if (_origin == null || _arcGo == null) return;

            int level = Mathf.Max(1, WeaponSystemState.AbilityLevel(AbilityKind.WaterBalloon));
            float baseDistance = DevTuning.Or(DevTuning.WaterBalloonBaseDistance, AbilityTuning.DefaultWaterBalloonBaseDistance);
            float perLevel = DevTuning.Or(DevTuning.WaterBalloonDistancePerLevel, AbilityTuning.DefaultWaterBalloonDistancePerLevel);
            float maxDistance = AbilityTuning.WaterBalloonDistance(level, baseDistance, perLevel);
            float distance = Mathf.Max(0.15f, maxDistance * DistanceFraction);

            _arcGo.transform.SetPositionAndRotation(_origin.position, Quaternion.LookRotation(Direction, Vector3.up));
            _arcGo.GetComponent<MeshFilter>().sharedMesh = WaterBalloonAimMesh.Build(distance);

            Vector3 landing = _origin.position + Direction * distance;
            _circleGo.transform.SetPositionAndRotation(
                new Vector3(landing.x, 0.01f, landing.z), Quaternion.identity);
            _circleGo.GetComponent<MeshFilter>().sharedMesh =
                WaterBalloonAimMesh.BuildLandingCircle(PlayerAbilities.SplashRadius);

            ApplyArmedTint(_arcGo, IsArmed);
            ApplyArmedTint(_circleGo, IsArmed);
        }
    }
}
