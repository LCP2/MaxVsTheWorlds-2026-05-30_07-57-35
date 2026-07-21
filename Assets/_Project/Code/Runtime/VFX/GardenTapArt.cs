using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The garden tap the hose plugs into (YT-134), as a low-poly greybox prop.
    ///
    /// Model only — the tether anchor, the leash and the plug-in logic are YT-129's <c>Tap</c>. This
    /// replaces the two-cylinder greybox that ticket stands up. It matters that this reads clearly as a
    /// CONNECT POINT from across the yard, because tap-hopping is the early traversal mechanic (YT-130):
    /// a standpipe with a brass valve wheel and a spout is unmistakably "plug the hose in here."
    ///
    /// The spout presents at <c>Tap.NozzleHeight</c> (0.9 m) so the hose line meets it where YT-129
    /// couples the tether. Built the house way: primitives, colliders stripped, one
    /// <see cref="KeepsOwnMaterial"/> on the root so the surface sweep can't repaint the pipe as stone.
    /// Authored with the base at y = 0, the valve facing +Z.
    /// </summary>
    public static class GardenTapArt
    {
        /// <summary>Where the hose couples — matches YT-129 <c>Tap.NozzleHeight</c>. If that constant
        /// moves, move this with it so the spout stays under the hose end.</summary>
        public const float SpoutHeight = 0.9f;

        private static readonly Color Pipe = new Color(0.42f, 0.45f, 0.5f);   // galvanised standpipe
        private static readonly Color BrassTone = new Color(0.74f, 0.56f, 0.24f);

        public static GameObject Build(Transform parent = null)
        {
            var root = new GameObject("GardenTap");
            if (parent != null) root.transform.SetParent(parent, worldPositionStays: false);
            root.AddComponent<KeepsOwnMaterial>();

            Material pipe = MaterialLibrary.Tinted(SurfaceKind.Metal, Pipe);
            Material brass = MaterialLibrary.Tinted(SurfaceKind.Metal, BrassTone);

            // A flange where it comes out of the ground, and the riser pipe up to the valve body.
            Part(root, "Flange", PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f),
                 new Vector3(0.2f, 0.03f, 0.2f), null, pipe);
            Part(root, "Riser", PrimitiveType.Cylinder, new Vector3(0f, 0.4f, 0f),
                 new Vector3(0.11f, 0.4f, 0.11f), null, pipe);

            // The brass valve body.
            Part(root, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0.82f, 0f),
                 new Vector3(0.17f, 0.1f, 0.17f), null, brass);

            // The spout — angled forward and down, ending at the coupling height so the hose meets it.
            Part(root, "Spout", PrimitiveType.Cylinder, new Vector3(0f, SpoutHeight - 0.02f, 0.16f),
                 new Vector3(0.08f, 0.13f, 0.08f), Quaternion.Euler(62f, 0f, 0f), brass);

            // The valve wheel on top — a rim plus a cross handle, the clearest "turn me on" read.
            Part(root, "Wheel", PrimitiveType.Cylinder, new Vector3(0f, 0.98f, 0f),
                 new Vector3(0.22f, 0.03f, 0.22f), null, brass);
            for (int i = 0; i < 2; i++)
            {
                Part(root, $"Spoke{i}", PrimitiveType.Cube, new Vector3(0f, 0.98f, 0f),
                     new Vector3(i == 0 ? 0.22f : 0.05f, 0.03f, i == 0 ? 0.05f : 0.22f), null, brass);
            }
            return root;
        }

        private static void Part(GameObject root, string name, PrimitiveType shape, Vector3 pos,
                                 Vector3 scale, Quaternion? rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(root.transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
