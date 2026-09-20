using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-860 — every Replicator built into World 2 today is IN = south (<c>Replicator</c> has no
    /// facing concept at all: <c>HatchOutwardNormal</c>/<c>OutputOutwardNormal</c> read
    /// <c>transform.forward</c>, but nothing ever rotates it away from the identity every box is spawned
    /// at). Lee's World 2 v3 sheet needs boxes facing every side. Fails to COMPILE on the pre-fix
    /// tree — <c>Replicator.SetFacing</c> does not exist there at all, the same "no such member"
    /// base-commit failure this file's own MV-706/MV-808 siblings already use. Tier 2 (resolved
    /// values): both assertions read a resolved Transform position derived from the rotation
    /// <c>SetFacing</c> applies, never an authored constant.
    /// </summary>
    public sealed class MV860ReplicatorFacingTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test uses.
        private static readonly Vector3 RigOrigin = new Vector3(51204f, 0f, -27680f);

        private GameObject _replicatorGo;

        [TearDown]
        public void TearDown()
        {
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
        }

        [Test]
        public void FacingEast_ResolvesLureEastOfTheBox_AndEmitsTheTwinWest()
        {
            LogAssert.ignoreFailingMessages = true; // BuildBody's collider-strip [Error], every Replicator test carries this

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's own authored footprint

            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.SetFacing("E");
            replicator.Build(); // AddComponent's own Awake never runs outside Play mode

            Vector3 lure = replicator.QueueSlotPosition(0);
            Vector3 twin = replicator.OutRampFootPosition;

            Assert.Greater(lure.x, RigOrigin.x + 1f,
                "facing E: the lure point must resolve east of the box, on the IN face's own side");
            Assert.Less(twin.x, RigOrigin.x - 1f,
                "facing E: the twin must emit west of the box, on the OUT face — the opposite side");
            Assert.AreEqual(RigOrigin.z, lure.z, 0.2f,
                "facing E: the lure point must stay within 0.2 m of the box's own z");
            Assert.AreEqual(RigOrigin.z, twin.z, 0.2f,
                "facing E: the twin's emit point must stay within 0.2 m of the box's own z");
        }
    }
}
