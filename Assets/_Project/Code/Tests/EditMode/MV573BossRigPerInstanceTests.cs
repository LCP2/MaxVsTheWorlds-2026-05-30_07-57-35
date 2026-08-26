using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Map-built bosses used to render as a bare cube (MV-573): <see cref="BigBermudaRig"/> installed
    /// itself once per SCENE — a <c>[RuntimeInitializeOnLoadMethod]</c> singleton that self-located "the"
    /// boss and bailed the moment any rig already existed. World 1 v4 builds six bosses from one map, so
    /// only the FIRST one ever grew a body; every other one stood there as the bare greybox cube
    /// <see cref="BigBermudaBoss.Awake"/> leaves behind. The fix binds a rig to each boss explicitly, at
    /// the point <see cref="MapRuntime"/> builds it, so the count of rigs always matches the count of
    /// bosses — including one built after rigs already exist elsewhere in the scene, the exact case the
    /// old singleton bailed on.
    /// </summary>
    public sealed class MV573BossRigPerInstanceTests
    {
        /// <summary>Entry stub → fight room → boss room, gated behind "all-sheds-destroyed" — same
        /// three-area shape <see cref="MV561MultiBossTests"/> uses, authoring two bosses in the boss room.</summary>
        private static WorldConfig TwoBossWorld() => new WorldConfig
        {
            world = "Test World",
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", role = "normal",
                    origin = new WorldAreaOrigin { x = -20f, z = 0f },
                    size = new WorldAreaSize { w = 40f, d = 20f },
                },
                new WorldArea
                {
                    id = "boss", role = "boss+exit", hasShed = false, garrisonDensity = "none",
                    origin = new WorldAreaOrigin { x = -20f, z = 20f },
                    size = new WorldAreaSize { w = 40f, d = 40f },
                    bosses = new[]
                    {
                        new WorldBoss { id = "boss1", x = -10f, z = 30f },
                        new WorldBoss { id = "boss2", x = 10f, z = 30f },
                    },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void EveryMapBuiltBoss_GetsItsOwnRig_EvenOneCreatedAfterOthersAlreadyExist()
        {
            GameObject root = null;
            GameObject laterBossGo = null;
            try
            {
                // --- two bosses authored in ONE map must each get their own rig, distinctly bound —
                // not one shared rig, which is the MV-573 regression.
                Assert.IsTrue(WorldMapLoader.TryLoad(TwoBossWorld(), out MapData map, out string reason), reason);

                root = new GameObject("MV-573 Rig Probe Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.AreEqual(2, built.Bosses.Count, "the map authored two bosses but MapRuntime built a different count");

                BigBermudaRig[] rigsAfterMapBuild = Object.FindObjectsByType<BigBermudaRig>();
                Assert.AreEqual(2, rigsAfterMapBuild.Length,
                    "two map-built bosses must each get their own rig — a shared/singleton rig is the " +
                    "MV-573 regression that left every boss but the first a bare cube");

                var boundBosses = new HashSet<BigBermudaBoss>();
                foreach (BigBermudaRig rig in rigsAfterMapBuild)
                {
                    Assert.IsNotNull(rig.Boss, "a rig exists that is bound to no boss at all");
                    Assert.IsTrue(built.Bosses.Contains(rig.Boss),
                        "a rig is bound to a boss this build never built");
                    Assert.IsTrue(boundBosses.Add(rig.Boss),
                        "two rigs are bound to the SAME boss instance — exactly the MV-573 singleton bug");
                }

                // --- a THIRD boss, built well after these two (standing in for "a boss added after
                // scene load"), must also get its own rig. The old singleton
                // ([RuntimeInitializeOnLoadMethod] Install()) bailed the instant ANY rig already existed
                // anywhere in the scene — exactly the state these first two rigs put the scene in.
                laterBossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                laterBossGo.transform.position = new Vector3(100f, 2f, 30f);
                BigBermudaBoss laterBoss = laterBossGo.AddComponent<BigBermudaBoss>();

                BigBermudaRig laterRig = BigBermudaRig.CreateFor(laterBoss);
                Assert.IsNotNull(laterRig, "a boss created after others already have rigs got no rig of its own");
                Assert.AreSame(laterBoss, laterRig.Boss, "the later rig is not bound to the later boss");

                BigBermudaRig[] rigsAfterLaterBoss = Object.FindObjectsByType<BigBermudaRig>();
                Assert.AreEqual(3, rigsAfterLaterBoss.Length,
                    "a boss built after others already have rigs must still get its own — the old " +
                    "singleton bailed the instant any rig already existed in the scene, which is the " +
                    "MV-573 bug");
            }
            finally
            {
                foreach (BigBermudaRig rig in Object.FindObjectsByType<BigBermudaRig>())
                    if (rig != null) Object.DestroyImmediate(rig.gameObject);
                if (laterBossGo != null) Object.DestroyImmediate(laterBossGo);
                if (root != null) Object.DestroyImmediate(root);
            }
        }
    }
}
