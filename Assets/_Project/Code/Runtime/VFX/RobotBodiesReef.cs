// MV-746: World 3's eight kinds get their own bodies instead of wearing World 1's (MV-715 renamed
// them but never re-shaped them). Hand-authored, the same exception BuildBolter/BuildLurker/
// BuildTurret/BuildSludger/BuildCharger already carry in RobotBodies.cs — robot-gen-mesh.html is an
// interactive browser tool this worker cannot drive headlessly. Built from the same CharacterMeshes
// primitives as every other kind; colours are supplied by the caller's RobotPalette, which RobotRig
// sources from WorldMaterials' Reef set for a "reef" skin (never from CharacterSkin).
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    public static partial class RobotBodies
    {
        /// <summary>Dispatches the eight World 3 kinds to their own silhouette. Returns false (and
        /// builds nothing) for any kind outside that roster, so <see cref="Build"/> falls through to
        /// the base roster's body instead of silently reskinning something World 3 never spawns.</summary>
        private static bool TryBuildReef(EnemyKind kind, Transform root, in RobotPalette p,
                                         List<MeshRenderer> eyes, List<Transform> wheels,
                                         List<Transform> legs, out Transform inflatable)
        {
            inflatable = null;
            switch (kind)
            {
                case EnemyKind.Rusher:   BuildReefRusher(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Bruiser:  BuildReefBruiser(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Heavy:    BuildReefHeavy(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Brute:    BuildReefBrute(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Gunner:   BuildReefGunner(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Launcher: inflatable = BuildReefLauncher(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Blinker:  BuildReefBlinker(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Bolter:   BuildReefBolter(root, p, eyes, wheels, legs); return true;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ Scrap Eel (Rusher)

        /// <summary>Scrap Eel — long, low and segmented, pitched nose-down into a permanent dart. No
        /// wheels: it never rolled, it swims/skitters, so an empty wheel list is correct here, not an
        /// oversight.</summary>
        private static void BuildReefRusher(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.02f, 0f), new Vector2(0.16f, 0.05f), new Vector2(0.18f, 0.14f), new Vector2(0.14f, 0.2f) }, 16), p.Cool,
                new Vector3(0f, 0.22f, 0.32f), Quaternion.Euler(-18f, 0f, 0f), Vector3.one, "Segment");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.14f, 0f), new Vector2(0.2f, 0.05f), new Vector2(0.19f, 0.15f), new Vector2(0.13f, 0.2f) }, 16), p.Warm,
                new Vector3(0f, 0.28f, 0f), Quaternion.identity, Vector3.one, "Segment");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.13f, 0f), new Vector2(0.15f, 0.05f), new Vector2(0.1f, 0.13f), new Vector2(0.04f, 0.16f) }, 14), p.Cool,
                new Vector3(0f, 0.24f, -0.34f), Quaternion.Euler(8f, 0f, 0f), Vector3.one, "Segment");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.04f, 0f), new Vector2(0.045f, 0.05f), new Vector2(0.02f, 0.09f), new Vector2(0f, 0.11f) }, 10), p.Dark,
                new Vector3(0f, 0.2f, -0.6f), Quaternion.Euler(14f, 0f, 0f), Vector3.one, "Tail");

            // The one gold tell, a dorsal spine along the mid-body.
            Add(root, CharacterMeshes.Prism(4, 0.02f, 0.01f, 0.18f, 0.2f, 0f), p.Gold,
                new Vector3(0f, 0.4f, 0.05f), Quaternion.Euler(0f, 0f, 90f), Vector3.one, "Spine");

            // The head, nose pitched down toward the ground it darts along.
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.15f, 0f), new Vector2(0.16f, 0.08f), new Vector2(0.1f, 0.16f), new Vector2(0.03f, 0.2f) }, 16), p.Dark,
                new Vector3(0f, 0.2f, 0.56f), Quaternion.Euler(-32f, 0f, 0f), Vector3.one, "Head");
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, 0.12f, 0.72f), Quaternion.Euler(-50f, 0f, 0f), new Vector3(0.13f, 0.13f, 0.05f)));
        }

        // ------------------------------------------------------------------ Salvage Crab (Bruiser)

        /// <summary>Salvage Crab — wide, flat, two heavy forward claws, a planted sideways stance
        /// instead of the World 1 Bruiser's tank treads.</summary>
        private static void BuildReefBruiser(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            Add(root, CharacterMeshes.Prism(4, 0.5f, 0.46f, 0.34f, 0.16f, 0f), p.Cool,
                new Vector3(0f, 0.34f, 0f), Quaternion.identity, new Vector3(1.3f, 0.6f, 1f), "Carapace");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.3f, 0f), new Vector2(0.32f, 0.05f), new Vector2(0.24f, 0.1f) }, 20), p.Warm,
                new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(1.3f, 1f, 1f), "Ridge");

            // Two heavy forward claws, splayed left/right — the sideways stance the ticket calls for.
            Add(root, CharacterMeshes.Prism(4, 0.14f, 0.09f, 0.4f, 0.2f, 0f), p.Dark,
                new Vector3(-0.42f, 0.3f, 0.34f), Quaternion.Euler(0f, -34f, 78f), Vector3.one, "ClawArm");
            Add(root, CharacterMeshes.Prism(4, 0.14f, 0.09f, 0.4f, 0.2f, 0f), p.Dark,
                new Vector3(0.42f, 0.3f, 0.34f), Quaternion.Euler(0f, 34f, -78f), Vector3.one, "ClawArm");
            Add(root, CharacterMeshes.Prism(3, 0.16f, 0.02f, 0.22f, 0.1f, 0f), p.Warm,
                new Vector3(-0.6f, 0.34f, 0.52f), Quaternion.Euler(0f, -34f, 90f), Vector3.one, "Pincer");
            Add(root, CharacterMeshes.Prism(3, 0.16f, 0.02f, 0.22f, 0.1f, 0f), p.Warm,
                new Vector3(0.6f, 0.34f, 0.52f), Quaternion.Euler(0f, 34f, -90f), Vector3.one, "Pincer");

            // Four short legs to the sides, planted rather than walking.
            for (int i = 0; i < 4; i++)
            {
                float side = i < 2 ? -1f : 1f;
                float zf = (i % 2 == 0) ? 0.16f : -0.16f;
                Add(root, CharacterMeshes.Beam(0.24f, 0.03f, 0.02f, 6), p.Dark,
                    new Vector3(side * 0.46f, 0.16f, zf), Quaternion.Euler(0f, 0f, side * 70f), Vector3.one, "Leg");
            }

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.1f, 0f), new Vector2(0.153f, 0.0157f), new Vector2(0.161f, 0.0323f), new Vector2(0.145f, 0.0522f) }, 24), p.Dark,
                new Vector3(0f, 0.56f, 0.22f), Quaternion.Euler(48f, 0f, 0f), Vector3.one);
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, 0.5774f, 0.243f), Quaternion.Euler(-42f, 0f, 0f), new Vector3(0.19f, 0.19f, 0.06f)));
        }

        // ------------------------------------------------------------------ Cable Tentacle (Heavy)

        /// <summary>Cable Tentacle — a thick armoured column rising straight out of a floor grate. No
        /// legs: it never walked, it simply rises.</summary>
        private static void BuildReefHeavy(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            Add(root, CharacterMeshes.Prism(8, 0.5f, 0.46f, 0.1f, 0.3f, 0f), p.Dark,
                new Vector3(0f, 0.05f, 0f), Quaternion.identity, Vector3.one, "Grate");

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.26f, 0f), new Vector2(0.3f, 0.15f), new Vector2(0.28f, 0.5f), new Vector2(0.3f, 0.9f), new Vector2(0.26f, 1.1f) }, 24), p.Cool,
                new Vector3(0f, 0.1f, 0f), Quaternion.identity, Vector3.one, "Column");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.26f, 1.1f), new Vector2(0.24f, 1.3f), new Vector2(0.16f, 1.5f) }, 24), p.Warm,
                new Vector3(0f, 0.1f, 0f), Quaternion.identity, Vector3.one);

            // Armour bands ringing the column — the "armoured" tell, one of them the gold tier tell.
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.3f, -0.02f), new Vector2(0.33f, 0f), new Vector2(0.3f, 0.02f) }, 24), p.Gold,
                new Vector3(0f, 0.55f, 0f), Quaternion.identity, Vector3.one);
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.29f, -0.02f), new Vector2(0.32f, 0f), new Vector2(0.29f, 0.02f) }, 24), p.Dark,
                new Vector3(0f, 0.95f, 0f), Quaternion.identity, Vector3.one);

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.1008f, 0f), new Vector2(0.1544f, 0.0158f), new Vector2(0.1625f, 0.0325f), new Vector2(0.1463f, 0.0525f) }, 30), p.Dark,
                new Vector3(0f, 1.32f, 0.16f), Quaternion.Euler(48f, 0f, 0f), Vector3.one);
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, 1.337f, 0.174f), Quaternion.Euler(-44f, 0f, 0f), new Vector3(0.2f, 0.2f, 0.065f)));
        }

        // ------------------------------------------------------------------ Dredge Hulk (Brute)

        /// <summary>Dredge Hulk — tall, on the base roster's four-wheel undercarriage, with a wide
        /// front armour plate mounted so it reads unmistakably as a shield from the front.</summary>
        private static void BuildReefBrute(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            // Tessellation is bumped by one from the base roster's own road-wheel/axle profile (the
            // same trick BuildReefBolter uses below) — same proportions, but a mesh set that is never
            // reference-equal to World 1's (MV-746 AC1), since CharacterMeshes caches by parameter hash.
            Vector2[] tire = { new Vector2(0.0645f, -0.0725f), new Vector2(0.1849f, -0.0725f), new Vector2(0.215f, -0.0435f), new Vector2(0.215f, 0.0435f), new Vector2(0.1849f, 0.0725f), new Vector2(0.0645f, 0.0725f) };
            Vector2[] hub = { new Vector2(0.1118f, -0.0261f), new Vector2(0.129f, 0f), new Vector2(0.1118f, 0.0261f) };
            foreach (float xs in new[] { -0.4f, 0.4f })
                foreach (float zs in new[] { -0.3f, 0.3f })
                {
                    wheels.Add(Add(root, CharacterMeshes.Lathe(tire, 23), p.Dark, new Vector3(xs, 0.215f, zs), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
                    wheels.Add(Add(root, CharacterMeshes.Lathe(hub, 17), p.Warm, new Vector3(xs, 0.215f, zs), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
                }
            Add(root, CharacterMeshes.Beam(0.6f, 0.05f, 0.05f, 7), p.Dark, new Vector3(-0.4f, 0.215f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            Add(root, CharacterMeshes.Beam(0.6f, 0.05f, 0.05f, 7), p.Dark, new Vector3(0.4f, 0.215f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.3f, 0f), new Vector2(0.46f, 0.1f), new Vector2(0.5f, 0.35f), new Vector2(0.48f, 0.6f) }, 24), p.Cool,
                new Vector3(0f, 0.34f, 0f), Quaternion.identity, Vector3.one);
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.48f, 0.6f), new Vector2(0.44f, 0.85f), new Vector2(0.3f, 1.0f), new Vector2(0.14f, 1.06f) }, 24), p.Dark,
                new Vector3(0f, 0.34f, 0f), Quaternion.identity, Vector3.one);

            // The front armour plate — a flat, wide plank standing upright, unmistakably a shield
            // when read from the front, mounted forward of the hull.
            Add(root, CharacterMeshes.Prism(4, 0.42f, 0.4f, 0.62f, 0.06f, 0f), p.Warm,
                new Vector3(0f, 0.68f, 0.34f), Quaternion.identity, new Vector3(1.6f, 1f, 0.16f), "Shield");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.05f, -0.02f), new Vector2(0.08f, 0f), new Vector2(0.05f, 0.02f) }, 16), p.Gold,
                new Vector3(0f, 0.68f, 0.42f), Quaternion.identity, Vector3.one);

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.0685f, 0f), new Vector2(0.105f, 0.0107f), new Vector2(0.1105f, 0.0221f), new Vector2(0.0995f, 0.0357f) }, 31), p.Dark,
                new Vector3(0f, 1.36f, 0.2f), Quaternion.Euler(44f, 0f, 0f), Vector3.one);
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, 1.374f, 0.216f), Quaternion.Euler(-42f, 0f, 0f), new Vector3(0.17f, 0.17f, 0.05f)));
        }

        // ------------------------------------------------------------------ Mine Urchin (Gunner)

        /// <summary>Mine Urchin — spherical, bristling with radial spines. No legs: the Gunner's
        /// tripod was a functional read for an emplacement that never moves (<c>LungeSpeed</c> is
        /// still 0, untouched), but a naval-mine silhouette needs to sit on the ground, not stand
        /// over it.</summary>
        private static void BuildReefGunner(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.16f, 0f), new Vector2(0.2f, 0.05f), new Vector2(0.18f, 0.12f) }, 16), p.Dark,
                Vector3.zero, Quaternion.identity, Vector3.one, "Base");
            Add(root, CharacterMeshes.Sphere(29), p.Cool, new Vector3(0f, 0.55f, 0f), Quaternion.identity, new Vector3(0.7f, 0.7f, 0.7f));

            const float sphereRadius = 0.35f;
            const float sphereCenterY = 0.55f;
            for (int i = 0; i < 8; i++)
            {
                float thetaRad = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(thetaRad), 0f, Mathf.Cos(thetaRad));
                Add(root, CharacterMeshes.Prism(4, 0.05f, 0.006f, 0.28f, 0.1f, 0f), p.Warm,
                    dir * sphereRadius + new Vector3(0f, sphereCenterY, 0f),
                    Quaternion.FromToRotation(Vector3.up, dir), Vector3.one, "Spine");
            }
            for (int i = 0; i < 6; i++)
            {
                float phiRad = (30f + i * 60f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(phiRad) * 0.7f, Mathf.Sin(phiRad), 0f).normalized;
                Add(root, CharacterMeshes.Prism(4, 0.045f, 0.005f, 0.22f, 0.1f, 0f), p.Warm,
                    dir * sphereRadius + new Vector3(0f, sphereCenterY, 0f),
                    Quaternion.FromToRotation(Vector3.up, dir), Vector3.one, "Spine");
            }

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.1008f, 0f), new Vector2(0.1544f, 0.0158f), new Vector2(0.1625f, 0.0325f), new Vector2(0.1463f, 0.0525f) }, 32), p.Dark,
                new Vector3(0f, 0.55f, 0.28f), Quaternion.Euler(46f, 0f, 0f), Vector3.one);
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, 0.567f, 0.3f), Quaternion.Euler(-44f, 0f, 0f), new Vector3(0.17f, 0.17f, 0.055f)));

            Add(root, CharacterMeshes.Sphere(13), p.Gold, new Vector3(0f, 0.9f, 0f), Quaternion.identity, new Vector3(0.035f, 0.035f, 0.035f));
        }

        // ------------------------------------------------------------------ Puffer Mine (Launcher)

        /// <summary>Puffer Mine — a spiked sphere that visibly inflates through its 1.2s telegraph
        /// (<see cref="EnemyArchetype"/>'s World 3 override; unchanged by this ticket). The inflatable
        /// core lives on its OWN transform, separate from the anchoring base and the spikes, so
        /// <see cref="MaxWorlds.VFX.RobotRig"/> can scale just that part up over the wind-up without
        /// moving anything else.</summary>
        private static Transform BuildReefLauncher(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.14f, 0f), new Vector2(0.17f, 0.04f), new Vector2(0.14f, 0.09f) }, 16), p.Dark,
                Vector3.zero, Quaternion.identity, Vector3.one, "Base");

            var inflatable = new GameObject("Inflatable").transform;
            inflatable.SetParent(root, worldPositionStays: false);
            inflatable.localPosition = new Vector3(0f, 0.42f, 0f);

            Add(inflatable, CharacterMeshes.Sphere(28), p.Cool, Vector3.zero, Quaternion.identity, new Vector3(0.62f, 0.62f, 0.62f));
            Add(inflatable, CharacterMeshes.Sphere(28), p.Warm, new Vector3(0f, 0.08f, 0f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));

            // Spike nubs fixed to the base, not the inflatable, so they read as the mine's own casing
            // rather than stretching along with the inflate.
            for (int i = 0; i < 5; i++)
            {
                float thetaRad = i * 72f * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(thetaRad) * 0.16f, 0.05f, Mathf.Cos(thetaRad) * 0.16f);
                Add(root, CharacterMeshes.Prism(4, 0.03f, 0.004f, 0.1f, 0.15f, 0f), p.Gold,
                    pos, Quaternion.identity, Vector3.one, "Spike");
            }

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.1008f, 0f), new Vector2(0.1544f, 0.0158f), new Vector2(0.1625f, 0.0325f), new Vector2(0.1463f, 0.0525f) }, 33), p.Dark,
                new Vector3(0f, 0.42f, 0.32f), Quaternion.Euler(48f, 0f, 0f), Vector3.one);
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, 0.437f, 0.35f), Quaternion.Euler(-46f, 0f, 0f), new Vector3(0.17f, 0.17f, 0.055f)));

            return inflatable;
        }

        // ------------------------------------------------------------------ Anglerfish-bot (Blinker)

        /// <summary>The Reef bio-glow lure — the ONLY emissive part on the Anglerfish-bot (MV-746 AC4):
        /// a fresh Character-shader material with <c>_EMISSION</c> explicitly enabled, shared across
        /// every Anglerfish-bot the same way every other kind's eye-lens material is shared. Nothing
        /// else this file builds ever calls <c>EnableKeyword("_EMISSION")</c>, which is what makes "the
        /// lure is the only lit part" a fact about the material graph, not just about placement.</summary>
        private static Material s_reefLureMaterial;
        private static Material ReefLureMaterial()
        {
            if (s_reefLureMaterial != null) return s_reefLureMaterial;

            Material template = MaterialLibrary.Character();
            var m = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
            m.name = "Reef_Lure";
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", WorldMaterials.ReefBioGlow);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", WorldMaterials.ReefBioGlow);
            m.EnableKeyword("_EMISSION");

            s_reefLureMaterial = m;
            return s_reefLureMaterial;
        }

        /// <summary>Anglerfish-bot — a thin body (no crystal core, no shards: that read is the World 1
        /// Blinker's, not this one's) with a single bright lure-light arced out in front on a thin,
        /// unlit rod. The rod and every other part wear the ordinary palette; only the lure itself
        /// carries emission.</summary>
        private static void BuildReefBlinker(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.05f, 0f), new Vector2(0.11f, 0.1f), new Vector2(0.12f, 0.3f), new Vector2(0.08f, 0.5f), new Vector2(0.02f, 0.62f) }, 20), p.Dark,
                new Vector3(0f, 0f, -0.05f), Quaternion.identity, Vector3.one, "Body");
            Add(root, CharacterMeshes.Prism(3, 0.14f, 0.02f, 0.3f, 0.2f, 0f), p.Cool,
                new Vector3(0f, 0.25f, -0.34f), Quaternion.Euler(0f, 0f, 90f), Vector3.one, "TailFin");

            Add(root, CharacterMeshes.Beam(0.28f, 0.015f, 0.006f, 6), p.Dark,
                new Vector3(0f, 0.5f, 0.28f), Quaternion.Euler(70f, 0f, 0f), Vector3.one, "LureRod");

            Transform lure = Add(root, CharacterMeshes.Sphere(16), ReefLureMaterial(),
                new Vector3(0f, 0.66f, 0.46f), Quaternion.identity, new Vector3(0.09f, 0.09f, 0.09f), "Lure");
            eyes.Add(lure.GetComponent<MeshRenderer>());
        }

        // ------------------------------------------------------------------ Reef Bolter (Bolter)

        /// <summary>Reef Bolter — the ticket's own call: "the World 1 bolter shape in Reef materials".
        /// Same profile numbers as <see cref="BuildBolter"/> (so it is, deliberately, the same shape),
        /// every tessellation count bumped by one so <see cref="CharacterMeshes"/>'s parameter-hash
        /// cache mints its OWN meshes rather than handing back World 1's exact <see cref="Mesh"/>
        /// instances — MV-746 AC1 needs a mesh set that is not reference-equal to World 1's, and a
        /// visually-identical shape drawn from a shared cache entry would fail that by construction.</summary>
        private static void BuildReefBolter(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            wheels.Add(Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.0705f, -0.0675f), new Vector2(0.2021f, -0.0675f), new Vector2(0.235f, -0.0405f), new Vector2(0.235f, 0.0405f), new Vector2(0.2021f, 0.0675f), new Vector2(0.0705f, 0.0675f) }, 23), p.Dark, new Vector3(0f, 0.235f, -0.02f), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
            wheels.Add(Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.1222f, -0.0243f), new Vector2(0.141f, 0f), new Vector2(0.1222f, 0.0243f) }, 17), p.Warm, new Vector3(0f, 0.235f, -0.02f), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
            Add(root, CharacterMeshes.Beam(0.22f, 0.032f, 0.024f, 7), p.Dark, new Vector3(0f, 0.16f, 0.2f), Quaternion.Euler(60f, 0f, 0f), Vector3.one);

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.16f, 0f), new Vector2(0.36f, 0.05f), new Vector2(0.42f, 0.14f), new Vector2(0.414f, 0.16f) }, 29), p.Cool, new Vector3(0f, 0.32f, 0f), Quaternion.identity, Vector3.one);
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.414f, 0.16f), new Vector2(0.4f, 0.22f), new Vector2(0.28f, 0.28f), new Vector2(0.15f, 0.34f) }, 29), p.Warm, new Vector3(0f, 0.32f, 0f), Quaternion.identity, Vector3.one);

            const float spikeRadius = 0.40f;
            const float spikeEquatorY = 0.46f;
            for (int i = 0; i < 6; i++)
            {
                float thetaDeg = 30f + i * 60f;
                float thetaRad = thetaDeg * Mathf.Deg2Rad;
                var spikePos = new Vector3(Mathf.Sin(thetaRad) * spikeRadius, spikeEquatorY, Mathf.Cos(thetaRad) * spikeRadius);
                Add(root, CharacterMeshes.Prism(5, 0.10f, 0.006f, 0.52f, 0.1f, 0f), p.Warm,
                    spikePos, Quaternion.Euler(78f, thetaDeg, 0f), Vector3.one, "Spike");
            }

            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.1008f, 0f), new Vector2(0.1544f, 0.0158f), new Vector2(0.1625f, 0.0325f), new Vector2(0.1463f, 0.0525f) }, 25), p.Dark, new Vector3(0f, 0.62f, 0.26f), Quaternion.Euler(50f, 0f, 0f), Vector3.one);
            eyes.Add(Lens(root, CharacterMeshes.Sphere(21), new Vector3(0f, 0.6361f, 0.2792f), Quaternion.Euler(-40f, 0f, 0f), new Vector3(0.1938f, 0.1938f, 0.0625f)));

            Add(root, CharacterMeshes.Beam(0.42f, 0.055f, 0.04f, 9), p.Dark, new Vector3(0f, 0.62f, 0.51f), Quaternion.Euler(90f, 0f, 0f), Vector3.one, "Barrel");
            Add(root, CharacterMeshes.Sphere(11), p.Gold, new Vector3(0f, 0.62f, 0.72f), Quaternion.identity, new Vector3(0.032f, 0.032f, 0.032f));
        }
    }
}
