using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1201 — Lee, 16 Oct: "they're not replicators in this world ... some other structure". The
    /// City's 58 relays are authored as `replicators` (World 2's own machinery) but must not LOOK like
    /// World 2's sewer machine. <see cref="Replicator.ApplyCityRelaySkin"/> is the City-only re-skin
    /// pass — the same idempotent post-build re-skin idiom <c>MowerHutch.ApplyReefSkin</c>/
    /// <c>AreaGate.ApplyCitySkin</c> already use, called once from <c>BackyardPath</c>'s own City
    /// dressing branch after a box has already built its World 2 default.
    ///
    /// Fails on the pre-MV-1201 commit: no <c>ApplyCityRelaySkin</c> method exists on
    /// <c>Replicator</c> at all, so this file does not even compile against that commit — the same
    /// "fails on base" proof <c>ReplicatorTests</c>'s own doc comment uses for a brand-new type.
    ///
    /// Tier 2 (resolved values): measures the mast's own rendered bounds after building it, and the
    /// collider's own resolved bounds — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV1201CityRelaySkinTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-71409f, 0f, 55218f);

        private GameObject _cityGo;
        private GameObject _world2Go;

        [TearDown]
        public void TearDown()
        {
            if (_cityGo != null) Object.DestroyImmediate(_cityGo);
            if (_world2Go != null) Object.DestroyImmediate(_world2Go);
        }

        private static Replicator NewReplicator(out GameObject go, Vector3 position)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Replicator";
            go.transform.position = position;
            go.transform.localScale = new Vector3(2f, 1.5f, 2f); // the authored 2x1.5x2 footprint
            var replicator = go.AddComponent<Replicator>();
            replicator.Build();
            return replicator;
        }

        [Test]
        public void CityRelaySkinAddsATallMastWorld2NeverGrows()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] ReplicatorTests already swallows

            Replicator cityReplicator = NewReplicator(out _cityGo, RigOrigin);
            cityReplicator.ApplyCityRelaySkin();

            // World 2 never calls ApplyCityRelaySkin — same production entry point BackyardPath's own
            // City-only dressing branch gates on (world.Kit == WorldKit.City).
            Replicator world2Replicator = NewReplicator(out _world2Go, RigOrigin + new Vector3(50f, 0f, 0f));

            Transform cityMast = FindDeep(_cityGo.transform, "RelayMast");
            Assert.IsNotNull(cityMast, "ApplyCityRelaySkin must add a child named RelayMast");

            Transform world2Mast = FindDeep(_world2Go.transform, "RelayMast");
            Assert.IsNull(world2Mast, "World 2's own Replicator must never grow a RelayMast");

            // AC1: "world height >= 2.5 m above the base" — measured off the mast's own resolved
            // renderer bounds (its apex, where the gold eye sits), never an authored constant.
            float groundY = _cityGo.transform.position.y - _cityGo.transform.lossyScale.y * 0.5f;
            Bounds? mastBounds = EncapsulateRenderers(cityMast);
            Assert.IsTrue(mastBounds.HasValue, "RelayMast must carry at least one renderer to measure");
            float mastWorldHeight = mastBounds.Value.max.y - groundY;
            Assert.That(mastWorldHeight, Is.GreaterThanOrEqualTo(2.5f),
                $"RelayMast's resolved top ({mastBounds.Value.max.y:F2}) is only {mastWorldHeight:F2} m above " +
                $"the base ({groundY:F2}) — the ticket's own 'world height >= 2.5 m above the base'");

            // AC1: both bodies' collider bounds are equal — the City re-skin is dressing only.
            BoxCollider cityCollider = _cityGo.GetComponent<BoxCollider>();
            BoxCollider world2Collider = _world2Go.GetComponent<BoxCollider>();
            Assert.IsNotNull(cityCollider);
            Assert.IsNotNull(world2Collider);
            Assert.AreEqual(world2Collider.bounds.size, cityCollider.bounds.size,
                "the City re-skin must never resize the box's own collider");
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static Bounds? EncapsulateRenderers(Transform root)
        {
            Bounds? result = null;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (result.HasValue) { Bounds b = result.Value; b.Encapsulate(r.bounds); result = b; }
                else result = r.bounds;
            }
            return result;
        }
    }
}
