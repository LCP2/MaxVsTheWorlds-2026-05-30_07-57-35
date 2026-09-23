using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-913: the shed missile fitting's old <c>GameObject.CreatePrimitive(PrimitiveType.Cube)</c>
    /// replaced with a reusable <see cref="MissileLauncherRig"/> built from generated meshes only,
    /// callable with no shed (or any other host) present — the whole point being that a future World 2
    /// Replicator or World 3 structure can call it unchanged. One test (per CC_AUTONOMY's one-new-test
    /// rule) covering the ticket's three build/behaviour AC together: the builder is its own standalone
    /// type (AC1), it contains no Unity primitive mesh (AC2), and its facing resolves to wherever it was
    /// last told to look (AC3) — all read off the BUILT instance, never an authored field.
    /// </summary>
    public sealed class MV913MissileLauncherRigTests
    {
        [Test]
        public void MissileLauncherRig_BuildsFromGeneratedMeshesOnly_AndFacesItsTarget()
        {
            var root = new GameObject("MV913 Probe Root — no shed anywhere near this");
            try
            {
                // AC1: constructible with no shed, MowerHutch or ShedFitting present — one line, size and
                // palette only.
                var testShader = Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard");
                var palette = new MissileLauncherPalette(
                    new Material(testShader), new Material(testShader), new Material(testShader));
                MissileLauncherRig rig = MissileLauncherRig.Build(root.transform, 0.5f, palette);
                Assert.IsNotNull(rig, "MissileLauncherRig.Build returned null");
                Assert.IsNotNull(rig.Turret, "the built rig has no aiming transform to face with");

                // AC2: generated meshes only — every MeshFilter under the built rig must be one of
                // CharacterMeshes' own cached builds (named "CharacterMesh_..."), never a Unity built-in
                // primitive mesh ("Cube"/"Sphere"/"Cylinder"/"Capsule"/"Plane"/"Quad").
                var filters = root.GetComponentsInChildren<MeshFilter>();
                Assert.Greater(filters.Length, 0, "the rig built no visible parts at all");
                foreach (var mf in filters)
                {
                    Assert.IsNotNull(mf.sharedMesh, $"{mf.name} carries no mesh");
                    StringAssert.DoesNotContain("Cube", mf.sharedMesh.name, $"{mf.name} is a primitive cube, not a generated mesh");
                    StringAssert.DoesNotContain("Sphere", mf.sharedMesh.name, $"{mf.name} is a primitive sphere, not a generated mesh");
                    StringAssert.DoesNotContain("Cylinder", mf.sharedMesh.name, $"{mf.name} is a primitive cylinder, not a generated mesh");
                    StringAssert.DoesNotContain("Capsule", mf.sharedMesh.name, $"{mf.name} is a primitive capsule, not a generated mesh");
                    StringAssert.StartsWith("CharacterMesh_", mf.sharedMesh.name,
                        $"{mf.name}'s mesh ({mf.sharedMesh.name}) did not come from CharacterMeshes — generated meshes only, per the standing rule.");
                }

                // AC3: facing rotates to a supplied target direction — read the RESOLVED transform
                // rotation (Turret.forward), never an authored field.
                rig.Face(new Vector3(1f, 0f, 0f));
                Assert.Greater(Vector3.Dot(rig.Turret.forward, Vector3.right), 0.99f,
                    $"facing right should point Turret.forward at +X, actually {rig.Turret.forward}");

                rig.Face(new Vector3(0f, 0f, -1f));
                Assert.Greater(Vector3.Dot(rig.Turret.forward, Vector3.back), 0.99f,
                    $"re-facing backward should re-resolve Turret.forward to -Z, actually {rig.Turret.forward}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
