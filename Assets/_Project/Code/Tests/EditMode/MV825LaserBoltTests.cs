using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-825 — Lee: build 302e10e's LPPE bolt reads as a giant orange arrow sign, not a weapon: a
    /// 0.9m chord lying ACROSS the travel direction with a 0.20m forward bow, and the trail spilling
    /// out of the chord's own midpoint like an arrow's shaft. "Make this look like a laser. Make it
    /// sleek, bright, crackling." Rebuilt straight, ALONG the travel axis instead: a thin white-hot
    /// core (drawn twice for intensity), a soft additive glow sheath around it, three crackling
    /// filaments, and a trail now emitted from the bolt's own TAIL. This is the one new EditMode test
    /// the testing policy allows this ticket, asserting entirely from resolved renderer/mesh state
    /// (MV-465 Tier 2), never an authored constant.
    ///
    /// Fails on 302e10e (pre-fix): <c>SeekerPulse</c> builds the MV-815 crescent -- there is no "Sheath"
    /// child at all (the crescent is one single "Bolt" mesh) and no <c>LineRenderer</c> children, so the
    /// very first "test precondition" assertion below (the "Sheath" child) fails outright.
    /// </summary>
    public sealed class MV825LaserBoltTests
    {
        [TearDown]
        public void TearDown() => SeekerPulse.ResetForTests();

        [Test]
        public void BoltIsAStraightLaserWithSheathCrackleAndTailTrail_CachedAndForkedBlue()
        {
            CombatVfxTuning.LppeBoltTuning tuning = CombatVfxTuning.LppeBolt();

            // Long lifetime, unlike the other SeekerPulse fixtures' usual 0.01f -- this test ticks the
            // pulse by 0.05s (c) to force a crackle re-randomise, and a 0.01s lifetime would retire
            // (and destroy) it inside that very same Tick call, before (c)/(d) can read anything back.
            SeekerPulse pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f,
                turnRateDegPerSec: 360f, lifetime: 1f, damage: 9f, lockRange: 14f, lockHalfAngleDeg: 35f);
            try
            {
                // --- (a) the core: 1.4m along travel, <=0.06m across ---
                Transform core = pulse.transform.Find("Bolt");
                Assert.IsNotNull(core, "test precondition: SeekerPulse must build a child named 'Bolt' (the core)");
                Bounds coreBounds = core.GetComponent<MeshRenderer>().bounds;
                Assert.That(coreBounds.size.z, Is.EqualTo(1.4f).Within(0.05f),
                    $"core length along travel ({coreBounds.size.z:0.000}m) is not 1.4m +/-0.05");
                Assert.That(Mathf.Max(coreBounds.size.x, coreBounds.size.y), Is.LessThanOrEqualTo(0.06f),
                    $"core cross-section ({Mathf.Max(coreBounds.size.x, coreBounds.size.y):0.000}m) exceeds 0.06m");

                // --- (b) every renderer except the sheath stays <=0.12m across travel; sheath <=0.22m ---
                Transform sheath = pulse.transform.Find("Sheath");
                Assert.IsNotNull(sheath, "test precondition: SeekerPulse must build a child named 'Sheath'");
                Bounds sheathBounds = sheath.GetComponent<MeshRenderer>().bounds;
                float sheathAcross = Mathf.Max(sheathBounds.size.x, sheathBounds.size.y);
                Assert.That(sheathAcross, Is.LessThanOrEqualTo(0.22f),
                    $"sheath cross-section ({sheathAcross:0.000}m) exceeds 0.22m");

                foreach (Renderer r in pulse.GetComponentsInChildren<Renderer>())
                {
                    if (r.transform == sheath) continue;
                    if (!(r is MeshRenderer) && !(r is LineRenderer)) continue;   // skip the TrailRenderer (d)
                    float across = Mathf.Max(r.bounds.size.x, r.bounds.size.y);
                    Assert.That(across, Is.LessThanOrEqualTo(0.12f),
                        $"'{r.name}' reads {across:0.000}m across travel -- only the sheath may exceed 0.12m");
                }

                // --- (c) exactly 3 filament LineRenderers, 7 positions each, re-randomised over time ---
                LineRenderer[] filaments = pulse.GetComponentsInChildren<LineRenderer>();
                Assert.AreEqual(3, filaments.Length, "expected exactly 3 crackle filament LineRenderers");

                var before = new Vector3[filaments.Length][];
                for (int i = 0; i < filaments.Length; i++)
                {
                    Assert.AreEqual(7, filaments[i].positionCount, $"filament {i} must have 7 positions");
                    before[i] = new Vector3[7];
                    filaments[i].GetPositions(before[i]);
                }

                pulse.Tick(0.05f);   // > the 0.04s re-randomise interval

                bool anyChanged = false;
                for (int i = 0; i < filaments.Length; i++)
                {
                    var cur = new Vector3[7];
                    filaments[i].GetPositions(cur);
                    for (int j = 0; j < 7; j++)
                        if ((cur[j] - before[i][j]).sqrMagnitude > 1e-8f) anyChanged = true;
                }
                Assert.IsTrue(anyChanged,
                    "no crackle filament vertex moved between two ticks 0.05s apart -- the filaments never re-randomise");

                // --- (d) the trail sits at the bolt's own tail, not its middle ---
                Transform trailAnchor = pulse.transform.Find("TrailAnchor");
                Assert.IsNotNull(trailAnchor, "test precondition: SeekerPulse must build a child named 'TrailAnchor'");
                Vector3 tailWorld = pulse.transform.position - pulse.transform.forward * tuning.CoreLength;
                Assert.That(Vector3.Distance(trailAnchor.position, tailWorld), Is.LessThanOrEqualTo(0.1f),
                    $"the trail anchor ({trailAnchor.position}) is not within 0.1m of the core's own tail " +
                    $"({tailWorld})");
            }
            finally
            {
                pulse.Tick(1f);   // forces Retire() -> destroys core/sheath/crackle/trail/ground glow
            }

            // --- (e) firing 20 bolts allocates no new core/sheath Mesh after the first ---
            var coreMeshes = new HashSet<Mesh>();
            var sheathMeshes = new HashSet<Mesh>();
            for (int i = 0; i < 20; i++)
            {
                SeekerPulse p = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f,
                    turnRateDegPerSec: 360f, lifetime: 0.01f, damage: 9f, lockRange: 14f, lockHalfAngleDeg: 35f);
                coreMeshes.Add(p.transform.Find("Bolt").GetComponent<MeshFilter>().sharedMesh);
                sheathMeshes.Add(p.transform.Find("Sheath").GetComponent<MeshFilter>().sharedMesh);
                p.Tick(1f);

                Assert.AreEqual(1, coreMeshes.Count, $"pulse #{i + 1} built a new core Mesh instance -- not cached");
                Assert.AreEqual(1, sheathMeshes.Count, $"pulse #{i + 1} built a new sheath Mesh instance -- not cached");
            }

            // --- (f) a forked bolt's core colour is 0.75/0.95/1.00 (electric blue-white) ---
            SeekerPulse forked = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f,
                turnRateDegPerSec: 360f, lifetime: 0.01f, damage: 9f, lockRange: 14f, lockHalfAngleDeg: 35f,
                canFork: false, isFork: true);
            try
            {
                Color forkCore = forked.transform.Find("Bolt").GetComponent<MeshRenderer>()
                    .sharedMaterial.GetColor("_BaseColor");
                Assert.That(forkCore.r, Is.EqualTo(0.75f).Within(0.02f),
                    $"forked core red ({forkCore.r:0.000}) is not 0.75");
                Assert.That(forkCore.g, Is.EqualTo(0.95f).Within(0.02f),
                    $"forked core green ({forkCore.g:0.000}) is not 0.95");
                Assert.That(forkCore.b, Is.EqualTo(1.00f).Within(0.02f),
                    $"forked core blue ({forkCore.b:0.000}) is not 1.00");
            }
            finally
            {
                forked.Tick(1f);
            }
        }
    }
}
