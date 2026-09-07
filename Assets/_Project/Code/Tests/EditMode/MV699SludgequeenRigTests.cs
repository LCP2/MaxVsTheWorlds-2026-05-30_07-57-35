using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-699 (the ticket's own suggested example, almost verbatim): the Sludgequeen's generated rig
    /// must build a flood plane whose <see cref="SludgeFlow.ScrollSpeed"/> resolves non-zero after
    /// bind — a RESOLVED value (Rule 2 of the testing policy), never the authored
    /// <c>FloodScrollSpeed</c> constant read back at itself.
    ///
    /// Fails to compile (CS1061) on 699ee7d, the base commit this ticket starts from:
    /// <c>SludgequeenRig</c> there is the MV-696 primitives-only greybox and exposes no
    /// <c>Flood</c> property at all — same "compile failure is the proof" shape
    /// <c>MV690StormdrainSludgeTests</c>/<c>MV696SludgequeenFloodTests</c> already use for their own
    /// base commits.
    ///
    /// EditMode only, reflection-driven for <c>Awake</c> (repo convention — a plain MonoBehaviour
    /// never runs it outside Play mode), same idiom <c>MV696SludgequeenFloodTests.NewWokenBoss</c> uses.
    /// </summary>
    public sealed class MV699SludgequeenRigTests
    {
        private GameObject _bossGo;
        private SludgequeenRig _rig;

        [SetUp]
        public void SetUp() => SludgequeenBoss.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_rig != null) Object.DestroyImmediate(_rig.gameObject);
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
            SludgequeenBoss.ResetRegistry();
        }

        [Test]
        public void CreateFor_BuildsAFloodPlaneWhoseScrollSpeedResolvesNonZero()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var boss = go.AddComponent<SludgequeenBoss>();
            _bossGo = go;

            // EditMode never calls Awake on a plain MonoBehaviour — invoke it explicitly, same idiom
            // as MV696SludgequeenFloodTests.NewWokenBoss for the same boss type.
            typeof(SludgequeenBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);
            boss.SetArenaBounds(new Rect(0f, 0f, 44f, 44f));

            _rig = SludgequeenRig.CreateFor(boss);

            Assert.IsNotNull(_rig, "CreateFor must build a rig for a live boss");
            Assert.IsNotNull(_rig.Flood, "the rig must build a flood plane carrying a SludgeFlow");
            Assert.Greater(_rig.Flood.ScrollSpeed.magnitude, 0f,
                "the flood plane's scroll speed must resolve non-zero after bind");
        }
    }
}
