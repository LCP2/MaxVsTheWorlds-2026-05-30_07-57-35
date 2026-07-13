using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The reticle against the real gadget (YT-84). The EditMode tests prove the mesh is truthful to
    /// the numbers it's handed; these prove the numbers it's handed are the REAL blaster's — which is
    /// the part that would rot silently, because a reticle wired to the wrong stats looks perfectly
    /// fine right up until the day someone changes the range.
    /// </summary>
    public sealed class AimReticlePlayTests
    {
        private GameObject _max;

        private WaterBlaster NewBlaster()
        {
            _max = new GameObject("Max");
            _max.transform.position = new Vector3(0f, 1f, 0f);
            return _max.AddComponent<WaterBlaster>();
        }

        private static GameObject ReticleGo() =>
            Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .FirstOrDefault(r => r.gameObject.name == "AimReticle")?.gameObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_max != null) Object.Destroy(_max);
            yield return null;
            var stray = ReticleGo();
            if (stray != null) Object.Destroy(stray);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheBlasterBringsItsOwnReticle_WithNoSceneWiring()
        {
            NewBlaster();
            yield return null;

            Assert.IsNotNull(_max.GetComponent<AimReticle>(),
                "the blaster should attach its own reticle — a feature that needs hand-wiring in the " +
                "editor doesn't survive a headless build (docs/CODE_DRIVEN_SCENES.md)");
            Assert.IsNotNull(ReticleGo(), "no reticle was actually drawn");
        }

        [UnityTest]
        public IEnumerator TheReticleIsBuiltFromTheBlastersOwnRangeAndSpread()
        {
            // The claim the whole ticket rests on. Not "a cone appears" — THIS blaster's cone.
            var blaster = NewBlaster();
            yield return null;

            var mesh = ReticleGo().GetComponent<MeshFilter>().sharedMesh;

            Assert.AreEqual(AimReticleMesh.DrawnReach(blaster.Range), mesh.bounds.max.z, 0.05f,
                $"the reticle is drawn to a different reach than the blaster's actual " +
                $"{blaster.Range} m — it is telling the player something the weapon won't do");

            float widest = mesh.vertices
                .Where(v => v.magnitude > 0.01f)
                .Max(v => Mathf.Abs(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg));
            Assert.AreEqual(blaster.ConeHalfAngle, widest, 0.5f,
                "the drawn arc is not the cone the blaster actually hits in");
        }

        [UnityTest]
        public IEnumerator ItLiesFlatOnTheLawn_UnderMax_NotFloatingAtHisChest()
        {
            NewBlaster();
            _max.transform.position = new Vector3(4f, 1f, -3f);
            yield return null;
            yield return null;

            var go = ReticleGo();
            Assert.AreEqual(4f, go.transform.position.x, 1e-3);
            Assert.AreEqual(-3f, go.transform.position.z, 1e-3);
            Assert.Greater(go.transform.position.y, 0f, "coplanar with the lawn — it will z-fight");
            Assert.Less(go.transform.position.y, 0.02f,
                "the reticle is floating at Max's chest height instead of lying on the ground");
        }

        [UnityTest]
        public IEnumerator ItNeverCastsOrReceivesShadows()
        {
            NewBlaster();
            yield return null;

            var r = ReticleGo().GetComponent<MeshRenderer>();
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode,
                "the reticle casts a shadow — a ground mark with a shadow under it");
            Assert.IsFalse(r.receiveShadows);
        }

        [UnityTest]
        public IEnumerator ItGoesWithTheBlaster_RatherThanBeingLeftOnTheLawn()
        {
            NewBlaster();
            yield return null;
            Assert.IsNotNull(ReticleGo(), "precondition: it's drawn");

            Object.DestroyImmediate(_max);
            yield return null;

            Assert.IsNull(ReticleGo(),
                "Max is gone and his reticle is still lying on the grass — it's unparented, so it " +
                "leaks unless it cleans itself up");
        }

        /// <summary>
        /// The Craft Bible's non-negotiable: readable on a 6-inch screen, verified at a phone aspect
        /// rather than on a monitor. The reticle is the thing teaching the player their reach, so if
        /// its range boundary is a couple of pixels on a handset it teaches nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRangeBoundaryIsLegibleOnASixInchPhone()
        {
            const int PhoneW = 2340, PhoneH = 1080;

            var blaster = NewBlaster();
            _max.transform.position = Vector3.zero;
            yield return null;
            yield return null;

            var rt = new RenderTexture(PhoneW, PhoneH, 16);
            var camGo = new GameObject("Phone Camera");
            var cam = camGo.AddComponent<Camera>();
            try
            {
                cam.targetTexture = rt;
                cam.fieldOfView = 40f;
                cam.transform.position =
                    MaxWorlds.CameraRig.FixedAngleCameraRig.ComputeOffset(25.1f, 72f);
                cam.transform.rotation = Quaternion.Euler(72f, 0f, 0f);

                // How wide is the weapon's reach on screen, at the range boundary?
                float r = blaster.Range;
                float half = blaster.ConeHalfAngle * Mathf.Deg2Rad;
                Vector3 left = new Vector3(-Mathf.Sin(half) * r, 0f, Mathf.Cos(half) * r);
                Vector3 right = new Vector3(Mathf.Sin(half) * r, 0f, Mathf.Cos(half) * r);

                float px = Vector2.Distance(cam.WorldToScreenPoint(left), cam.WorldToScreenPoint(right));

                Debug.Log($"[YT-84] the blaster's range boundary spans {px:0} px on a {PhoneW}x{PhoneH} " +
                          $"phone ({px / PhoneH * 100f:0.0}% of screen height).");

                Assert.Greater(px, 60f,
                    $"the reach boundary is only {px:0} px wide on a 6-inch phone — the reticle " +
                    "cannot teach a range nobody can see");
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
            }
        }
    }
}
