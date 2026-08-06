using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The HUD minimap against the real shipped map (MV-264): <see cref="MinimapModelTests"/> proves
    /// the fog-of-war maths in isolation; this proves <see cref="HudController"/> actually finds the
    /// live <see cref="BackyardPath"/>/<see cref="MaxWorlds.Enemies.AreaAccumulationDirector"/> and
    /// draws it — including across the Awake-order gap between the two components, which Unity does
    /// not promise resolves in the same frame.
    /// </summary>
    public sealed class MinimapPlayTests
    {
        private GameObject _path, _player, _hudGo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevTuning.Reset();
            HudController.SkipTouchControlsForTests = true;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hudGo != null) Object.Destroy(_hudGo);
            if (_path != null) Object.Destroy(_path);
            if (_player != null) Object.Destroy(_player);
            HudController.SkipTouchControlsForTests = false;
            DevTuning.Reset();
            yield return null;
        }

        /// <summary>Same shape as <see cref="MapPlayTests.BuildLevelFromTheMap"/>: a tagged player,
        /// then <see cref="BackyardPath"/> to build the shipped map on top of it. The HUD is added
        /// last and given an extra frame so <see cref="HudController.EnsureMinimapBuilt"/> — which
        /// only runs from <c>Update</c> — has a chance to fire.</summary>
        private IEnumerator BuildLevelAndHud()
        {
            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _player.name = "Max (Greybox)";
            _player.tag = "Player";
            _player.AddComponent<CharacterController>();
            yield return null;

            _path = new GameObject("Backyard Path", typeof(BackyardPath));
            yield return null;

            _hudGo = new GameObject("HUD");
            _hudGo.AddComponent<HudController>();
            yield return null;
            yield return null;
        }

        private HudController Hud() => _hudGo.GetComponent<HudController>();

        [UnityTest]
        public IEnumerator TheStrip_HasOnePipPerShippedArea_AndStartsWithOnlyAreaOneCurrent()
        {
            yield return BuildLevelAndHud();

            AreaVisibility[] states = Hud().MinimapStates;
            Assert.AreEqual(10, states.Length, "the shipped backyard_slice map defines 10 areas");
            Assert.AreEqual(AreaVisibility.Current, states[0], "a fresh run should mark area 1 current");
            for (int i = 1; i < states.Length; i++)
                Assert.AreEqual(AreaVisibility.Hidden, states[i], $"area {i + 1} should still be hidden");
        }

        [UnityTest]
        public IEnumerator BreakingTheFirstGate_RevealsArea2_AndKeepsArea1Visited()
        {
            yield return BuildLevelAndHud();

            AreaGate gate1 = null;
            foreach (AreaGate g in _path.GetComponentsInChildren<AreaGate>())
                if (g.name == "gate1") gate1 = g;
            Assert.IsNotNull(gate1, "the shipped map built no 'gate1' to break");

            gate1.TakeDamage(new DamageInfo(gate1.MaxHp, Vector3.zero, Vector3.forward,
                Team.Player, source: DamageSource.PrimaryWeapon));
            yield return null; // HudController.Update repaints the strip off the new CurrentArea

            AreaVisibility[] states = Hud().MinimapStates;
            Assert.AreEqual(AreaVisibility.Visited, states[0], "area 1 should now read as visited, not current");
            Assert.AreEqual(AreaVisibility.Current, states[1], "area 2 should be marked current the instant its gate opens");
            Assert.AreEqual(AreaVisibility.Hidden, states[2], "area 3 is still ahead — must stay hidden");
        }
    }
}
