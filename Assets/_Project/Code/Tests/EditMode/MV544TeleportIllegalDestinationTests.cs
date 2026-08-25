using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-544, Lee: "The teleport is confusing for the user - it allows the destination to be set in
    /// areas that Max can't teleport to." Investigating found the deeper bug: an illegal cross-area
    /// blink was not a no-op — <see cref="PlayerAbilities.TryTeleport"/> spent the cooldown, fired
    /// <see cref="HudSignals.MaxTeleported"/>, and slid Max toward the illegal spot via
    /// <see cref="CharacterController.Move"/>, all while reporting success. This is the single EditMode
    /// test the project's testing policy (MV-465 Rule 1) allows for this ticket — one fixture folding
    /// AC1 (illegal refusal), AC2 (legal cross-area warp unchanged) and AC3 (the aim-time refusal
    /// colour contract) into one run, the same shape <see cref="MV493DoorwayWaypointTests"/> and the
    /// MV-530 sibling ticket already use for a multi-assertion single test.
    ///
    /// Fails pre-fix on every count named below: <c>TryTeleport</c> returned true for the illegal
    /// direction, moved Max, spent the cooldown and fired the signal; and <c>RebuildAimVisual</c> had
    /// no illegal-destination colour at all — the illegal-direction aim tint was indistinguishable from
    /// the armed-and-ready tint (plain white), not merely "not yet distinct from not-ready".
    /// </summary>
    public sealed class MV544TeleportIllegalDestinationTests
    {
        private GameObject _max;
        private PlayerAbilities _abilities;
        private GameObject _pathGo;
        private GameObject _gateGo;
        private float _distance;

        /// <summary>Three rooms sharing Max's home: "reachable" behind an always-open (gate-less) link
        /// — MV-544's "legal" case, unchanged behaviour — and "locked" behind a link naming a real,
        /// still-shut <see cref="SubZoneGate"/> — MV-544's illegal case, the one Lee hit in play.</summary>
        private MapData ThreeRoomsOneLocked()
        {
            // 6m rooms (half-width 3m) spaced by the teleport distance (8m by default) leave a
            // clean gap between "home" and each far room — no overlap for ZoneAt to disambiguate.
            var home = new MapZone { id = "home", x = 0f, z = 0f, width = 6f, depth = 6f };
            var reachable = new MapZone { id = "reachable", x = 0f, z = _distance, width = 6f, depth = 6f };
            var locked = new MapZone { id = "locked", x = _distance, z = 0f, width = 6f, depth = 6f };

            return new MapData
            {
                zones = new[] { home, reachable, locked },
                links = new[]
                {
                    new MapLink { from = "home", to = "reachable", doorway = 4f, gate = "" },
                    new MapLink { from = "home", to = "locked", doorway = 4f, gate = "gate1" },
                },
            };
        }

        /// <summary>Points <see cref="EnemyNavigation.Map"/> at <paramref name="map"/> without running
        /// <see cref="BackyardPath.Awake"/> — the same reflection seam <see cref="MV493DoorwayWaypointTests"/>
        /// already relies on.</summary>
        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV544-test-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();   // also resets RigState (MV-457)
            EnemyNavigation.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);

            WeaponSystemState.Acquire(AbilityKind.Teleport);

            _distance = AbilityTuning.TeleportDistance(
                WeaponSystemState.AbilityLevel(AbilityKind.Teleport),
                AbilityTuning.DefaultTeleportBaseDistance,
                AbilityTuning.DefaultTeleportDistancePerLevel);

            InstallMap(ThreeRoomsOneLocked());

            // "gate1" registered but never Open()'d — reads shut, same as a still-locked boss gate.
            _gateGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var gate = _gateGo.AddComponent<SubZoneGate>();
            EnemyNavigation.RegisterGate("gate1", gate);

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _abilities = _max.GetComponent<PlayerAbilities>();
            if (_abilities == null) _abilities = _max.AddComponent<PlayerAbilities>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_max);
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_gateGo != null) Object.DestroyImmediate(_gateGo);
            EnemyNavigation.Reset();
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
        }

        private static PointerEventData At(Vector2 pos) => new PointerEventData(EventSystem.current) { position = pos };

        [Test]
        public void IllegalCrossAreaBlinkIsRefused_LegalOneIsUnchanged_AndTheAimTintTellsThemApart()
        {
            Vector3 illegalDir = Vector3.right;    // -> "locked" room, gate1 still shut
            Vector3 legalDir = Vector3.forward;    // -> "reachable" room, gate-less link, always open

            bool signalFired = false;
            void OnTeleported(Vector3 from, Vector3 to) => signalFired = true;
            HudSignals.MaxTeleported += OnTeleported;

            try
            {
                // ---- AC1: an illegal destination is a hard refusal on every count.
                bool illegalResult = _abilities.TryTeleport(illegalDir);

                Assert.That(illegalResult, Is.False,
                    "TryTeleport must refuse a blink into a still-locked area instead of reporting success");
                Assert.That(Vector3.Distance(_max.transform.position, Vector3.zero), Is.LessThan(0.01f),
                    "a refused blink must not move Max at all — no CharacterController.Move slide either");
                Assert.That(_abilities.TeleportCooldownRemaining, Is.EqualTo(0f),
                    "a refused blink must not spend the cooldown — the player is charged nothing for it");
                Assert.That(signalFired, Is.False,
                    "a refused blink must never announce HudSignals.MaxTeleported");

                // ---- AC2: a legal cross-area blink is completely unaffected by the AC1 fix.
                bool legalResult = _abilities.TryTeleport(legalDir);

                Assert.That(legalResult, Is.True,
                    "a legal, already-open cross-area blink must still succeed");
                Vector3 expectedLanding = new Vector3(0f, 0f, _distance);
                Assert.That(Vector3.Distance(_max.transform.position, expectedLanding), Is.LessThan(0.01f),
                    "a legal blink must land exactly on the aimed point, same as before this ticket");
                Assert.That(_abilities.TeleportCooldownRemaining, Is.GreaterThan(0f),
                    "a legal blink must still spend the cooldown");
                Assert.That(signalFired, Is.True,
                    "a legal blink must still announce HudSignals.MaxTeleported");
            }
            finally
            {
                HudSignals.MaxTeleported -= OnTeleported;
            }

            // ---- AC3: RebuildAimVisual's colour contract — illegal reads as a distinct refusal red,
            // never confusable with the pre-existing not-ready (on cooldown) tint (1, 0.3, 0.25, 0.35).
            // Teleport is now on cooldown from the AC2 call above, so a fresh control/PlayerAbilities
            // pair isolates the aim tint from that cooldown state.
            var freshMax = new GameObject("Max2", typeof(CharacterController), typeof(PlayerController));
            var freshAbilities = freshMax.GetComponent<PlayerAbilities>();
            if (freshAbilities == null) freshAbilities = freshMax.AddComponent<PlayerAbilities>();

            var pad = new GameObject("Teleport Touch", typeof(RectTransform), typeof(Image));
            var control = pad.AddComponent<TeleportJoystickControl>();
            var knob = new GameObject("Knob", typeof(RectTransform)).GetComponent<RectTransform>();
            control.Init(knob, freshMax.transform, freshAbilities);

            try
            {
                control.OnPointerDown(At(Vector2.zero));
                // TeleportJoystickControl.RebuildAimVisual builds landing from _origin.position +
                // Direction * (maxDistance * DistanceFraction); OnDrag's Vector2->Vector3 mapping is
                // (x, y) -> (x, 0, y), so a (200, 0) screen drag aims world +X ("locked", illegal) and
                // a (0, 200) screen drag aims world +Z ("reachable", legal) at full deflection.
                control.OnDrag(At(new Vector2(200f, 0f)));

                var circleField = typeof(TeleportJoystickControl).GetField("_circleGo", BindingFlags.NonPublic | BindingFlags.Instance);
                var circleGo = (GameObject)circleField.GetValue(control);
                Assert.IsNotNull(circleGo, "the landing circle must exist once aiming has started");

                var renderer = circleGo.GetComponent<MeshRenderer>();
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);
                Color illegalTint = mpb.GetColor(Shader.PropertyToID("_BaseColor"));

                Color notReadyTint = new Color(1f, 0.3f, 0.25f, 0.35f);
                Assert.That(illegalTint, Is.Not.EqualTo(notReadyTint),
                    "the illegal-destination tint must not equal the pre-existing not-ready (on cooldown) " +
                    "tint — conflating them would read a blocked destination as merely 'on cooldown'");
                Assert.That(illegalTint.a, Is.GreaterThan(notReadyTint.a),
                    "the illegal tint must read as a clearer refusal than the dim not-ready wash, not a fainter one");

                // Re-aim toward the legal direction and confirm the tint changes back off the refusal red.
                control.OnDrag(At(new Vector2(0f, 200f)));   // -> "reachable", legal
                renderer.GetPropertyBlock(mpb);
                Color legalTint = mpb.GetColor(Shader.PropertyToID("_BaseColor"));
                Assert.That(legalTint, Is.Not.EqualTo(illegalTint),
                    "aiming back over a legal destination must clear the refusal tint");

                control.OnPointerUp(At(new Vector2(0f, 200f)));
            }
            finally
            {
                Object.DestroyImmediate(pad);
                Object.DestroyImmediate(freshMax);
            }
        }
    }
}
