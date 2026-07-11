using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MaxWorlds.Editor;
using MaxWorlds.Models;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-51 — the generated-model pipeline: key derivation, load-by-key, and the placeholder swap.
    ///
    /// The swap tests stand a prefab up in Resources under a test key (what the import tool
    /// produces from an .fbx) and prove a greybox upgrades itself to it, then clean the asset up.
    /// That's the flow the whole pipeline exists to deliver, and it's testable today with no
    /// generated model in hand.
    /// </summary>
    public sealed class ModelPipelineTests
    {
        private const string TestKey = "zz_test_model";
        private string _prefabPath;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_prefabPath) && File.Exists(_prefabPath))
            {
                AssetDatabase.DeleteAsset(_prefabPath);
                AssetDatabase.Refresh();
            }
            _prefabPath = null;
            Resources.UnloadUnusedAssets();
        }

        /// <summary>Stand in for the import tool's output: a keyed prefab in Resources/Models.</summary>
        private void GivenAGeneratedModelExists(string key)
        {
            Directory.CreateDirectory(ModelImportPipeline.PrefabDir);
            AssetDatabase.Refresh();

            var root = new GameObject(key);
            root.AddComponent<GeneratedModel>().Set(key, "Assets/_Project/Art/Models/Incoming/fake.fbx");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.transform.SetParent(root.transform, worldPositionStays: false);

            _prefabPath = $"{ModelImportPipeline.PrefabDir}/{key}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, _prefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
        }

        // --- keys ---

        [Test]
        public void KeyFor_IsStableAndFilesystemSafe()
        {
            Assert.AreEqual("big_bermuda_v3", ModelImportPipeline.KeyFor("Art/Incoming/Big Bermuda v3.fbx"));
            Assert.AreEqual("max", ModelImportPipeline.KeyFor("max.FBX"));
            Assert.AreEqual("robot_enemy", ModelImportPipeline.KeyFor("robot-enemy.fbx"));
        }

        [Test]
        public void KeyFor_IsDeterministic_SoARegeneratedModelOverwritesRatherThanDuplicates()
        {
            // The same source name must always map to the same key, or re-rolling a model in
            // Meshy would quietly create a second prefab beside the old one.
            Assert.AreEqual(
                ModelImportPipeline.KeyFor("Incoming/Robot Enemy.fbx"),
                ModelImportPipeline.KeyFor("Somewhere/Else/Robot Enemy.fbx"));
        }

        [Test]
        public void Glb_IsNotAccepted_WithoutTheGltfastPackage()
        {
            // Documents the constraint rather than pretending it works: Unity cannot import
            // .glb natively, and adding a package is a guardrail on this stream.
            Assert.That(ModelImportPipeline.AcceptedExtensions, Does.Contain(".fbx"));
            Assert.That(ModelImportPipeline.AcceptedExtensions, Does.Not.Contain(".glb"));
        }

        // --- load by key ---

        [Test]
        public void MissingKey_LoadsAsNull_AndIsNotAnError()
        {
            Assert.IsNull(ModelLibrary.Load("definitely_not_generated_yet"));
            Assert.IsFalse(ModelLibrary.Exists("definitely_not_generated_yet"));
        }

        [Test]
        public void GeneratedModel_LoadsByKey()
        {
            GivenAGeneratedModelExists(TestKey);

            Assert.IsTrue(ModelLibrary.Exists(TestKey));
            var prefab = ModelLibrary.Load(TestKey);
            Assert.IsNotNull(prefab, "the tool's prefab must be reachable by its key alone");
            Assert.AreEqual(TestKey, prefab.GetComponent<GeneratedModel>().Key,
                "the prefab should carry its own provenance");
        }

        // --- the swap ---

        [Test]
        public void Placeholder_UpgradesItselfWhenAModelExists()
        {
            GivenAGeneratedModelExists(TestKey);

            var greybox = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var swap = greybox.AddComponent<ModelSwap>().Bind(TestKey);
                bool swapped = swap.Apply();

                Assert.IsTrue(swapped, "a greybox with a generated model available must swap to it");
                Assert.IsNotNull(swap.Spawned);
                Assert.AreEqual(greybox.transform, swap.Spawned.transform.parent);
                Assert.IsFalse(greybox.GetComponent<MeshRenderer>().enabled,
                    "the placeholder's visual must be hidden once the real model is in");
                Assert.IsNotNull(greybox.GetComponent<Collider>(),
                    "the collider must survive the swap — gameplay still points at this object");
            }
            finally
            {
                Object.DestroyImmediate(greybox);
            }
        }

        [Test]
        public void Placeholder_StaysPutWhenNoModelHasBeenGeneratedYet()
        {
            var greybox = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var swap = greybox.AddComponent<ModelSwap>().Bind("nothing_generated_for_this");

                Assert.IsFalse(swap.Apply(), "a missing model is the normal state today, not a failure");
                Assert.IsFalse(swap.Swapped);
                Assert.IsTrue(greybox.GetComponent<MeshRenderer>().enabled,
                    "the greybox must keep rendering — the game has to run before any art exists");
            }
            finally
            {
                Object.DestroyImmediate(greybox);
            }
        }

        [Test]
        public void Swap_IsIdempotent()
        {
            GivenAGeneratedModelExists(TestKey);

            var greybox = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var swap = greybox.AddComponent<ModelSwap>().Bind(TestKey);
                swap.Apply();
                var first = swap.Spawned;
                swap.Apply();

                Assert.AreSame(first, swap.Spawned, "re-applying must not stack a second copy of the model");
                Assert.AreEqual(1, greybox.transform.childCount);
            }
            finally
            {
                Object.DestroyImmediate(greybox);
            }
        }
    }
}
