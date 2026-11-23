using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1417: canisters are a red hazard drum with 80 HP, flash white on every hit, and explode in a
    /// real fireball. One test, Tier 2 (resolved HP, MaterialPropertyBlock colour, fireball radius).
    /// </summary>
    public sealed class MV1417CanisterLookTests
    {
        private GameObject _root;
        private Canister _canister;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            Canister.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            Canister.ResetRegistry();
        }

        private static float Whiteness(Color c, Color from)
        {
            // fraction of the way from `from` to white, on the channel with the most room to travel
            float best = 0f;
            for (int i = 0; i < 3; i++)
            {
                float room = 1f - from[i];
                if (room > best) { best = room; }
            }
            float travelled = 0f;
            for (int i = 0; i < 3; i++)
                if (1f - from[i] >= best - 1e-4f) travelled = (c[i] - from[i]) / (1f - from[i]);
            return travelled;
        }

        [Test]
        public void RedDrum_80Hp_FlashesOnHit_AndExplodesInAFireball()
        {
            _root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _root.name = "MV-1417 Canister";
            _canister = _root.AddComponent<Canister>();
            _canister.Build();

            Assert.AreEqual(80f, ((IHealthReadout)_canister).HealthCurrent, 0.01f,
                "full health through IHealthReadout is the max HP");

            Color red = new Color32(0xB2, 0x1C, 0x18, 255);
            Color drum = _canister.DrumResolvedColor;
            Assert.AreEqual(red.r, drum.r, 0.05f, "drum red channel");
            Assert.AreEqual(red.g, drum.g, 0.05f, "drum green channel");
            Assert.AreEqual(red.b, drum.b, 0.05f, "drum blue channel");

            _canister.TakeDamage(new DamageInfo(12f, _root.transform.position, Vector3.down, Team.Player));
            Color flashed = _canister.DrumResolvedColor;
            Assert.GreaterOrEqual(Whiteness(flashed, red), 0.6f, $"hit flash colour {flashed}");

            _canister.Tick(0.1f);
            Color back = _canister.DrumResolvedColor;
            Assert.AreEqual(red.r, back.r, 0.05f, "drum back to red (r)");
            Assert.AreEqual(red.g, back.g, 0.05f, "drum back to red (g)");
            Assert.AreEqual(red.b, back.b, 0.05f, "drum back to red (b)");

            _canister.TakeDamage(new DamageInfo(80f, _root.transform.position, Vector3.down, Team.Player));
            for (float t = 0f; t < 1.05f; t += 0.1f) _canister.Tick(0.1f);
            Assert.IsNotNull(_canister.Fireball, "fireball exists once the fuse elapses");
            _canister.Tick(0.1f);
            _canister.Tick(0.1f);
            Assert.GreaterOrEqual(_canister.FireballRadius, 2.0f, "fireball radius after 0.2 s");
        }
    }
}
