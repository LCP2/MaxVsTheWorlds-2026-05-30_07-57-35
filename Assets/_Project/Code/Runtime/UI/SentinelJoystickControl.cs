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
    /// MV-399: an aimed-placement joystick for deploying a Wall or Gunner sentinel away from Max,
    /// reusing the exact press/drag/release + landing-preview shape already built for Teleport and
    /// Water Balloon (MV-371/370/372) rather than a new targeting system — reversing MV-362's
    /// "deployed at Max's position, not aimed at range" DECISION per Lee's 15 Aug 2026 request: "I
    /// should be able to put them anywhere in the current arena."
    ///
    /// One class, not two, parameterized by <see cref="SentinelKind"/> — <see cref="Sentinel"/> itself
    /// already takes the same shape rather than a Wall/Gunner subclass pair.
    ///
    /// The reticle is CLAMPED into Max's own current room (<see cref="MapZone.Clamp"/>) as it is
    /// aimed, not merely tinted a warning colour past the edge — MV-393 flagged exactly that failure
    /// mode in Teleport ("a selectable-looking circle beyond a wall that then silently fails to be
    /// honoured reads as broken either way"). What the reticle shows is always where release actually
    /// lands.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class SentinelJoystickControl : AbilityJoystickControlBase
    {
        /// <summary>The reticle radius, world metres — roughly a sentinel's own footprint.</summary>
        private const float PlacementRadius = 0.9f;

        /// <summary>Keeps the Wall's 3 m-wide body clear of the wall it's dropped near — sized off the
        /// Wall's own half-width (the larger of the two bodies), so one margin serves both kinds.</summary>
        private static readonly float ZoneEdgeMargin = WallSentinel.Width * 0.5f + 0.25f;

        private Transform _origin;
        private PlayerAbilities _abilities;
        private SentinelKind _kind;

        private GameObject _circleGo;

        /// <summary>Wired by <c>HudController</c> right after construction — the same hand-off shape
        /// <see cref="TeleportJoystickControl.Init"/> uses, plus which sentinel kind this control
        /// deploys.</summary>
        public void Init(RectTransform knob, Transform origin, PlayerAbilities abilities, SentinelKind kind, Image rings = null)
        {
            _origin = origin;
            _abilities = abilities;
            _kind = kind;
            InitBase(knob, origin, rings);
        }

        /// <summary>True while the placement circle is actually showing — the same test hook
        /// <see cref="TeleportJoystickControl.LandingCircleVisible"/> exposes.</summary>
        public bool PlacementCircleVisible => _circleGo != null && _circleGo.activeSelf;

        protected override bool IsOwned => WeaponSystemState.IsAcquired(AbilityKind.Sentinels);

        protected override bool AbilityReady => _abilities != null &&
            (_kind == SentinelKind.Wall ? _abilities.WallSentinelReady : _abilities.GunnerSentinelReady);

        /// <summary>Deploys at the ability's full authored range in the dragged direction — same "the
        /// drag aims a DIRECTION, it doesn't shorten the throw" shape Teleport's blink already uses,
        /// not whatever fraction the drag happened to reach at release.</summary>
        protected override void Fire(Vector3 direction)
        {
            if (_abilities == null || _origin == null) return;

            Vector3 point = PlacementPoint(direction, AbilityTuning.DefaultSentinelPlacementRange);
            if (_kind == SentinelKind.Wall)
                _abilities.TryDeployWallSentinel(point, Quaternion.LookRotation(FlatFacing(direction)));
            else
                _abilities.TryDeployGunnerSentinel(point);
        }

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
            if (_circleGo != null) return;

            string name = _kind == SentinelKind.Wall ? "Wall Sentinel Placement Circle" : "Gunner Sentinel Placement Circle";
            _circleGo = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            // Marks the renderer as owned so neither RuntimeSurfaceDirector nor CharacterSkinDirector
            // repaints it — same guard Teleport/Water Balloon's own aim meshes carry.
            _circleGo.AddComponent<KeepsOwnMaterial>();
            var renderer = _circleGo.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _circleGo.SetActive(false);
        }

        private static Vector3 FlatFacing(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        }

        /// <summary>The aimed point at <paramref name="distance"/> from Max, pulled back inside Max's
        /// own current room if a level is loaded (a bare EditMode test fixture has none — degrades to
        /// the raw point, same no-level fallback <see cref="EnemyNavigation.Waypoint"/> itself uses).</summary>
        private Vector3 PlacementPoint(Vector3 direction, float distance)
        {
            Vector3 raw = _origin.position + direction * distance;

            MapData map = EnemyNavigation.Map;
            MapZone zone = map != null ? map.ZoneAt(_origin.position.x, _origin.position.z) : null;
            return zone != null ? zone.Clamp(raw, ZoneEdgeMargin) : raw;
        }

        /// <summary>Rebuilds the placement reticle for the current drag — distance previews toward the
        /// full placement range as the drag comes out (same shape as Teleport's own landing circle),
        /// clamped into the current room, and tinted red (via <see cref="AbilityJoystickControlBase.ApplyArmedTint"/>'s
        /// <c>ready</c> parameter) whenever the aimed point is already occupied, on top of the usual
        /// armed/cost/cap states.</summary>
        protected override void RebuildAimVisual()
        {
            if (_origin == null || _circleGo == null) return;

            float distance = Mathf.Max(0.15f, AbilityTuning.DefaultSentinelPlacementRange * DistanceFraction);
            Vector3 point = PlacementPoint(Direction, distance);

            _circleGo.transform.SetPositionAndRotation(
                new Vector3(point.x, 0.01f, point.z), Quaternion.identity);
            _circleGo.GetComponent<MeshFilter>().sharedMesh = WaterBalloonAimMesh.BuildLandingCircle(PlacementRadius);

            bool validSpot = _abilities == null || _abilities.IsValidSentinelPlacement(point);
            ApplyArmedTint(_circleGo, IsArmed, AbilityReady && validSpot);
        }
    }
}
