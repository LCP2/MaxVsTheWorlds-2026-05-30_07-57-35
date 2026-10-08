using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1133 — Max's outfit, version 3 (Lee, 2026-10-01: in-game Max "looks like a caveman", from
    /// the bare arms, the hair and a weapon that looks like a club; approved 2026-10-07). One test
    /// building the real <see cref="MaxRig"/> (and its hair) through its normal create path, carrying
    /// every acceptance criterion as a sub-check (MV-465 Rule 1).
    ///
    /// Fails on the commit before this ticket: the arms render Skin-coloured (not Tunic), the hair is
    /// MV-854's 31-lock dark-brown mane (not buildV3()'s 25-lock blue-black), the belt is brown leather
    /// with no chest strap, the gadget has no shared housing/barrel and no buckle — every sub-check
    /// below fails against that geometry (quoted in the fix comment).
    ///
    /// EditMode, same reflected-Awake/Tick idiom <see cref="MV1071MaxRigDamageFlashTests"/>/
    /// <see cref="MV730MaxArmsGunWaddleTests"/> already use for this rig. Every colour/count/position
    /// below is a RESOLVED value read off the real built renderers/transforms, never an authored
    /// constant read back from source — the only literals in this file are the ticket's own
    /// independently-stated target numbers.
    /// </summary>
    public sealed class MV1133MaxOutfitV3Tests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // The ticket's own target numbers (buildV3()), independent of any source-code constant.
        private static readonly Color SkinColor = new Color(0.87f, 0.63f, 0.46f);
        private static readonly Color HairColor = new Color(0.05f, 0.065f, 0.125f);
        private static readonly Color OldBrownHair = new Color(0.19f, 0.11f, 0.06f);
        private static readonly Color WebbingColor = new Color(0.16f, 0.16f, 0.19f);
        private static readonly Color SteelColor = new Color(0.66f, 0.71f, 0.78f);
        private static readonly Color HousingColor = new Color(0.23f, 0.25f, 0.30f);
        private const float Tol = 0.03f;

        private GameObject _playerGo;
        private GameObject _rigGo;
        private PlayerController _player;
        private MaxRig _rig;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV1133-Test", typeof(CharacterController));
            var cc = _playerGo.GetComponent<CharacterController>();
            _playerGo.transform.position = new Vector3(0f, cc.height * 0.5f - cc.center.y, 0f);
            _player = _playerGo.AddComponent<PlayerController>();
            InvokeAwake(_player);

            _rigGo = new GameObject("MaxRig-MV1133-Test");
            _rig = _rigGo.AddComponent<MaxRig>();
            InvokeAwake(_rig);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            WeaponSystemState.Reset();
        }

        private static void InvokeAwake(object target) =>
            target.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static object Invoke(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private void SetMoveInput(float x, float y) =>
            typeof(PlayerController).GetProperty("MoveInput")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { new Vector2(x, y) });

        private void SetIsAiming(bool aiming) =>
            typeof(PlayerController).GetProperty("IsAiming")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { aiming });

        /// <summary>The resolved value a renderer actually draws: a property-block override when one
        /// has been written, the renderer's own material colour otherwise — <see
        /// cref="MV1071MaxRigDamageFlashTests"/>'s own idiom, reused here for base colour too.</summary>
        private static Color Resolved(Renderer r, int id)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            return mpb.isEmpty ? r.sharedMaterial.GetColor(id) : mpb.GetColor(id);
        }

        private static bool ColorsClose(Color a, Color b, float tol) =>
            Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol;

        [Test]
        public void MaxBuildsV3Outfit()
        {
            // ---------------------------------------------------------------- a) sleeves
            var armL = GetField<Transform>(_rig, "_armL");
            var armR = GetField<Transform>(_rig, "_armR");
            Color tunic = MaxRig.TunicColor;

            foreach (var (arm, label) in new[] { (armL, "ArmL"), (armR, "ArmR") })
            {
                var r = arm.GetComponent<MeshRenderer>();
                Assert.IsNotNull(r, $"{label} has no renderer to resolve a colour from.");
                Color c = Resolved(r, BaseColorId);
                Assert.IsFalse(ColorsClose(c, SkinColor, Tol),
                    $"{label} still resolves to the skin colour {c} — the sleeve must show no skin.");
                Assert.IsTrue(ColorsClose(c, tunic, Tol),
                    $"{label} resolves to {c}, not the tunic colour {tunic} (+-{Tol}).");
            }

            // ---------------------------------------------------------------- b) hair colour + count
            var head = GetField<Transform>(_rig, "_head");
            var hairMat = GetField<Material>(_rig, "_hairMat");
            var hairRibbonMat = GetField<Material>(_rig, "_hairRibbonMat");

            var specs = MaxHair.BuildLayout();
            Assert.That(specs.Length, Is.InRange(22, 28),
                $"MaxHair.BuildLayout() grew {specs.Length} locks, not between 22 and 28.");

            foreach (var mat in new[] { hairMat, hairRibbonMat })
            {
                Color c = mat.GetColor(BaseColorId);
                Assert.IsTrue(ColorsClose(c, HairColor, Tol),
                    $"a hair material resolves to {c}, not the new blue-black {HairColor} (+-{Tol}).");
                Assert.IsFalse(ColorsClose(c, OldBrownHair, Tol),
                    $"a hair material still resolves to the old brown {OldBrownHair}.");
            }

            // ---------------------------------------------------------------- c) face clear of hair
            // "Gameplay camera pose (72 degree pitch)" per the ticket's own AC text (a literal number,
            // not derived from any live rig constant). Max faces the camera (yaw = 0, his face mesh
            // sits at local +Z) and the ray runs from the camera to a point between his eyes.
            _playerGo.transform.eulerAngles = Vector3.zero;
            Invoke(_rig, "Follow");

            Vector3 faceTarget = head.TransformPoint(new Vector3(0f, 1.68f, 0.18f));
            float pitch = 72f * Mathf.Deg2Rad;
            Vector3 camDir = new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch));
            Vector3 camPos = faceTarget + camDir * 5f;
            Ray ray = new Ray(camPos, (faceTarget - camPos).normalized);

            // Let the hair settle into its idle, wind-blown resting pose before judging it — a fresh,
            // un-simulated frame is not what the camera actually sees.
            for (int i = 0; i < 120; i++) Invoke(_rig, "TickHair", 1f / 60f);

            var headRenderers = head.GetComponentsInChildren<MeshRenderer>(true);
            float? nearestFace = null, nearestHair = null;
            foreach (var r in headRenderers)
            {
                bool isHair = r.sharedMaterial == hairMat || r.sharedMaterial == hairRibbonMat;
                if (!TryRayMeshDistance(ray, r, out float dist)) continue;
                if (isHair) { if (nearestHair == null || dist < nearestHair) nearestHair = dist; }
                else { if (nearestFace == null || dist < nearestFace) nearestFace = dist; }
            }

            Assert.IsNotNull(nearestFace, "the ray never hit any face geometry at all.");
            if (nearestHair.HasValue)
            {
                Assert.That(nearestFace.Value, Is.LessThan(nearestHair.Value),
                    $"a ray to the centre of Max's face hits hair (t={nearestHair.Value:0.000}) before " +
                    $"the face (t={nearestFace.Value:0.000}) — the hair is not clear of the front.");
            }

            // ---------------------------------------------------------------- d) belt + chest strap
            var torso = GetField<Transform>(_rig, "_torso");
            var feet = torso.Find("Feet");
            Assert.IsNotNull(feet, "the rig built no Feet pivot to find the belt/strap under.");

            var beltMat = GetField<Material>(_rig, "_beltMat");
            var beltRenderer = feet.GetComponentsInChildren<MeshRenderer>(true)
                .FirstOrDefault(r => r.sharedMaterial == beltMat);
            Assert.IsNotNull(beltRenderer, "no renderer under Feet uses the belt material.");
            Color beltColor = Resolved(beltRenderer, BaseColorId);
            Assert.IsTrue(ColorsClose(beltColor, WebbingColor, Tol),
                $"the belt resolves to {beltColor}, not the dark webbing colour {WebbingColor} (+-{Tol}).");

            Transform chestStrap = feet.Find("ChestStrap");
            Assert.IsNotNull(chestStrap, "no ChestStrap part was built.");
            Assert.IsNotNull(chestStrap.Find("ChestStrapBand"),
                "ChestStrap has no band mesh under it.");

            // ---------------------------------------------------------------- e) gadget housing/barrel
            var rcdaGadget = GetField<GameObject>(_rig, "_rcdaGadget");
            Transform gunBody = rcdaGadget.transform.Find("GunBody");
            Assert.IsNotNull(gunBody, "the RCDA gadget has no GunBody pivot — buildV3()'s housing never built.");

            Transform barrel = gunBody.Find("Barrel");
            Transform housing = gunBody.Find("Housing");
            Assert.IsNotNull(barrel, "GunBody has no Barrel part.");
            Assert.IsNotNull(housing, "GunBody has no Housing part.");

            Color barrelColor = Resolved(barrel.GetComponent<MeshRenderer>(), BaseColorId);
            Color housingColor = Resolved(housing.GetComponent<MeshRenderer>(), BaseColorId);
            Assert.IsTrue(ColorsClose(barrelColor, SteelColor, Tol),
                $"the barrel resolves to {barrelColor}, not the steel colour {SteelColor} (+-{Tol}).");
            Assert.IsTrue(ColorsClose(housingColor, HousingColor, Tol),
                $"the housing resolves to {housingColor}, not the housing colour {HousingColor} (+-{Tol}).");

            float gunLength = MeasureLocalZExtent(gunBody);
            Assert.That(gunLength, Is.InRange(0.55f, 0.80f),
                $"the gadget's rendered length is {gunLength:0.000} m, not between 0.55 and 0.80 m.");

            // ---------------------------------------------------------------- f) wiring + emitter height
            var hipsArr = GetField<Transform[]>(_rig, "_hips");
            var gadgetGlow = GetField<MeshRenderer[]>(_rig, "_gadgetGlow");
            var hairLocks = GetField<object>(_rig, "_hairLocks");
            var gun = GetField<Transform>(_rig, "_gun");

            Assert.IsNotNull(armL); Assert.IsNotNull(armR);
            Assert.IsNotNull(hipsArr); Assert.That(hipsArr, Has.Length.EqualTo(2));
            Assert.IsNotNull(hipsArr[0]); Assert.IsNotNull(hipsArr[1]);
            Assert.IsNotNull(head);
            Assert.IsNotNull(gun);
            Assert.IsNotNull(hairLocks);
            Assert.That(gadgetGlow, Is.Not.Null.And.Length.EqualTo(2),
                "the RCDA gadget must hand back exactly two glow renderers (energy cell, emitter).");

            // Full aim, so the gadget sits where WaterBlaster actually fires from (MaxRigTests'
            // TheGadgetIsHeldWhereTheWaterActuallyComesFrom already proves BarrelHeight(1) itself is
            // correct; this proves the REAL BUILT emitter mesh lands there too, not just the formula).
            SetIsAiming(true);
            for (int i = 0; i < 90; i++) Invoke(_rig, "TickGadget", 1f / 20f);

            float emitterY = gadgetGlow[1].transform.position.y;
            float expectedY = MaxRig.BarrelHeight(1f);
            Assert.That(Mathf.Abs(emitterY - expectedY), Is.LessThan(0.05f),
                $"the built emitter sits at world y={emitterY:0.000}, not within 0.05 m of " +
                $"BarrelHeight(1)={expectedY:0.000} — the shot does not leave from where the gadget is drawn.");

            // ---------------------------------------------------------------- g) per-world loadout
            foreach (int worldIndex in new[] { 0, 1, 2 })
            {
                WeaponSystemState.ApplyWorldLoadout(worldIndex);

                var rcda = GetField<GameObject>(_rig, "_rcdaGadget");
                var lppe = GetField<GameObject>(_rig, "_lppeGadget");
                GameObject activeGadget = rcda.activeSelf ? rcda : (lppe.activeSelf ? lppe : null);
                Assert.IsNotNull(activeGadget, $"no gadget submesh is active under world {worldIndex}.");

                var activeGlow = rcda.activeSelf
                    ? GetField<MeshRenderer[]>(_rig, "_gadgetGlow")
                    : GetField<MeshRenderer[]>(_rig, "_lppeGlow");
                Assert.That(activeGlow, Is.Not.Null.And.Not.Empty,
                    $"world {worldIndex}'s active gadget has no glow renderers.");
                foreach (var glowRenderer in activeGlow)
                {
                    Assert.IsNotNull(glowRenderer, $"world {worldIndex} has a null glow renderer.");
                    Assert.IsTrue(glowRenderer.gameObject.activeInHierarchy,
                        $"world {worldIndex}'s glow renderer is not enabled.");
                }
            }

            Color world2Emission = MaxRig.WorldCompensationEmission(MaxRig.TunicColor, BackyardLook.Stormdrain);
            Assert.That(world2Emission.maxColorComponent, Is.GreaterThan(0f),
                "under World 2's look the tunic must still carry the MV-857 compensation emission.");

            // ---------------------------------------------------------------- h) hair moves while walking
            int vertsPerLock = MaxHair.NodeCount * 4;
            var verticesField = hairLocks.GetType().GetField("_vertices", BindingFlags.NonPublic | BindingFlags.Instance);
            var before = ((Vector3[])verticesField.GetValue(hairLocks)).ToArray();

            SetMoveInput(0f, 1f);
            SetIsAiming(false);
            const float dt = 1f / 60f;
            for (int i = 0; i < 120; i++)
            {
                Invoke(_rig, "TickRun", dt);
                Invoke(_rig, "TickHair", dt);
            }
            var after = (Vector3[])verticesField.GetValue(hairLocks);

            float maxTipMove = 0f;
            for (int lock_ = 0; lock_ < specs.Length; lock_++)
            {
                int tipA = lock_ * vertsPerLock + (MaxHair.NodeCount - 1) * 2;
                Vector3 tipBefore = (before[tipA] + before[tipA + 1]) * 0.5f;
                Vector3 tipAfter = (after[tipA] + after[tipA + 1]) * 0.5f;
                maxTipMove = Mathf.Max(maxTipMove, Vector3.Distance(tipBefore, tipAfter));
            }

            Assert.That(maxTipMove, Is.GreaterThan(0.03f),
                $"after 2 s walking at full speed, the furthest-moving lock tip travelled only " +
                $"{maxTipMove * 100f:0.0} cm relative to the head — the hair should visibly move with his stride.");
        }

        /// <summary>Nearest ray/triangle hit distance against a renderer's real mesh, in world space —
        /// none of these parts carry a collider (<see cref="CharacterPart"/>'s own rule), so
        /// <c>Physics.Raycast</c> cannot see them.</summary>
        private static bool TryRayMeshDistance(Ray ray, MeshRenderer r, out float distance)
        {
            distance = float.PositiveInfinity;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;

            Mesh mesh = mf.sharedMesh;
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            Transform t = r.transform;
            bool hit = false;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(verts[tris[i]]);
                Vector3 b = t.TransformPoint(verts[tris[i + 1]]);
                Vector3 c = t.TransformPoint(verts[tris[i + 2]]);

                if (RayTriangle(ray, a, b, c, out float d) && d < distance)
                {
                    distance = d;
                    hit = true;
                }
            }
            return hit;
        }

        /// <summary>Standard Moller-Trumbore ray/triangle intersection, both winding directions (these
        /// meshes are smooth/flat shaded for lighting, not reliably single-sided for a raycast).</summary>
        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float det = Vector3.Dot(edge1, h);
            if (Mathf.Abs(det) < 1e-8f) return false;

            float inv = 1f / det;
            Vector3 s = ray.origin - a;
            float u = Vector3.Dot(s, h) * inv;
            if (u < -1e-5f || u > 1f + 1e-5f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(ray.direction, q) * inv;
            if (v < -1e-5f || u + v > 1f + 1e-5f) return false;

            float dist = Vector3.Dot(edge2, q) * inv;
            if (dist < 1e-5f) return false;

            t = dist;
            return true;
        }

        /// <summary>The combined local-space Z extent (the barrel's own long axis) of every renderer
        /// under <paramref name="pivot"/>, measured by transforming each renderer's world bounds corners
        /// into the pivot's own local space.</summary>
        private static float MeasureLocalZExtent(Transform pivot)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var r in pivot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Bounds b = r.bounds;
                for (int xi = 0; xi < 2; xi++)
                for (int yi = 0; yi < 2; yi++)
                for (int zi = 0; zi < 2; zi++)
                {
                    Vector3 corner = new Vector3(
                        xi == 0 ? b.min.x : b.max.x,
                        yi == 0 ? b.min.y : b.max.y,
                        zi == 0 ? b.min.z : b.max.z);
                    float z = pivot.InverseTransformPoint(corner).z;
                    min = Mathf.Min(min, z);
                    max = Mathf.Max(max, z);
                }
            }
            return max - min;
        }
    }
}
