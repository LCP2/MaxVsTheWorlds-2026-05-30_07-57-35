using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Core;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// One piece of a character. Everything the old per-rig <c>Part</c> helpers did, in one place, so
    /// Max and the robots cannot drift apart on the details that used to bite:
    ///
    ///   * a real material ALWAYS — a renderer left with no material ships MAGENTA (YT-58);
    ///   * <see cref="SelfDrivenTint"/> on every part, or <c>CharacterSkinDirector</c> claims it and
    ///     repaints it flat;
    ///   * shadows off (YT-186) — the fixed camera reads these by shape and eye colour, and a swarm
    ///     of shadow casters is pure cost;
    ///   * no collider, ever. The CharacterController is the only hitbox any character has.
    ///
    /// Note there is no <c>GameObject.CreatePrimitive</c> anywhere: the mesh arrives from
    /// <see cref="CharacterMeshes"/>, already shared and cached, so this only ever adds a filter and
    /// a renderer.
    /// </summary>
    public static class CharacterPart
    {
        public static Transform Add(Transform parent, Mesh mesh, Material mat,
                                    Vector3 at, Quaternion rot, Vector3 scale, string name = "Part")
        {
            var go = New(parent, mesh, at, rot, scale);
            go.name = name;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>An eye or a glow. Additive and unlit — a light, not a painted ball, so it stays
        /// bright while the body is in its own shadow.</summary>
        public static MeshRenderer AddLens(Transform parent, Mesh mesh,
                                           Vector3 at, Quaternion rot, Vector3 scale)
        {
            var go = New(parent, mesh, at, rot, scale);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static GameObject New(Transform parent, Mesh mesh,
                                      Vector3 at, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject("Part");
            go.AddComponent<SelfDrivenTint>();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            return go;
        }
    }
}
