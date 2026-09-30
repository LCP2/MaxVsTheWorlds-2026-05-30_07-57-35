using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1012 — World 3's UNDERTOW was never actually attached to Max: <see cref="PlayerController.Awake"/>
    /// self-attached <see cref="PlayerAbilities"/>, <see cref="MaxWorlds.Upgrades.ShoulderRack"/> and
    /// <see cref="PulseLaser"/> but never <see cref="Undertow"/>, so <see cref="WaterBlaster"/> (baked once
    /// into the scene, gated only on "not Lppe") kept firing the RCDA stream straight through World 3's
    /// morph. This proves the RESOLVED firing behaviour off a player built through the real entry point —
    /// <see cref="PlayerController.Awake"/>, invoked the same reflection-idiom every other EditMode test in
    /// this project uses to trigger Unity lifecycle methods that don't run for AddComponent outside Play
    /// mode (see <c>MV503StuckDiagnosticTests</c>) — never a hand-set field standing in for the self-attach
    /// or the aim-source fallback this ticket also adds.
    ///
    /// MV-1034 removed <c>CavitationImplosion</c> entirely (the charge/cavitation shot "does not work as a
    /// design", Lee), so the line-of-sight cover case this test used to also cover (item 4 of MV-1012's own
    /// ticket) is culled along with the code it guarded — see <c>UndertowTests</c>' own doc comment for the
    /// same cull.
    ///
    /// Fails on base commit 4436916 (today, before this ticket): (1) <c>player.GetComponent&lt;Undertow&gt;()</c>
    /// is null — <c>PlayerController.Awake</c> never attaches one; (2) even attached directly, <c>Undertow</c>
    /// has no <c>ActivePrimary</c> gate, so it keeps emitting under World 2's LPPE morph; (3) <c>WaterBlaster</c>
    /// still emits under World 3's UNDERTOW morph (its carve-out comment says so explicitly).
    /// </summary>
    public sealed class MV1012UndertowAttachAndCoverGateTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowUpdate =
            typeof(Undertow).GetMethod("Update", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo WaterBlasterUpdate =
            typeof(WaterBlaster).GetMethod("Update", NonPublicInstance);

        private GameObject _playerGo;
        private GameObject _wbGo;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_wbGo != null) Object.DestroyImmediate(_wbGo);
        }

        [Test]
        public void UndertowSelfAttachesAndGatesOnActivePrimary()
        {
            // --- AC1/AC3: PlayerController.Awake -- the real entry point -- attaches a live Undertow.
            _playerGo = new GameObject("MV1012 Player", typeof(CharacterController), typeof(PlayerController));
            var player = _playerGo.GetComponent<PlayerController>();
            PlayerControllerAwake.Invoke(player, null);

            var undertow = _playerGo.GetComponent<Undertow>();
            Assert.IsNotNull(undertow,
                "PlayerController.Awake must self-attach a live Undertow the same way it self-attaches PulseLaser");

            // Undertow's own Awake doesn't run for AddComponent outside Play mode either -- invoke it
            // directly, same as every other self-attached weapon's test in this project. This also
            // resolves its aimSource fallback to the real PlayerController just attached above (ticket
            // item 2) -- never hand-set.
            UndertowAwake.Invoke(undertow, null);

            // --- AC1: World 3's primary is UNDERTOW -- Undertow emits when firing is requested, RCDA doesn't.
            WeaponSystemState.ApplyWeaponCoreMorph(2);

            // DevMode.AutoFire forces IsFiring true the same way PressKitDirector's filming rig does,
            // sidestepping the real Input System (this project bans PlayMode) while still exercising the
            // real self-attached aimSource resolved just above.
            DevMode.Enabled = true;
            DevMode.AutoFire = true;
            UndertowUpdate.Invoke(undertow, null);
            Assert.IsTrue(undertow.IsEmitting,
                "Undertow must be emitting once World 3's primary is active and firing is requested");

            _wbGo = new GameObject("MV1012 WaterBlaster");
            var blaster = _wbGo.AddComponent<WaterBlaster>();
            WaterBlasterAwake.Invoke(blaster, null);
            blaster.SetFiring(true);
            WaterBlasterUpdate.Invoke(blaster, null);
            Assert.IsFalse(blaster.IsEmitting,
                "the RCDA hose must stand down once World 3's UNDERTOW is the active primary -- the old " +
                "carve-out gated WaterBlaster on \"not Lppe\" only, so it kept streaming under Undertow too");

            // --- AC1 continued: World 2's primary is the LPPE -- Undertow must emit nothing.
            WeaponSystemState.ApplyWeaponCoreMorph(1);
            UndertowUpdate.Invoke(undertow, null);
            Assert.IsFalse(undertow.IsEmitting,
                "Undertow must emit nothing once World 2's LPPE is the active primary instead");
        }
    }
}
