using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1242 (one new test, per CC_AUTONOMY's testing policy): the Helmsman's rig resolves to a 3.0 m
    /// wide upper body (<see cref="HelmsmanRig.BodyRoot"/>'s own combined rendered bounds) in BOTH the
    /// seated Phase 1 and the torn-free, leg-borne Phase 2, and the switch to Phase 2 disables the
    /// throne's rear cable renderers.
    ///
    /// Built at <c>AuthoredBodyWidth</c> (4.5 — the Mothership's own shipped a30 boss size), the same
    /// "build the fixture at the rig's own reference scale" idiom <c>MV1170SludgequeenSizeTests</c> uses,
    /// so a resolved rig scale of 1 isolates this test from that config value ever changing independently.
    ///
    /// Fails on base commit 7da3274: there is no <c>HelmsmanRig</c> in Runtime/VFX, <c>BossKinds</c>'s own
    /// "helmsman" row builds no rig (MV-1239's own comment: "rides the greybox cube"), and
    /// <c>HelmsmanBoss</c> carries neither <c>LensTelling</c> nor <c>FitColliderTo</c>.
    /// </summary>
    public sealed class MV1242HelmsmanLookTests
    {
        private const float AuthoredBodyWidth = 4.5f;

        [Test]
        public void HelmsmanRig_BodyWidthIs3m_InBothPhases_AndCablesDisableInPhase2()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var strayCollider = go.GetComponent<BoxCollider>();
            if (strayCollider != null) UnityEngine.Object.DestroyImmediate(strayCollider);
            go.transform.localScale = Vector3.one * AuthoredBodyWidth;
            var boss = go.AddComponent<HelmsmanBoss>();

            HelmsmanRig rig = HelmsmanRig.CreateFor(boss);
            try
            {
                Assert.IsNotNull(rig, "HelmsmanBoss must build a HelmsmanRig");

                // --- Phase 1 (seated, the default phase a fresh boss resolves to): body width + cables on. ---
                float phase1Width = ResolvedBodyWidth(rig);
                Assert.AreEqual(3.0f, phase1Width, 0.15f,
                    $"Phase 1 resolved body width must be 3.0m +-0.15, measured {phase1Width:F3}");

                MeshRenderer[] cables = rig.CableRenderers;
                Assert.IsTrue(cables.Length > 0, "test precondition: the throne must carry at least one cable renderer");
                Assert.IsTrue(Array.TrueForAll(cables, r => r != null && r.enabled),
                    "Phase 1 (seated) throne cable renderers must be enabled");

                // --- force Phase 2 the same reflection way MV1239HelmsmanBossTests does, then re-tick the rig. ---
                SetPrivateField(boss, "_phase", Enum.Parse(
                    typeof(HelmsmanBoss).GetNestedType("Phase", BindingFlags.NonPublic), "Walking"));
                Assert.IsTrue(boss.IsWalking, "test precondition: boss must report Phase 2 after the reflection set");

                InvokeTick(rig);

                float phase2Width = ResolvedBodyWidth(rig);
                Assert.AreEqual(3.0f, phase2Width, 0.15f,
                    $"Phase 2 resolved body width must be 3.0m +-0.15, measured {phase2Width:F3}");

                Assert.IsTrue(Array.TrueForAll(rig.CableRenderers, r => r != null && !r.enabled),
                    "Phase 2 (torn free) throne cable renderers must be disabled");
            }
            finally
            {
                if (rig != null) UnityEngine.Object.DestroyImmediate(rig.gameObject);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static void InvokeTick(HelmsmanRig rig) =>
            typeof(HelmsmanRig).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, null);

        private static float ResolvedBodyWidth(HelmsmanRig rig)
        {
            var renderers = rig.BodyRoot.GetComponentsInChildren<MeshRenderer>();
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.size.x;
        }
    }
}
