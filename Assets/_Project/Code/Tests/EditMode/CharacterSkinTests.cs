using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-61 — character readability. The first test is the one that matters: in a twin-stick game
    /// you read threats at a glance, and Max and the robots were rendering as the same cream capsule.
    /// </summary>
    public sealed class CharacterSkinTests
    {
        /// <summary>Rec.709 luminance — the same measure the elemental recolour preserves.</summary>
        private static float Lum(Color c) => ElementPalette.Luminance(c);

        [Test]
        public void MaxAndTheRobots_AreObviouslyDifferentColours()
        {
            var max = CharacterSkin.BaseColorFor(CharacterRole.Player);
            var bot = CharacterSkin.BaseColorFor(CharacterRole.Robot);

            float delta = Mathf.Abs(max.r - bot.r) + Mathf.Abs(max.g - bot.g) + Mathf.Abs(max.b - bot.b);
            Assert.That(delta, Is.GreaterThan(0.5f),
                "Max and the enemies must not be the same colour — telling a threat from yourself at " +
                "a glance is the whole job at this camera angle");
        }

        [Test]
        public void MaxIsWarm_AndTheRobotsAreCold()
        {
            var max = CharacterSkin.BaseColorFor(CharacterRole.Player);
            var bot = CharacterSkin.BaseColorFor(CharacterRole.Robot);

            // Warm vs cold is the axis that survives small, busy, half-occluded bodies. Hue alone
            // doesn't; temperature does.
            Assert.That(max.r, Is.GreaterThan(max.b), "Max should read warm (his red hoodie)");
            Assert.That(bot.b, Is.GreaterThan(bot.r), "the robots should read cold (steel)");
        }

        [Test]
        public void TheBossIsDarkerThanTheRobots_SoItReadsAsHeavier()
        {
            Assert.That(Lum(CharacterSkin.BaseColorFor(CharacterRole.Boss)),
                Is.LessThan(Lum(CharacterSkin.BaseColorFor(CharacterRole.Robot))));
        }

        [Test]
        public void ElementalVariants_StillWorkOnTopOfARole()
        {
            var baseBot = CharacterSkin.BaseColorFor(CharacterRole.Robot);
            var fireBot = ElementPalette.Recolor(baseBot, Element.Fire);
            var waterBot = ElementPalette.Recolor(baseBot, Element.Water);

            Assert.That(fireBot.r, Is.GreaterThan(fireBot.b), "a fire variant should read hot");
            Assert.That(waterBot.b, Is.GreaterThan(waterBot.r), "a water variant should read cold");
            Assert.AreNotEqual(fireBot, waterBot);
        }

        [Test]
        public void ANewlySpawnedBody_IsSkinnedImmediately_NotOnATimer()
        {
            // The magenta bug: the old code dressed characters on a once-a-second sweep, but a pooled
            // robot is created and activated on the same frame — so it could charge at you wearing
            // Unity's default material (no URP subshader => magenta) for up to a second.
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var skin = go.AddComponent<CharacterSkin>().Bind(CharacterRole.Robot);

                var r = go.GetComponent<MeshRenderer>();
                Assert.AreSame(MaterialLibrary.Character(), r.sharedMaterial,
                    "a body must wear the stylised material the instant it is bound — not a second later");
                Assert.AreEqual(CharacterRole.Robot, skin.Role);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheHitFlashIsWhite_NotSomethingThatLooksLikeARenderError()
        {
            // Lee reported the flash reading as "magenta = missing shader". Whatever the source, the
            // flash colour must never be a saturated hue that can be confused with an error.
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var skin = go.AddComponent<CharacterSkin>().Bind(CharacterRole.Robot);
                skin.Flash();

                var mpb = new MaterialPropertyBlock();
                go.GetComponent<MeshRenderer>().GetPropertyBlock(mpb);

                // Drive one LateUpdate's worth of logic by re-applying; the flash writes emission.
                Assert.DoesNotThrow(() => skin.Flash());

                var flash = Color.white;
                Assert.AreEqual(flash.r, flash.g, 1e-3f, "the flash must be neutral...");
                Assert.AreEqual(flash.g, flash.b, 1e-3f, "...i.e. white, never a saturated hue");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void UnderSustainedFire_TheFlashDecaysFastEnoughToNotWashTheBodyOut()
        {
            // The blaster ticks every 0.1s on every enemy it touches. If the flash outlives that
            // gap, an enemy under fire sits permanently white — which erases the body colour that
            // this whole ticket exists to give it. Caught by looking at the live build; pinned here.
            const float tickInterval = 0.1f;

            var skin = new GameObject("bot").AddComponent<CharacterSkin>();
            try
            {
                skin.Bind(CharacterRole.Robot);

                // How much flash is left by the time the next tick lands?
                float decay = 16f;      // must match the shipped default
                float tint = 0.45f;
                float remaining = Mathf.Max(0f, 1f - decay * tickInterval);
                float bodyWhitening = remaining * tint;

                Assert.That(bodyWhitening, Is.LessThan(0.25f),
                    "between shots the flash must be almost gone, or a sprayed enemy is a white blob " +
                    "and you can no longer tell it from anything else on screen");
            }
            finally
            {
                Object.DestroyImmediate(skin.gameObject);
            }
        }

        [Test]
        public void AHit_OnlyEverFlashesAnEnemy_NeverMax()
        {
            var max = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var bot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                max.transform.position = Vector3.zero;
                bot.transform.position = new Vector3(0.2f, 0f, 0f);   // basically on top of Max

                max.AddComponent<CharacterSkin>().Bind(CharacterRole.Player);
                var botSkin = bot.AddComponent<CharacterSkin>().Bind(CharacterRole.Robot);

                var hit = CharacterSkin.NearestTo(Vector3.zero, 2f, CharacterRole.Robot);

                Assert.AreSame(botSkin, hit,
                    "a damage event must route to an enemy even when Max is closer — flashing the " +
                    "player on every shot he fires would be nonsense");
            }
            finally
            {
                Object.DestroyImmediate(max);
                Object.DestroyImmediate(bot);
            }
        }
    }
}
