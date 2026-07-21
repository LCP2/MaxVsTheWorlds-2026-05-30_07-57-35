using UnityEngine;
using UnityEngine.Rendering;
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

            // A chunk bigger than the first cut (YT-144), so it's easy to spot as a water source from
            // across the yard at the ~72 deg camera. The mass and girth grow ~1.5x; the vertical layout
            // is deliberately NOT scaled with it — the SPOUT has to stay at the hose-coupling height
            // (SpoutHeight / Tap.NozzleHeight, 0.9 m) or the hose line meets it in mid-air and the
            // retained cyan connection bulb (Tap's, at 1.02 m) floats off the valve. So it grows fat and
            // a touch taller, not uniformly scaled: a beefier standpipe, same plug-in point.

            // A flange where it comes out of the ground, and the riser pipe up to the valve body.
            Part(root, "Flange", PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f),
                 new Vector3(0.30f, 0.05f, 0.30f), null, pipe);
            Part(root, "Riser", PrimitiveType.Cylinder, new Vector3(0f, 0.46f, 0f),
                 new Vector3(0.17f, 0.46f, 0.17f), null, pipe);

            // The brass valve body.
            Part(root, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0.95f, 0f),
                 new Vector3(0.25f, 0.12f, 0.25f), null, brass);

            // The spout — angled forward and down, its mouth kept at the coupling height so the hose
            // meets it. Branches off the riser BELOW the raised valve, so the tap can grow taller
            // without the water source leaving the 0.9 m line.
            Part(root, "Spout", PrimitiveType.Cylinder, new Vector3(0f, SpoutHeight - 0.04f, 0.24f),
                 new Vector3(0.11f, 0.18f, 0.11f), Quaternion.Euler(62f, 0f, 0f), brass);

            // The valve wheel on top — a rim plus a cross handle, the clearest "turn me on" read.
            Part(root, "Wheel", PrimitiveType.Cylinder, new Vector3(0f, 1.08f, 0f),
                 new Vector3(0.32f, 0.04f, 0.32f), null, brass);
            for (int i = 0; i < 2; i++)
            {
                Part(root, $"Spoke{i}", PrimitiveType.Cube, new Vector3(0f, 1.08f, 0f),
                     new Vector3(i == 0 ? 0.32f : 0.07f, 0.04f, i == 0 ? 0.07f : 0.32f), null, brass);
            }

            BuildDrip(root);
            return root;
        }

        /// <summary>The dripping-tap beacon (YT-142): a continuous water drip off the spout and a wet
        /// patch pooling at the base, so a tap reads at a glance as a water source to plug into — the
        /// "here I am" locator Lee asked for, the way a pickup shimmers.</summary>
        private static void BuildDrip(GameObject root)
        {
            // The drip — droplets falling from the spout mouth. World space so they fall straight down
            // and pool no matter how the tap is turned.
            var dripGo = new GameObject("Drip");
            dripGo.transform.SetParent(root.transform, worldPositionStays: false);
            dripGo.transform.localPosition = new Vector3(0f, SpoutHeight - 0.06f, 0.36f);   // the bigger spout's mouth
            var ps = dripGo.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.75f);   // ~time to fall to the lawn
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.13f, 0.21f);       // grown with the tap — fat beads that read at zoom
            main.gravityModifier = 1.7f;                                          // it FALLS — that is the read
            main.startColor = new Color(0.62f, 0.86f, 1f, 0.95f);
            main.maxParticles = 60;                                               // ambience budget (< 200)

            var emission = ps.emission;
            emission.rateOverTime = 11f;   // a steady, catch-the-eye drip, still not a stream

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.035f;

            // Stretched along fall velocity so each drop reads as a bead of water, not a dot.
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.09f;
            r.lengthScale = 2.4f;
            r.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Droplet());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // The wet patch — a soft translucent disc darkening the lawn under the spout.
            var patch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            patch.name = "WetPatch";
            var col = patch.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            patch.transform.SetParent(root.transform, worldPositionStays: false);
            patch.transform.localPosition = new Vector3(0f, 0.02f, 0.24f);
            patch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);        // lie flat on the lawn
            patch.transform.localScale = new Vector3(1.15f, 1.15f, 1f);           // grown with the tap
            var pr = patch.GetComponent<MeshRenderer>();
            pr.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Droplet());
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            pr.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", new Color(0.16f, 0.30f, 0.38f, 0.5f));     // wet, darkened lawn
            pr.SetPropertyBlock(mpb);
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
