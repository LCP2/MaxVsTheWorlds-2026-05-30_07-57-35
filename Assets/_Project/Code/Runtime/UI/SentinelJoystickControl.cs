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
    /// MV-399: an aimed-placement joystick for deploying the sentinel away from Max, reusing the exact
    /// press/drag/release + landing-preview shape already built for Teleport and Water Balloon
    /// (MV-371/370/372) rather than a new targeting system — reversing MV-362's "deployed at Max's
    /// position, not aimed at range" DECISION per Lee's 15 Aug 2026 request: "I should be able to put
    /// them anywhere in the current arena." MV-422 deletes the Wall/Gunner split entirely — one
    /// sentinel, one control, no <c>SentinelKind</c> parameter.
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
        /// <summary>The reticle radius, world metres — roughly the sentinel's own footprint.</summary>
        private const float PlacementRadius = 0.9f;

        /// <summary>Keeps the sentinel's own body clear of whatever it's dropped near.</summary>
        private const float ZoneEdgeMargin = 1.5f;

        private Transform _origin;
        private PlayerAbilities _abilities;

        private GameObject _circleGo;

        /// <summary>Wired by <c>HudController</c> right after construction — the same hand-off shape
        /// <see cref="TeleportJoystickControl.Init"/> uses.</summary>
        public void Init(RectTransform knob, Transform origin, PlayerAbilities abilities, Image rings = null)
        {
            _origin = origin;
            _abilities = abilities;
            InitBase(knob, origin, rings);
        }

        /// <summary>True while the placement circle is actually showing — the same test hook
        /// <see cref="TeleportJoystickControl.LandingCircleVisible"/> exposes.</summary>
        public bool PlacementCircleVisible => _circleGo != null && _circleGo.activeSelf;

        protected override bool IsOwned => WeaponSystemState.IsAcquired(AbilityKind.Sentinels);

        protected override bool AbilityReady => _abilities != null && _abilities.SentinelReady;

        /// <summary>Deploys at the SAME point the reticle just previewed (MV-615 fix — this used to
        /// deploy at the full authored range regardless of how far the drag actually reached, so a
        /// half-pulled drag showed a green marker at half range and deployed at full range instead — a
        /// point that was never clearance-checked). Shares <see cref="PreviewDistance"/> with
        /// <see cref="RebuildAimVisual"/> so the two can never disagree.</summary>
        protected override void Fire(Vector3 direction)
        {
            if (_abilities == null || _origin == null) return;

            Vector3 point = PlacementPoint(direction, PreviewDistance());
            _abilities.TryDeploySentinel(point);
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

            _circleGo = new GameObject("Sentinel Placement Circle", typeof(MeshFilter), typeof(MeshRenderer));
            // Marks the renderer as owned so neither RuntimeSurfaceDirector nor CharacterSkinDirector
            // repaints it — same guard Teleport/Water Balloon's own aim meshes carry.
            _circleGo.AddComponent<KeepsOwnMaterial>();
            var renderer = _circleGo.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _circleGo.SetActive(false);
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

        /// <summary>The aimed point's distance from Max for the CURRENT drag — a fraction of the full
        /// placement range, floored so even a barely-armed drag still previews (and would deploy)
        /// somewhere visibly off Max's feet. <see cref="Fire"/> reuses this exact formula (MV-615) so
        /// the deployed point is always the same one the reticle just showed.</summary>
        private float PreviewDistance() => Mathf.Max(0.15f, AbilityTuning.DefaultSentinelPlacementRange * DistanceFraction);

        /// <summary>Rebuilds the placement reticle for the current drag — distance previews toward the
        /// full placement range as the drag comes out (same shape as Teleport's own landing circle),
        /// clamped into the current room, and tinted red (via <see cref="AbilityJoystickControlBase.ApplyArmedTint"/>'s
        /// <c>ready</c> parameter) whenever the aimed point is already occupied, on top of the usual
        /// armed/cost/cap states.</summary>
        protected override void RebuildAimVisual()
        {
            if (_origin == null || _circleGo == null) return;

            Vector3 point = PlacementPoint(Direction, PreviewDistance());

            _circleGo.transform.SetPositionAndRotation(
                new Vector3(point.x, 0.01f, point.z), Quaternion.identity);
            _circleGo.GetComponent<MeshFilter>().sharedMesh = WaterBalloonAimMesh.BuildLandingCircle(PlacementRadius);

            bool validSpot = _abilities == null || _abilities.IsValidSentinelPlacement(point);
            ApplyArmedTint(_circleGo, IsArmed, AbilityReady && validSpot);
        }
    }
}
