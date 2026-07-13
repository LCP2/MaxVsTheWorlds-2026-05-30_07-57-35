using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The actors are the only loud thing on the screen (YT-86).
    ///
    /// The yard spends nothing on contrast — a restrained green, a restrained brown, every albedo held
    /// under a sunlit ceiling (YT-69, YT-77). That is deliberate, and it is only half a plan. The other
    /// half is that the things you have to READ get the whole contrast budget, and they did not have
    /// it: measured off the shipped build, a robot rendered (160,159,160) and a stepping stone rendered
    /// (125,109,101). The enemies were the same colour as the scenery they were walking over.
    /// </summary>
    public sealed class ActorReadabilityTests
    {
        private static readonly CharacterRole[] Actors =
        {
            CharacterRole.Player,
            CharacterRole.Robot,
            CharacterRole.Bruiser,
        };

        /// <summary>How much colour a colour actually has. Grey is 0.</summary>
        private static float Saturation(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return max <= 1e-4f ? 0f : (max - min) / max;
        }

        private static float Distance(Color a, Color b) =>
            Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        [TearDown]
        public void TearDown()
        {
            MaterialLibrary.Palette = BiomePalette.Backyard;
            MaterialLibrary.Clear();
        }

        // ------------------------------------------------------------------ loud

        [Test]
        public void EveryActorIsLouderThanTheGroundItStandsOn([ValueSource(nameof(Actors))] CharacterRole role)
        {
            float actor = Saturation(CharacterSkin.BaseColorFor(role));

            Assert.Greater(actor, 0.6f,
                $"{role} has a saturation of {actor:0.00}. This is the figure-ground trade the whole " +
                "look is built on: the yard is quiet SO THAT the actors can be loud. An actor that is " +
                "as washed out as the lawn is an actor nobody can find in a fight.");
        }

        /// <summary>
        /// The one that actually shipped broken. "Cold steel" is a lovely phrase and a grey with no
        /// saturation in it, and a garden is FULL of grey — paving, rocks, stepping stones. The swarm
        /// has to be a colour the yard does not contain.
        ///
        /// Separation is measured in HUE and SATURATION, not as a distance in RGB, because RGB distance
        /// is not how anyone sees this. The first cut of this test asserted on RGB distance and failed
        /// the violet bruiser for being "the colour of stone" — they share a red channel and nothing
        /// else, and no human being would ever confuse them. What actually matters is: against a GREY
        /// (stone, metal), an enemy separates by being vividly coloured at all; against a COLOURED
        /// thing (grass, timber, soil), it separates by being a different colour.
        /// </summary>
        [Test]
        public void NoEnemyIsTheColourOfTheScenery()
        {
            var palette = BiomePalette.Backyard;

            foreach (var role in new[] { CharacterRole.Robot, CharacterRole.Bruiser })
            {
                Color body = CharacterSkin.BaseColorFor(role);
                Color.RGBToHSV(body, out float bodyHue, out float bodySat, out _);

                foreach (var (name, scenery) in new[]
                {
                    ("stone", palette.Stone),
                    ("metal", palette.Metal),
                    ("timber", palette.Wood),
                    ("soil", palette.Dirt),
                    ("grass", palette.GroundAccent),
                })
                {
                    Color.RGBToHSV(scenery, out float sceneHue, out float sceneSat, out _);

                    if (sceneSat < 0.3f)
                    {
                        // A grey. The enemy separates from it by HAVING a colour — which is exactly
                        // what the shipped "cold steel" robot did not have.
                        Assert.Greater(bodySat - sceneSat, 0.4f,
                            $"the {role} is as washed-out as the yard's {name}. That is the bug this " +
                            "ticket exists to fix: a robot rendered (160,159,160) and a stepping stone " +
                            "rendered (125,109,101), and the swarm read as gravel.");
                        continue;
                    }

                    // A coloured thing. The enemy separates from it by being a DIFFERENT colour.
                    float hue = Mathf.Abs(bodyHue - sceneHue) * 360f;
                    if (hue > 180f) hue = 360f - hue;

                    Assert.Greater(hue, 50f,
                        $"the {role} is the same colour family as the yard's {name} ({hue:0}° apart). " +
                        "It walks over that, all game, at twenty pixels across on a phone.");
                }
            }
        }

        [Test]
        public void MaxIsTheOnlyWarmThingOnTheField()
        {
            Color max = CharacterSkin.BaseColorFor(CharacterRole.Player);
            Assert.Greater(max.r, max.g + 0.3f, "Max is not warm. Warm is his half of the axis.");
            Assert.Greater(max.r, max.b + 0.3f, "Max is not warm.");

            foreach (var role in new[] { CharacterRole.Robot, CharacterRole.Bruiser, CharacterRole.Boss })
            {
                Color c = CharacterSkin.BaseColorFor(role);
                Assert.Greater(Mathf.Max(c.g, c.b), c.r,
                    $"the {role} is warm. Warm vs cold is the axis that survives being small, busy and " +
                    "half-occluded — the moment an enemy goes warm, it is competing with the player " +
                    "for the one cue that still works when the screen is full.");
            }
        }

        /// <summary>
        /// "Not just tinted variants" — the ticket's words. A rusher and a bruiser want completely
        /// different responses (kite one, commit three seconds of spray to the other), so telling them
        /// apart cannot depend on noticing that one is slightly darker.
        /// </summary>
        [Test]
        public void TheTwoEnemyKinds_AreNotTintsOfEachOther()
        {
            Color rusher = CharacterSkin.BaseColorFor(CharacterRole.Robot);
            Color bruiser = CharacterSkin.BaseColorFor(CharacterRole.Bruiser);

            Assert.Greater(Distance(rusher, bruiser), 0.5f,
                "the two enemy kinds are nearly the same colour. They demand opposite responses — you " +
                "kite one and you spend three seconds of held spray on the other — so a player who " +
                "cannot tell them apart cannot play the fight.");

            float rusherValue = Mathf.Max(rusher.r, Mathf.Max(rusher.g, rusher.b));
            float bruiserValue = Mathf.Max(bruiser.r, Mathf.Max(bruiser.g, bruiser.b));
            Assert.Greater(Mathf.Abs(rusherValue - bruiserValue), 0.0f,
                "they are the same brightness as well as being close in hue — nothing separates them.");
        }

        // ------------------------------------------------------------------ the edge

        [Test]
        public void TheCharacterMaterial_HasAnEdgeLoudEnoughToSee()
        {
            var m = MaterialLibrary.Character();

            Assert.Greater(m.GetFloat("_RimStrength"), 1f,
                "the rim is too quiet. At a fixed top-down camera a body's SILHOUETTE is most of what " +
                "you can see of it, so lighting the edge is what separates it from the ground.");

            // Higher power = tighter to the silhouette. The first cut set this low and the rim became a
            // wash across the whole body: Max stopped being orange and became a pale smear.
            Assert.GreaterOrEqual(m.GetFloat("_RimPower"), 3.5f,
                "the rim is a wash, not an edge — it will spill across the body and eat the very colour " +
                "it exists to frame.");

            Assert.GreaterOrEqual(m.GetFloat("_OutlineWidth"), 0.010f,
                "the outline is a hairline. It is measured in SCREEN space precisely so it survives the " +
                "camera pulling back (YT-82) — but only if it is thick enough to survive a phone.");
        }

        // ------------------------------------------------------------------ the kind is the truth

        /// <summary>
        /// A body's colour follows its KIND, and it re-reads it every time it is switched on.
        ///
        /// The enemies are POOLED. A body is a rusher, dies, goes back in the pool, and comes out again
        /// as a bruiser — same GameObject, same CharacterSkin, new archetype. Bind the colour once at
        /// spawn and the yard fills with turquoise fridges and violet rushers, each one lying about
        /// what it is and how hard it hits.
        /// </summary>
        [Test]
        public void APooledBody_ComesBackWearingTheKindItActuallyIs()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var enemy = go.AddComponent<RobotEnemy>();
                enemy.Apply(EnemyArchetype.Rusher);

                var skin = go.AddComponent<CharacterSkin>().Bind(CharacterRole.Robot);
                Assert.AreEqual(CharacterRole.Robot, skin.Role, "it did not come out as a rusher.");
                Color asRusher = skin.BodyColor;

                // Back in the pool; out again as the other kind.
                enemy.Apply(EnemyArchetype.Bruiser);
                skin.Apply();

                Assert.AreEqual(CharacterRole.Bruiser, skin.Role,
                    "a recycled body came back as a bruiser and kept the rusher's role.");
                Assert.AreNotEqual(asRusher, skin.BodyColor,
                    "it is a bruiser now and it is still wearing the rusher's colour — it will read as " +
                    "something you can kite, right up until it doesn't die.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------ the flash

        /// <summary>
        /// A hit flashes ANY enemy, and never Max.
        ///
        /// The flash used to be routed by asking for the nearest body of one hard-coded role: the
        /// rusher. Giving the bruiser a role of its own would have silently stopped every bruiser in
        /// the game from flashing — you would pour water into the toughest thing in the fight, the one
        /// that costs three seconds of held spray, with no confirmation that any of it was landing.
        /// </summary>
        [Test]
        public void AHit_FlashesEitherKindOfEnemy_AndNeverMax()
        {
            var bruiser = NewSkin(CharacterRole.Bruiser, new Vector3(10f, 0f, 0f));
            var rusher = NewSkin(CharacterRole.Robot, new Vector3(-10f, 0f, 0f));
            var max = NewSkin(CharacterRole.Player, new Vector3(0f, 0f, 0f));

            try
            {
                Assert.AreSame(bruiser, CharacterSkin.NearestEnemy(new Vector3(10.2f, 0f, 0f), 1.6f),
                    "a hit on the bruiser found nothing to flash.");
                Assert.AreSame(rusher, CharacterSkin.NearestEnemy(new Vector3(-10.2f, 0f, 0f), 1.6f),
                    "a hit on the rusher found nothing to flash.");
                Assert.IsNull(CharacterSkin.NearestEnemy(Vector3.zero, 1.6f),
                    "a hit next to MAX found Max. The player must never flash as though he were the " +
                    "thing being shot.");
            }
            finally
            {
                Object.DestroyImmediate(bruiser.gameObject);
                Object.DestroyImmediate(rusher.gameObject);
                Object.DestroyImmediate(max.gameObject);
            }
        }

        private static CharacterSkin NewSkin(CharacterRole role, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = role.ToString();
            go.transform.position = at;
            return go.AddComponent<CharacterSkin>().Bind(role);
        }
    }
}
