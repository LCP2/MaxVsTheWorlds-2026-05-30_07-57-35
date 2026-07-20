using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Fog of war on the real level (YT-107). The complaint was that a fresh run showed you every
    /// factory and the boss before you had walked anywhere, so the yard had nothing left to find.
    ///
    /// These run against the shipped map and the real MapPanel rather than a stand-in, because the
    /// interesting failure is not "the flag doesn't flip" — it's a landmark that never got marked, or
    /// a marker the panel draws anyway. Both of those look completely fine in a unit test of the
    /// record itself.
    /// </summary>
    public sealed class DiscoveryPlayTests
    {
        private GameObject _path, _player, _gate, _boss, _camera, _hud, _blocker, _hutch, _director;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject go in new[] { _path, _player, _gate, _boss, _camera, _hud, _blocker,
                                              _hutch, _director })
                if (go != null) Object.Destroy(go);

            yield return null;
        }

        /// <summary>The scene's one-of actors, then the map on top — same shape as MapPlayTests.</summary>
        private IEnumerator BuildLevelFromTheMap()
        {
            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _player.name = "Max (Greybox)";
            _player.tag = "Player";

            _gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _gate.name = "SubZone Gate";
            _gate.AddComponent<SubZoneGate>();

            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.name = "Big Bermuda";
            _boss.AddComponent<BigBermudaBoss>();

            yield return null;

            _path = new GameObject("Backyard Path", typeof(BackyardPath));
            yield return null;
            Physics.SyncTransforms();
            yield return null;
        }

        private Camera NewCamera(Vector3 at, Vector3 lookAt)
        {
            _camera = new GameObject("Eye", typeof(Camera));
            _camera.tag = "MainCamera";
            _camera.transform.position = at;
            _camera.transform.LookAt(lookAt);
            return _camera.GetComponent<Camera>();
        }

        // ---------------------------------------------------------------- the level marks its landmarks

        /// <summary>
        /// The one that stops the feature disappearing. <see cref="Discoverable.FoundOn"/> fails OPEN,
        /// so if the map ever stopped marking its landmarks the fog would silently switch itself off
        /// and every other test here would still pass — they'd be asserting about markers that no
        /// longer exist.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShippedMapMarksEveryFactoryTheBossAndTheGate()
        {
            yield return BuildLevelFromTheMap();

            Assert.That(FactoryCensus.All.Count, Is.GreaterThan(0), "the map built no factories");

            foreach (MowerHutch hutch in FactoryCensus.All)
            {
                Assert.IsNotNull(hutch.GetComponent<Discoverable>(),
                                 $"{hutch.name} is not discoverable — it would be on the map from frame one");
                Assert.That(Discoverable.FoundOn(hutch), Is.False, "a fresh run starts unexplored");
            }

            Assert.IsNotNull(_boss.GetComponent<Discoverable>(), "the boss is not discoverable");
            Assert.IsNotNull(_gate.GetComponent<Discoverable>(), "the boss gate is not discoverable");
        }

        // ---------------------------------------------------------------- the map draws nothing yet

        [UnityTest]
        public IEnumerator OnAFreshRun_TheMapShowsNoFactoryBossOrGateMarker()
        {
            yield return BuildLevelFromTheMap();

            _hud = new GameObject("HUD", typeof(RectTransform), typeof(Canvas));
            var panel = new GameObject("MapPanel", typeof(RectTransform)).AddComponent<MapPanel>();
            panel.Build((RectTransform)_hud.transform, new Vector2(150f, 280f), 0.62f, 1.7f);
            yield return null;

            foreach (Transform marker in AllMarkers(panel))
            {
                if (marker.name == "Max") continue;   // the player dot is not a secret
                Assert.That(marker.gameObject.activeSelf, Is.False,
                            $"'{marker.name}' is drawn on the map before Max has found anything");
            }
        }

        /// <summary>Every marker the panel built, by name, whether shown or not.</summary>
        private static Transform[] AllMarkers(MapPanel panel)
        {
            var found = new System.Collections.Generic.List<Transform>();
            foreach (RectTransform rt in panel.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == "Max" || rt.name == "Gate" || rt.name == "Boss" ||
                    rt.name.StartsWith("Factory "))
                    found.Add(rt);

            Assert.That(found.Count, Is.GreaterThan(2), "the panel drew almost no markers — wrong names?");
            return found.ToArray();
        }

        // ---------------------------------------------------------------- what counts as "in view"

        /// <summary>A hutch on its own, with Max and a camera looking straight at it.</summary>
        private IEnumerator NewSightRig(float maxToHutch)
        {
            _hutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutch.name = "Mower Hutch";
            _hutch.transform.position = new Vector3(0f, 1f, maxToHutch);
            _hutch.transform.localScale = new Vector3(3f, 2f, 3f);
            _hutch.AddComponent<Discoverable>();
            _hutch.AddComponent<MowerHutch>();

            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _player.name = "Max";
            _player.tag = "Player";
            _player.transform.position = Vector3.up;

            yield return null;
        }

        private Discoverable Mark => _hutch.GetComponent<Discoverable>();

        [UnityTest]
        public IEnumerator AFactoryInPlainSightIsFound()
        {
            yield return NewSightRig(12f);
            Camera eye = NewCamera(new Vector3(0f, 12f, -6f), _hutch.transform.position);
            yield return null;

            Assert.That(Discovery.InView(eye, _player.transform, Mark), Is.True,
                        "Max is looking straight at it down an empty lane");
        }

        /// <summary>
        /// The camera looks down at ~72° and sees clean over the fences; Max does not. If the sweep
        /// asked only "is it on screen" then walking up to the shed wall would hand you everything
        /// behind it — which is the fog undoing itself at exactly the moment it should hold.
        /// </summary>
        [UnityTest]
        public IEnumerator AFactoryOnScreenButBehindCoverIsNotFound()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            yield return NewSightRig(12f);

            _blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _blocker.name = "Fence";
            _blocker.transform.position = new Vector3(0f, 1f, 6f);
            _blocker.transform.localScale = new Vector3(8f, 2.4f, 0.4f);
            CoverLayer.Assign(_blocker);

            Camera eye = NewCamera(new Vector3(0f, 12f, -6f), _hutch.transform.position);
            Physics.SyncTransforms();
            yield return null;

            Assert.That(GeometryUtility.TestPlanesAABB(
                            GeometryUtility.CalculateFrustumPlanes(eye), Discovery.VolumeOf(Mark)),
                        Is.True, "the fixture is wrong — the hutch must be ON SCREEN for this to mean anything");

            Assert.That(Discovery.InView(eye, _player.transform, Mark), Is.False,
                        "the fence is between Max and the hutch; he cannot have seen it");
        }

        [UnityTest]
        public IEnumerator AFactoryOffScreenIsNotFound_EvenStandingNextToIt()
        {
            yield return NewSightRig(12f);
            Camera eye = NewCamera(new Vector3(0f, 12f, -6f), new Vector3(0f, 0f, -60f)); // facing away
            yield return null;

            Assert.That(Discovery.InView(eye, _player.transform, Mark), Is.False);
        }

        // ---------------------------------------------------------------- once seen, seen for good

        [UnityTest]
        public IEnumerator OnceFound_ItStaysFoundAfterMaxWalksAway()
        {
            yield return NewSightRig(12f);
            var director = new GameObject("Director").AddComponent<DiscoveryDirector>();
            _director = director.gameObject;

            NewCamera(new Vector3(0f, 12f, -6f), _hutch.transform.position);
            yield return null;

            director.Rescan();
            director.Sweep();
            Assert.That(Mark.Found, Is.True, "the sweep never found a hutch in plain sight");

            // Max leaves, and the camera turns its back on the whole thing.
            _player.transform.position = new Vector3(0f, 1f, -80f);
            _camera.transform.LookAt(new Vector3(0f, 0f, -160f));
            Physics.SyncTransforms();
            yield return null;

            director.Sweep();
            Assert.That(Mark.Found, Is.True,
                        "walking away un-discovered the factory — the map must not forget");
        }

        // ---------------------------------------------------------------- the factory's own tells

        [UnityTest]
        public IEnumerator AnUndiscoveredFactoryHidesItsNameBadgeAndCore()
        {
            yield return NewSightRig(12f);
            yield return null;

            Assert.That(BadgeShown(), Is.False,
                        "'MOWER HUTCH' floats 2.7 m up, clear of the shed walls — it gives the objective away");
            Assert.That(CoreShown(), Is.False, "the pulsing core pulls the eye from across the yard");
        }

        [UnityTest]
        public IEnumerator FindingTheFactoryBringsItsTellsBack()
        {
            yield return NewSightRig(12f);
            Mark.Reveal();
            yield return null;

            Assert.That(BadgeShown(), Is.True, "found it, and it still has no health bar or name");
            Assert.That(CoreShown(), Is.True, "found it, and there is nothing to aim at");
        }

        private bool BadgeShown()
        {
            Transform pivot = _hutch.transform.Find("FactoryHealthBar");
            Assert.IsNotNull(pivot, "the factory built no health bar at all");
            return pivot.gameObject.activeSelf;
        }

        private bool CoreShown()
        {
            Transform core = _hutch.transform.Find("VulnerableCore");
            Assert.IsNotNull(core, "the factory built no vulnerable core at all");
            return core.gameObject.activeSelf;
        }
    }
}
