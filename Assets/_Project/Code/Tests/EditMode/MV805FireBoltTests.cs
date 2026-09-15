using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-805 -- "Max's weapon looks too much like water" (Lee, 2026-09-15): the LPPE bolt, the head
    /// gauge above Max and the muzzle/impact flashes were all the same cold cyan-white as the RCDA's
    /// own tank, and the bolt's mesh was a Unity capsule at an aspect where the two hemispheres meet
    /// in a hard crease across the middle -- "a bend" in Lee's own words. One test proves both halves
    /// of the fix (MV-465 Rule 1 -- colour and shape are the same "reads as water/broken" report, not
    /// two independent regressions): (1) the bolt's resolved tint reads orange, and the head gauge
    /// <see cref="PlayerHealth"/> hands <see cref="WorldHealthBar.Attach"/> for the LPPE stays locked
    /// to it; (2) the resolved bolt mesh, walked along its own long axis, is a single smooth curve --
    /// never the capsule's abrupt crease.
    ///
    /// Fails on base commit f89c4e7: <c>SeekerPulse.BoltColor</c> is still the cold cyan-white
    /// (0.55, 0.95, 1) and <c>SeekerPulse.BuildVisual</c> still builds a
    /// <c>GameObject.CreatePrimitive(PrimitiveType.Capsule)</c>.
    /// </summary>
    public sealed class MV805FireBoltTests
    {
        private static readonly FieldInfo BarSecondaryFillField =
            typeof(WorldHealthBar).GetField("_secondaryFill", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _maxGo;
        private SeekerPulse _pulse;

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            UpgradeState.Reset();
            DevTuning.Reset();
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            if (_pulse != null) _pulse.Tick(1f); // forces Retire() -> destroys bolt+trail+glow
        }

        [Test]
        public void BoltReadsOrangeLockedToTheGauge_AndItsMeshIsOneSmoothCurveNotACapsuleCrease()
        {
            _pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f, turnRateDegPerSec: 360f,
                lifetime: 0.01f, damage: 9f, lockRange: 14f, lockHalfAngleDeg: 35f);

            Transform boltMesh = _pulse.transform.Find("Bolt");
            Assert.IsNotNull(boltMesh, "test precondition: SeekerPulse must build a child named 'Bolt'");
            var renderer = boltMesh.GetComponent<MeshRenderer>();

            // --- (1a) colour: the bolt itself reads orange, not cyan-white ---
            Color boltTint = renderer.sharedMaterial.GetColor("_BaseColor");
            Assert.That(boltTint.r, Is.GreaterThan(boltTint.g),
                $"bolt red ({boltTint.r:0.000}) is not ahead of green ({boltTint.g:0.000}) -- doesn't read orange");
            Assert.That(boltTint.g, Is.GreaterThan(boltTint.b),
                $"bolt green ({boltTint.g:0.000}) is not ahead of blue ({boltTint.b:0.000}) -- doesn't read orange");
            Assert.That(boltTint.r, Is.GreaterThanOrEqualTo(0.9f),
                $"bolt red channel ({boltTint.r:0.000}) too low to read as orange");
            Assert.That(boltTint.b, Is.LessThanOrEqualTo(0.2f),
                $"bolt blue channel ({boltTint.b:0.000}) too high -- still reads cyan/water");

            // --- (1b) the LPPE head gauge stays locked to the bolt's own tint ---
            WeaponSystemState.Reset();
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Lppe;
            _maxGo = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            var pulseLaser = _maxGo.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(pulseLaser, null);
            var waterBlaster = _maxGo.AddComponent<WaterBlaster>();
            WaterBlasterAwake.Invoke(waterBlaster, null);
            var health = _maxGo.AddComponent<PlayerHealth>();
            health.Initialize();

            var bar = _maxGo.GetComponent<WorldHealthBar>();
            var secondaryFill = (Image)BarSecondaryFillField.GetValue(bar);
            Color gaugeColor = secondaryFill.color;

            Assert.That(Mathf.Abs(gaugeColor.r - boltTint.r), Is.LessThanOrEqualTo(0.02f),
                $"gauge red ({gaugeColor.r:0.000}) doesn't match the bolt's ({boltTint.r:0.000})");
            Assert.That(Mathf.Abs(gaugeColor.g - boltTint.g), Is.LessThanOrEqualTo(0.02f),
                $"gauge green ({gaugeColor.g:0.000}) doesn't match the bolt's ({boltTint.g:0.000})");
            Assert.That(Mathf.Abs(gaugeColor.b - boltTint.b), Is.LessThanOrEqualTo(0.02f),
                $"gauge blue ({gaugeColor.b:0.000}) doesn't match the bolt's ({boltTint.b:0.000})");

            // --- (2) shape: the resolved mesh, walked along its own long axis, is one smooth curve ---
            var meshFilter = boltMesh.GetComponent<MeshFilter>();
            Assert.IsNotNull(meshFilter, "test precondition: the bolt must carry its own MeshFilter");
            List<(float height, float radius)> profile = RadiusProfile(meshFilter.sharedMesh, boltMesh.localScale);

            Assert.That(profile.Count, Is.GreaterThanOrEqualTo(12),
                "too few distinct height samples along the bolt's long axis to prove a smooth profile");

            float peak = 0f;
            foreach (var p in profile) peak = Mathf.Max(peak, p.radius);
            Assert.Greater(peak, 0f, "test precondition: the bolt has some radius somewhere along its length");

            bool sawDescent = false;
            for (int i = 1; i < profile.Count; i++)
            {
                float prev = profile[i - 1].radius;
                float cur = profile[i].radius;
                if (cur < prev - 0.0005f) sawDescent = true;
                else if (sawDescent && cur > prev + 0.0005f)
                    Assert.Fail($"radius profile is not unimodal -- it rose again at sample {i} " +
                        $"({prev:0.0000} -> {cur:0.0000}) after it had already started falling; a single " +
                        "continuous curve from nose to tail cannot do that");

                float step = Mathf.Abs(cur - prev);
                Assert.That(step, Is.LessThan(peak * 0.25f),
                    $"radius step between adjacent samples ({step:0.0000}) is {step / peak:P0} of the peak " +
                    "radius -- too abrupt to read as a smooth curve rather than the capsule's own crease");
            }
        }

        /// <summary>Walks <paramref name="mesh"/>'s vertices -- scaled by <paramref name="localScale"/>,
        /// the transform the bolt is actually rendered at, so this reads the RESOLVED shape rather than
        /// an unscaled authored mesh -- grouped by height along the long axis (Y: the lathe's own
        /// revolve axis, and also the axis a Unity capsule primitive is built along before the
        /// 90-degree rotation that points either shape down the travel axis, so the two are directly
        /// comparable), widest vertex per height band.</summary>
        private static List<(float height, float radius)> RadiusProfile(Mesh mesh, Vector3 localScale)
        {
            var byHeight = new SortedDictionary<float, float>();
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 s = Vector3.Scale(v, localScale);
                float h = Mathf.Round(s.y * 2000f) / 2000f;
                float r = Mathf.Sqrt(s.x * s.x + s.z * s.z);
                if (!byHeight.TryGetValue(h, out float existing) || r > existing) byHeight[h] = r;
            }
            var list = new List<(float, float)>();
            foreach (var kv in byHeight) list.Add((kv.Key, kv.Value));
            return list;
        }
    }
}
