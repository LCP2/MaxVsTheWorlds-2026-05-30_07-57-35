using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-793: <c>MaxRig.Follow</c> copied Max's X/Z but hard-coded the rig's world Y to 0 — his drawn
    /// body never left the ground even while his real <c>CharacterController</c> climbed one of World
    /// 2's 33 decks (up to 2.5 m) or 17 ramps. Everything anchored to Max's real transform (the Field,
    /// his health bar, his aim) rose with him; only the drawn kid stayed pinned to the lawn. Never
    /// showed in World 1, which has no decks, so the hard-coded 0 was always the right answer there.
    ///
    /// Fails on base commit 091064c: <c>Follow</c> writes a literal <c>0f</c> into the rig's Y
    /// regardless of Max's actual height, so a Max standing on a 2.5 m deck resolves a rig Y of 0.00,
    /// not ~2.5.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2) — read off the live <c>CharacterController</c>'s own <c>center</c>/<c>height</c>, never a
    /// literal, so this still holds if either is ever retuned. Same reflection idiom
    /// <c>MV730MaxArmsGunWaddleTests</c> already uses for this rig: Awake is not called automatically
    /// outside Play Mode, so <c>PlayerController</c>'s and <c>MaxRig</c>'s Awake, and MaxRig's private
    /// <c>Follow</c>, are invoked directly.
    /// </summary>
    public sealed class MV793RigFollowsHeightTests
    {
        private GameObject _playerGo;
        private GameObject _rigGo;
        private PlayerController _player;
        private MaxRig _rig;
        private CharacterController _cc;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV793-Test", typeof(CharacterController));
            _cc = _playerGo.GetComponent<CharacterController>();
            _player = _playerGo.AddComponent<PlayerController>();
            Invoke(_player, "Awake");

            _rigGo = new GameObject("MaxRig-MV793-Test");
            _rig = _rigGo.AddComponent<MaxRig>();
            Invoke(_rig, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static object Invoke(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        /// <summary>The offset between Max's transform (his capsule CENTRE) and his own feet — read off
        /// the live CharacterController's resolved <c>height</c>/<c>center</c>, never a literal.</summary>
        private float CentreToFeet => _cc.height * 0.5f - _cc.center.y;

        [Test]
        public void RigFollowsMaxsResolvedHeight_NotAHardCodedGround()
        {
            // ---- AC1: standing on a 2.5 m World 2 deck, the rig follows him UP, not just across ----
            const float deckHeight = 2.5f;
            _player.transform.SetPositionAndRotation(
                new Vector3(3f, deckHeight + CentreToFeet, -1f), Quaternion.identity);

            Invoke(_rig, "Follow");

            Assert.That(_rigGo.transform.position.y, Is.EqualTo(deckHeight).Within(0.05f),
                $"Max stands on a 2.5 m deck but his drawn rig resolved to y={_rigGo.transform.position.y:F3} " +
                "- the rig is still pinned near the ground instead of following his resolved controller height.");

            // ---- AC2: standing on flat ground at world y = 0 (World 1's only case, and the base
            // commit's hard-coded 0f), the rig's Y must not move - no regression to the framing every
            // existing screen was measured against ----
            _player.transform.position = new Vector3(5f, CentreToFeet, 2f);   // grounded at world y = 0

            Invoke(_rig, "Follow");

            Assert.That(_rigGo.transform.position.y, Is.EqualTo(0f).Within(0.05f),
                $"Max stands on flat ground (world y=0) but his drawn rig resolved to " +
                $"y={_rigGo.transform.position.y:F3} - ground-level framing shifted.");

            // ---- AC3: X/Z and yaw still track him exactly - only the height math changed ----
            _player.transform.SetPositionAndRotation(new Vector3(4f, 2.5f + CentreToFeet, -6f),
                Quaternion.Euler(0f, 57f, 0f));

            Invoke(_rig, "Follow");

            Assert.That(_rigGo.transform.position.x, Is.EqualTo(4f).Within(0.001f),
                "the rig's X no longer tracks Max exactly.");
            Assert.That(_rigGo.transform.position.z, Is.EqualTo(-6f).Within(0.001f),
                "the rig's Z no longer tracks Max exactly.");
            Assert.That(_rigGo.transform.eulerAngles.y, Is.EqualTo(57f).Within(0.001f),
                "the rig's yaw no longer matches Max's.");

            // ---- AC4: the rig is still a scene-root object, not reparented onto Max - the class doc's
            // reason (CharacterSkinDirector would claim and flat-repaint every part of it) still holds ----
            Assert.IsNull(_rigGo.transform.parent,
                "MaxRig has been reparented - it must stay a scene-root object (see the class doc).");
        }
    }
}
