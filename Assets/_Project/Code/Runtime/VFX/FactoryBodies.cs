// MV-693 EXCEPTION: hand-authored, same reason as RobotBodies' own BuildBolter/BuildLurker/
// BuildTurret/BuildSludger — robot-gen-mesh.html is an interactive browser tool this worker
// cannot drive headlessly. Flagged for Lee to fold into the design source at his convenience.
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Replicator's generated body (MV-693) — same lathe/prism/beam vocabulary as
    /// <see cref="RobotBodies"/>, built from <see cref="CharacterMeshes"/> rather than the
    /// primitive cube MV-706 shipped with. An armoured hull, a black/yellow hazard band under the
    /// roofline, a rusted hatch and status LED on the lure face, and a valve wheel on top
    /// (Sludgequeen's motif).
    ///
    /// Materials are shared, static instances (never per-Replicator) — unlike a robot's own
    /// <c>RobotRig.BuildMaterials</c>, nothing here needs a per-instance emissive tween, so there
    /// is no reason to pay for one material set per box.
    /// </summary>
    public static class FactoryBodies
    {
        private static Material s_dark;
        private static Material s_hazard;
        private static Material s_rust;

        /// <summary>What <see cref="MaxWorlds.Factories.Replicator"/> needs back to drive its own
        /// tells: the hatch (rotated to droop on destruction), the hatch-open glow lens (lit while
        /// something is mid-consume), the emit-flash lens (the 0.6 s "twin" flash), and the status
        /// LED renderer (driven exactly as MV-706 already drove it, just off a generated mesh now).</summary>
        public readonly struct ReplicatorParts
        {
            public readonly Transform Hatch;
            public readonly MeshRenderer HatchGlow;
            public readonly MeshRenderer EmitFlash;
            public readonly MeshRenderer Led;

            public ReplicatorParts(Transform hatch, MeshRenderer hatchGlow, MeshRenderer emitFlash, MeshRenderer led)
            {
                Hatch = hatch; HatchGlow = hatchGlow; EmitFlash = emitFlash; Led = led;
            }
        }

        /// <summary><paramref name="root"/> must already be a metre-space container
        /// (<see cref="ParentScale.MakeMetreSpace"/>) sitting at the box's own centre — the same
        /// pivot convention the primitive cube it replaces used. <paramref name="size"/> is the
        /// box's real world footprint in metres (width, height, depth), read from that body's own
        /// <c>lossyScale</c> before <c>MakeMetreSpace</c> cancels it.</summary>
        public static ReplicatorParts BuildReplicator(Transform root, Vector3 size)
        {
            EnsureMaterials();

            float hw = size.x * 0.5f, hh = size.y * 0.5f, hd = size.z * 0.5f;
            // 1/cos(45deg): CharacterMeshes.Prism's 4-sided case lands its vertices on the
            // diagonals, so this is the radius that makes the FLAT FACES sit exactly at +-1 —
            // i.e. a unit box before the non-uniform (hw, 1, hd) scale below stretches it to the
            // authored footprint.
            const float diag = 1.41421356f;

            // The hull — centred on the box's own pivot, spanning the full authored footprint.
            Add(root, CharacterMeshes.Prism(4, diag, diag, size.y, 0.05f), s_dark,
                Vector3.zero, Quaternion.identity, new Vector3(hw, 1f, hd), "Hull");

            // The hazard band, just under the roofline — a black course over a yellow one, the
            // same hard-colour-block idiom Bolter/Rusher's own two-tone chassis reads use rather
            // than a fine diagonal tape texture that would not survive being ~20 px tall at
            // gameplay distance.
            float bandH = size.y * 0.05f;
            float bandY = hh - size.y * 0.09f;
            Add(root, CharacterMeshes.Prism(4, diag * 1.012f, diag * 1.012f, bandH, 0.25f), s_hazard,
                new Vector3(0f, bandY, 0f), Quaternion.identity, new Vector3(hw, 1f, hd), "HazardBand");
            Add(root, CharacterMeshes.Prism(4, diag * 1.02f, diag * 1.02f, bandH, 0.25f), s_dark,
                new Vector3(0f, bandY - bandH, 0f), Quaternion.identity, new Vector3(hw, 1f, hd), "HazardBandDark");

            // The rusted hatch, on the lure face (-Z — the same face MV-706's own LED sat on).
            // MV-693's own "irises open" is cut to the Reads list's glow tell (HatchGlow below) —
            // true iris-mesh animation is tight-slice scope for a later pass; noted here rather
            // than silently dropped.
            float hatchHalfW = hw * 0.55f, hatchHalfH = hh * 0.55f;
            Vector3 hatchAt = new Vector3(0f, -hh * 0.05f, -hd - 0.02f);
            Transform hatch = Add(root, CharacterMeshes.Prism(4, diag, diag, 0.1f, 0.25f), s_rust,
                hatchAt, Quaternion.Euler(90f, 0f, 0f), new Vector3(hatchHalfW, 1f, hatchHalfH), "Hatch");

            MeshRenderer hatchGlow = CharacterPart.AddLens(root,
                CharacterMeshes.Prism(4, diag, diag, 0.02f, 0.3f),
                hatchAt + new Vector3(0f, 0f, -0.04f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(hatchHalfW * 0.85f, 1f, hatchHalfH * 0.85f));
            hatchGlow.gameObject.name = "HatchGlow";

            MeshRenderer emitFlash = CharacterPart.AddLens(root, CharacterMeshes.Sphere(14),
                hatchAt + new Vector3(0f, 0f, -0.14f), Quaternion.identity,
                Vector3.one * (hatchHalfW * 1.1f));
            emitFlash.gameObject.name = "EmitFlash";

            // The status LED, beside the hatch — same lit character material MV-706 already drove
            // (kept _BaseColor/_EmissionColor via a MaterialPropertyBlock unchanged); only the
            // primitive-cube mesh underneath it is gone, per MV-693's own "not Unity primitives" rule.
            Transform led = Add(root, CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.1f, 0.025f), new Vector2(0f, 0.025f),
                }, 12), MaterialLibrary.Character(),
                new Vector3(hw * 0.5f, hh * 0.3f, -hd - 0.02f), Quaternion.Euler(90f, 0f, 0f), Vector3.one, "ReplicatorLed");

            // The valve wheel on top — Sludgequeen's motif: a rusted rim, a dark hub, four spokes,
            // laid flat on the roof.
            BuildValveWheel(root, new Vector3(-hw * 0.4f, hh + 0.02f, hd * 0.15f));

            return new ReplicatorParts(hatch, hatchGlow, emitFlash, led.GetComponent<MeshRenderer>());
        }

        private static void BuildValveWheel(Transform root, Vector3 at)
        {
            Add(root, CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0.16f, 0f), new Vector2(0.22f, 0.02f), new Vector2(0.22f, 0.05f), new Vector2(0.16f, 0.07f),
                }, 20), s_rust, at, Quaternion.identity, Vector3.one, "ValveRim");
            Add(root, CharacterMeshes.Sphere(10), s_dark, at + Vector3.up * 0.035f, Quaternion.identity,
                new Vector3(0.06f, 0.06f, 0.06f), "ValveHub");
            for (int i = 0; i < 4; i++)
            {
                float thetaDeg = i * 90f;
                Add(root, CharacterMeshes.Beam(0.32f, 0.02f, 0.014f, 6), s_rust,
                    at + Vector3.up * 0.035f, Quaternion.Euler(90f, thetaDeg, 0f), Vector3.one, "ValveSpoke");
            }
        }

        private static void EnsureMaterials()
        {
            if (s_dark != null) return;
            s_dark = NewMaterial("Factory_Dark", new Color(0.06f, 0.06f, 0.07f));
            s_hazard = NewMaterial("Factory_Hazard", new Color(0.95f, 0.78f, 0.08f));
            s_rust = NewMaterial("Factory_Rust", new Color(0.55f, 0.27f, 0.11f));
        }

        private static Material NewMaterial(string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58):
            // a plain lit material still draws the right colour, just without the outline.
            var template = MaterialLibrary.Character();
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private static Transform Add(Transform root, Mesh mesh, Material mat,
                                     Vector3 at, Quaternion rot, Vector3 scale, string name = "Part")
            => CharacterPart.Add(root, mesh, mat, at, rot, scale, name);
    }
}
