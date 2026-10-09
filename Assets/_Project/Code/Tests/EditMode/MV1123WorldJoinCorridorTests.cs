using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1123 (the one new test, per CC_AUTONOMY's testing policy): the World 1 -> World 2 join
    /// corridor, built through the real <see cref="WorldJoinSequence.Initialize"/> entry point (same
    /// idiom MV845/849/965/1077 already establish), carries every change this ticket makes: a 5 m
    /// walkway, solid wall-top banks replacing the old floor-level apron, accent-coloured guide lights,
    /// uninterrupted player control across the exit door, a far door that opens on proximity with jamb
    /// lamps, a fade that starts once Max walks past it, and a title card resolved from the destination
    /// config's own <c>world</c> field.
    ///
    /// Fails on base commit 06c0566: the walkway resolves 3 m wide (not 5), no "Bank E"/"Bank W" object
    /// exists at all (<c>Assert.IsTrue(bankE.Length &gt; 0, ...)</c> fails outright), no "Guide Light"
    /// object exists, <see cref="WorldJoinSequence.FarGate"/> does not exist (CS1061), and
    /// <see cref="WorldJoinSequence.TitleWorldLine"/>/<see cref="WorldJoinSequence.TitleNameLine"/> do
    /// not exist either (CS1061).
    /// </summary>
    public sealed class MV1123WorldJoinCorridorTests
    {
        private GameObject _camGo;
        private GameObject _playerGo;
        private GameObject _hudGo;
        private WorldJoinSequence _sequence;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_sequence != null) Object.DestroyImmediate(_sequence.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_hudGo != null)
            {
                // OnDisable isn't reliably invoked for DestroyImmediate outside Play mode (same
                // documented quirk every other HUD-building EditMode test in this suite works around,
                // e.g. HudCellIconPivotOverlapTests) -- without this, HudController stays subscribed to
                // HudSignals forever and a LATER, unrelated test that fires one crashes reaching into
                // this already-destroyed instance's own RectTransforms.
                var hud = _hudGo.GetComponent<HudController>();
                if (hud != null) InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(_hudGo);
            }
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        /// <summary>The point <paramref name="along"/> metres outward from <paramref name="doorMouth"/>
        /// along <paramref name="wall"/>'s own outward axis — same wall-outward math
        /// <see cref="WorldJoinSequence"/> itself uses internally, kept deliberately separate here rather
        /// than exposed from production code (MV849CorridorBlendTests' own copy of this helper).</summary>
        private static Vector3 PointAlong(Vector2 doorMouth, Wall wall, float along) => wall switch
        {
            Wall.N => new Vector3(doorMouth.x, 0f, doorMouth.y + along),
            Wall.S => new Vector3(doorMouth.x, 0f, doorMouth.y - along),
            Wall.E => new Vector3(doorMouth.x + along, 0f, doorMouth.y),
            Wall.W => new Vector3(doorMouth.x - along, 0f, doorMouth.y),
            _ => new Vector3(doorMouth.x, 0f, doorMouth.y),
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Test]
        public void WorldJoinCorridorCarriesEveryMV1123Change()
        {
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(fromCfg, "world1_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            _hudGo = new GameObject("HUD");
            var hud = _hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");

            _sequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _sequence.Initialize(fromCfg, map, entry, fromWorldIndex: 0, player, () => { });

            // ---- AC1d: Max crossed the exit door inside Initialize() (MV-845's own idiom) -- his own
            // control and the HUD must both still be live, not suspended for a scripted walk. ----
            Assert.IsTrue(player.enabled, "PlayerController must stay enabled across the exit door — no scripted walk any more.");
            Assert.IsTrue(_hudGo.activeSelf, "the HUD root must stay active across the exit door.");

            Transform segA = _sequence.CorridorSegmentRoot('A');
            Assert.IsNotNull(segA, "segment A must have been built");
            Transform corridorRoot = segA.parent;
            Assert.IsNotNull(corridorRoot, "segment A must be parented under the corridor's own root");

            // ---- AC1a: the walkway floor resolves 5 m wide. ----
            Transform floorA = segA.Find("Segment A Floor");
            Assert.IsNotNull(floorA, "segment A must build its own floor");
            float resolvedWidth = floorA.GetComponent<Renderer>().bounds.size.z;   // World 1's exit wall is E -> across = Z
            Assert.AreEqual(5f, resolvedWidth, 0.05f, "the walkway's resolved width must be 5 m");

            // ---- AC1b: a solid bank on each side, top level with the wall top, reaching at least 15 m
            // outward and spanning the corridor's whole length plus 6 m past each end; no floor-level
            // apron object survives. ----
            Transform wallA1 = segA.Find("Segment A Wall 1");
            Assert.IsNotNull(wallA1, "segment A must build its own wall");
            float wallTop = wallA1.GetComponent<Renderer>().bounds.max.y;

            Renderer[] allCorridorRenderers = corridorRoot.GetComponentsInChildren<Renderer>(true);
            Assert.IsFalse(allCorridorRenderers.Any(r => r.name.StartsWith("Ground Apron")),
                "no floor-level 'Ground Apron' object may remain");

            Renderer[] banksE = allCorridorRenderers.Where(r => r.name == "Bank E").ToArray();
            Renderer[] banksW = allCorridorRenderers.Where(r => r.name == "Bank W").ToArray();
            Assert.IsNotEmpty(banksE, "a 'Bank E' object must exist outside the east wall");
            Assert.IsNotEmpty(banksW, "a 'Bank W' object must exist outside the west wall");

            foreach (Renderer bank in banksE.Concat(banksW))
            {
                Assert.AreEqual(wallTop, bank.bounds.max.y, 0.02f, $"{bank.name}'s top must sit level with the wall top");
                Assert.GreaterOrEqual(bank.bounds.size.z, 15f - 0.05f, $"{bank.name} must reach at least 15 m outward");
            }

            float bankMinAlong = banksE.Min(r => r.bounds.min.x) - doorMouth.x;
            float bankMaxAlong = banksE.Max(r => r.bounds.max.x) - doorMouth.x;
            Assert.LessOrEqual(bankMinAlong, -6f + 0.05f, "the east bank must reach 6 m before the corridor's own start");
            Assert.GreaterOrEqual(bankMaxAlong, entry.CorridorLength + 6f - 0.05f, "the east bank must reach 6 m past the corridor's own far end");

            // ---- AC1c: at least 12 guide strips, unlit and coloured to World 2's own Status accent. ----
            Renderer[] guideLights = allCorridorRenderers.Where(r => r.name == "Guide Light").ToArray();
            Assert.GreaterOrEqual(guideLights.Length, 12, "at least 12 guide-light strips must exist");

            Color expectedAccent = StormdrainKit.Status;
            foreach (Renderer strip in guideLights)
            {
                Color c = strip.sharedMaterial.GetColor(BaseColorId);
                Assert.AreEqual(expectedAccent.r, c.r, 0.05f, $"{strip.name}'s red channel is not the World 2 accent");
                Assert.AreEqual(expectedAccent.g, c.g, 0.05f, $"{strip.name}'s green channel is not the World 2 accent");
                Assert.AreEqual(expectedAccent.b, c.b, 0.05f, $"{strip.name}'s blue channel is not the World 2 accent");
            }

            // ---- AC1e: the far door and its jamb lamps. At 8 m it is closed and red; at 5 m it is open
            // and green. ----
            Assert.IsNotNull(_sequence.FarGate, "the exit side must build a real far gate");
            // AddComponent never fires Awake as a side effect inside this project's synchronous EditMode
            // harness (confirmed empirically for AreaGate — AreaGateTests.InvokeAwake's own note, and
            // MV997WorldExitDoorTests' own identical call) — without this, _health stays null and
            // ForceOpen() silently no-ops later.
            InvokeLifecycle(_sequence.FarGate, "Awake");
            Renderer lampL = _sequence.gameObject.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "Far Door Lamp L");
            Renderer lampR = _sequence.gameObject.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "Far Door Lamp R");

            player.transform.position = PointAlong(doorMouth, entry.ExitWall, entry.CorridorLength - 8f);
            _sequence.Tick(0.01f);
            Assert.IsFalse(_sequence.FarGate.IsOpen, "at 8 m the far door must still be closed");
            AssertLampColor(lampL, new Color(0.85f, 0.12f, 0.10f), "L", "closed");
            AssertLampColor(lampR, new Color(0.85f, 0.12f, 0.10f), "R", "closed");

            player.transform.position = PointAlong(doorMouth, entry.ExitWall, entry.CorridorLength - 5f);
            _sequence.Tick(0.01f);
            Assert.IsTrue(_sequence.FarGate.IsOpen, "at 5 m the far door must be open");
            AssertLampColor(lampL, new Color(0.208f, 0.878f, 0.420f), "L", "open");
            AssertLampColor(lampR, new Color(0.208f, 0.878f, 0.420f), "R", "open");

            // ---- AC1f: 2 m past the far door starts the fade and leads to FinaleGateCrossed. ----
            bool crossed = false;
            void OnCrossed() => crossed = true;
            MaxWorlds.UI.HudSignals.FinaleGateCrossed += OnCrossed;
            try
            {
                player.transform.position = PointAlong(doorMouth, entry.ExitWall, entry.CorridorLength + 2f);
                for (int i = 0; i < 60 && !crossed; i++) _sequence.Tick(0.05f);   // well past FadeDuration (0.4s)
                Assert.IsTrue(crossed, "walking 2 m past the far door must lead to FinaleGateCrossed");
            }
            finally
            {
                MaxWorlds.UI.HudSignals.FinaleGateCrossed -= OnCrossed;
            }

            // ---- AC1g: the title card strings. ----
            Assert.AreEqual("WORLD 2", _sequence.TitleWorldLine, "the title card's own WORLD line");
            Assert.AreEqual("STORMDRAIN", _sequence.TitleNameLine, "the title card's own name line");
        }

        private static void AssertLampColor(Renderer lamp, Color expected, string side, string state)
        {
            Color c = lamp.sharedMaterial.GetColor(BaseColorId);
            Assert.AreEqual(expected.r, c.r, 0.02f, $"far door lamp {side} red channel wrong while {state}");
            Assert.AreEqual(expected.g, c.g, 0.02f, $"far door lamp {side} green channel wrong while {state}");
            Assert.AreEqual(expected.b, c.b, 0.02f, $"far door lamp {side} blue channel wrong while {state}");
        }
    }
}
