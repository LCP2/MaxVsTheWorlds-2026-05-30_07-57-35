using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The boss's ground marks have to be ON the ground (YT-113).
    ///
    /// They were not. A damage zone is a damage SPHERE, and its origin sits at the height of
    /// whatever spawned it — blade-rain is placed at y=1, grass at the boss's mid-body — so a visual
    /// drawn at the zone's own position hung in the air above the lawn. Reported as "white circles
    /// around the player" and "green circles around the boss"; they were the boss's attacks, drawn a
    /// metre too high.
    ///
    /// These assert world height and the lift ORDER, because that is what "lies on the grass and
    /// does not fight the marks around it" actually means.
    /// </summary>
    public sealed class GroundDecalPlayTests
    {
        private DamageZone _zone;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_zone != null) Object.Destroy(_zone.gameObject);
            yield return null;
        }

        private DamageZone SpawnAt(Vector3 pos)
        {
            _zone = DamageZone.Spawn(pos, radius: 1.5f, damage: 5f, life: 5f, armDelay: 0.3f,
                                     color: new Color(0.8f, 0.8f, 0.85f, 0.8f));
            return _zone;
        }

        private static Transform Visual(DamageZone zone)
        {
            var mr = zone.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(mr, "the zone drew nothing at all");
            return mr.transform;
        }

        /// <summary>Blade-rain: spawned at chest height, scattered around Max.</summary>
        [UnityTest]
        public IEnumerator ABladeZoneSpawnedAtChestHeightStillDrawsOnTheLawn()
        {
            DamageZone zone = SpawnAt(new Vector3(4f, 1f, 2f));
            yield return null;

            float y = Visual(zone).position.y;
            Assert.That(y, Is.LessThan(0.1f),
                        $"the mark is floating {y:0.00} m above the grass — this is the white circle");
            Assert.That(y, Is.GreaterThan(0f), "co-planar with the lawn would z-fight");
        }

        /// <summary>Grass clippings: spawned at the boss's mid-body, which is higher still.</summary>
        [UnityTest]
        public IEnumerator AGrassZoneSpawnedAtTheBossesMidBodyStillDrawsOnTheLawn()
        {
            DamageZone zone = SpawnAt(new Vector3(-2f, 1.6f, 5f));
            yield return null;

            Assert.That(Visual(zone).position.y, Is.LessThan(0.1f),
                        "the grass puddle is hanging in the air where the boss's belly is");
        }

        /// <summary>It must still be where the attack landed, in the plane that matters.</summary>
        [UnityTest]
        public IEnumerator TheMarkStaysOverTheGroundItThreatens()
        {
            DamageZone zone = SpawnAt(new Vector3(4f, 1f, 2f));
            yield return null;

            Vector3 p = Visual(zone).position;
            Assert.That(p.x, Is.EqualTo(4f).Within(0.01f));
            Assert.That(p.z, Is.EqualTo(2f).Within(0.01f));
        }

        /// <summary>
        /// A boss attack must draw UNDER the telegraph that warns about it and OVER the always-on
        /// footprint rings. Get this backwards and a decoration hides the one mark the player has to
        /// react to.
        /// </summary>
        [UnityTest]
        public IEnumerator TheZoneSitsBetweenTheFootprintRingsAndTheTelegraph()
        {
            Assert.That(DamageZone.ZoneLift, Is.LessThan(GroundRing.GroundLift),
                        "the zone would cover the danger telegraph drawn on top of it");
            Assert.That(DamageZone.ZoneLift, Is.GreaterThan(GroundAnchorTuning.RingLift),
                        "a footprint ring would cover the boss's attack");
            yield return null;
        }

        /// <summary>
        /// The material is right on the frame it is born, not a frame later. Grass puddles spawn
        /// every 0.18s through a charge, so "the sweep will fix it next frame" meant there was
        /// always one on screen that it hadn't fixed yet.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMarkIsNeverDrawnWithoutAMaterial()
        {
            DamageZone zone = SpawnAt(new Vector3(1f, 1f, 1f));

            var mr = zone.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(mr.sharedMaterial, "spawned with no material — that frame draws white");
            Assert.IsNotNull(mr.sharedMaterial.mainTexture,
                             "an untextured disc is a flat white circle, which is the bug reported");
            yield return null;
        }
    }
}
