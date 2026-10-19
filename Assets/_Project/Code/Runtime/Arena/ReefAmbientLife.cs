using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// World 3's ambient-life pass (MV-1257) — WHERE each cheap VFX driver from
    /// <c>MaxWorlds.VFX.ReefAmbientLife</c>-adjacent types (actually <c>ReefMarineSnow</c>/
    /// <c>ReefLightFlicker</c>/<c>ReefDrumCorePulse</c>/<c>ReefAmbientBurstEmitter</c>, all in
    /// <c>Runtime/VFX/ReefAmbientLife.cs</c>) gets attached, once the rest of <see cref="ReefKit"/>'s
    /// structure kit and <see cref="ReefDressing"/>'s cover pass have already built the drums,
    /// manifolds, consoles, wall lamps and circuit-spine strips this reads back by name — same
    /// WHERE/WHAT split <see cref="ReefHydroponics"/> and <see cref="ReefFloorDetail"/> already keep
    /// with <see cref="ReefKit"/>. "Beyond the window walls" (Change item 3) is closed separately, in
    /// <see cref="ReefAreaKit.BuildWindows"/> itself, since that is the one call that already knows
    /// where every in-map window actually is.
    ///
    /// Every selection below is a deterministic seeded hash of the piece's own built position
    /// (<see cref="MaxWorlds.VFX.SludgeBubbles"/>'s own idiom, reused rather than Unity's global
    /// <see cref="Random"/>), so the same map always lights up the same ~half of drums / ~10% of
    /// lamps / ~1-in-6 consoles rather than reshuffling on every load.
    /// </summary>
    public static class ReefAmbientLife
    {
        /// <summary>Ticket AC1's own budget ceiling: the resolved count of
        /// <see cref="ReefAmbientEmitter"/>-tagged GameObjects this pass builds for a real Reef map must
        /// stay at or under this. A typical World 3 map (8 reef_drum pieces today) resolves to roughly
        /// 1 (marine snow) + half the drums (bubble streams) + 1 (shared steam) + 1 (shared sparks) —
        /// comfortably inside this ceiling with headroom for a bigger future layout, while still
        /// catching a regression that turned "one shared burst per kind" back into "one per prop".</summary>
        public const int MaxAmbientEmitters = 24;

        private const float DrumBubbleFraction = 0.5f;
        private const float ManifoldSteamFraction = 0.2f;
        private const float ConsoleSparkFraction = 1f / 6f;
        private const float LampFlickerFraction = 0.1f;

        private const float SteamIntervalSeconds = 5f;
        private const float SparkIntervalSeconds = 6f;

        private static readonly Color SteamColorA = new Color(0.85f, 0.90f, 0.92f, 0.5f);
        private static readonly Color SteamColorB = new Color(0.70f, 0.78f, 0.80f, 0.35f);

        /// <summary>Builds every ambient-life driver for an already-fully-dressed Reef map
        /// (<paramref name="host"/> must already carry <see cref="ReefKit.DressHull"/>'s circuit spine
        /// and wall lamps, and <see cref="ReefDressing.DressCover"/>'s structure kit). Called once from
        /// <see cref="BackyardPath"/>'s own <c>ApplyReefKit</c>, after every other Reef dressing call.</summary>
        public static void Build(Transform host)
        {
            if (host == null) return;

            ReefMarineSnow.Build(host);
            BuildDrumLife(host);
            BuildManifoldSteam(host);
            BuildConsoleSparks(host);
            BuildLightFlicker(host);
        }

        // ---- drums: bubble streams (Change item 2) + core pulse (Change item 4) -------------------

        private static void BuildDrumLife(Transform host)
        {
            List<Transform> drums = FindNamed(host, "ReefDrum");
            for (int i = 0; i < drums.Count; i++)
            {
                Transform drum = drums[i];
                int seed = unchecked((int)StableHash(drum.position));

                Transform core = drum.Find("Core");
                if (core != null) ReefDrumCorePulse.Attach(core, seed);

                if (ReefAmbientHash.Hash01(seed, 0) >= DrumBubbleFraction) continue;

                SludgeBubbles stream = SludgeBubbles.Attach(drum, "Bubble Stream", new Vector2(0.18f, 0.18f),
                    0.05f, seed, 3, WorldMaterials.ReefCircuitCyan);
                stream.gameObject.AddComponent<ReefAmbientEmitter>();
            }
        }

        // ---- manifolds: shared steam-puff source (Change item 5) ----------------------------------

        private static void BuildManifoldSteam(Transform host)
        {
            List<Transform> manifolds = FindNamed(host, "ReefManifold");
            var points = new List<Vector3>(manifolds.Count);
            foreach (Transform m in manifolds)
            {
                int seed = unchecked((int)StableHash(m.position));
                if (ReefAmbientHash.Hash01(seed, 1) < ManifoldSteamFraction)
                    points.Add(m.position + Vector3.up * 0.55f);
            }
            if (points.Count == 0) return;

            ReefAmbientBurstEmitter.Attach(host, "Reef Steam Puffs", VfxMaterials.AlphaBlend(VfxMaterials.Glow()),
                points.ToArray(), seed: unchecked((int)StableHash(host.position)), interval: SteamIntervalSeconds,
                count: 4, axis: Vector3.up, spreadDegrees: 22f,
                speedMin: 0.25f, speedMax: 0.5f, sizeMin: 0.12f, sizeMax: 0.28f,
                lifeMin: 1.2f, lifeMax: 2f, colorA: SteamColorA, colorB: SteamColorB,
                maxParticles: 80, gravity: -0.03f, perFrameCap: 4);
        }

        // ---- consoles: shared spark source (Change item 5) -----------------------------------------

        private static void BuildConsoleSparks(Transform host)
        {
            List<Transform> consoles = FindNamed(host, "ReefConsole");
            var points = new List<Vector3>(consoles.Count);
            foreach (Transform c in consoles)
            {
                int seed = unchecked((int)StableHash(c.position));
                if (ReefAmbientHash.Hash01(seed, 2) < ConsoleSparkFraction)
                    points.Add(c.position + Vector3.up * 0.5f);
            }
            if (points.Count == 0) return;

            ReefAmbientBurstEmitter.Attach(host, "Reef Console Sparks", VfxMaterials.Additive(VfxMaterials.Glow()),
                points.ToArray(), seed: unchecked((int)StableHash(host.position)) + 1, interval: SparkIntervalSeconds,
                count: 3, axis: Vector3.up, spreadDegrees: 50f,
                speedMin: 0.6f, speedMax: 1.3f, sizeMin: 0.03f, sizeMax: 0.07f,
                lifeMin: 0.2f, lifeMax: 0.4f, colorA: WorldMaterials.ReefHazard, colorB: Color.white,
                maxParticles: 40, gravity: 0.5f, perFrameCap: 3);
        }

        // ---- lamps and circuit-spine strips: flicker (Change item 4) ------------------------------

        private static void BuildLightFlicker(Transform host)
        {
            var renderers = new List<Renderer>();
            CollectChildRenderers(host, "Wall Lamps", renderers);
            CollectChildRenderers(host, "Circuit Spine", renderers);

            for (int i = 0; i < renderers.Count; i++)
            {
                int seed = unchecked((int)StableHash(renderers[i].transform.position));
                if (ReefAmbientHash.Hash01(seed, 3) < LampFlickerFraction)
                    ReefLightFlicker.Attach(renderers[i], seed);
            }
        }

        // ---- shared helpers -------------------------------------------------------------------------

        private static List<Transform> FindNamed(Transform host, string name)
        {
            var found = new List<Transform>();
            foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
                if (t.name == name) found.Add(t);
            return found;
        }

        private static void CollectChildRenderers(Transform host, string rootName, List<Renderer> into)
        {
            foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != rootName) continue;
                into.AddRange(t.GetComponentsInChildren<Renderer>(true));
            }
        }

        /// <summary>A stable per-position seed (world position is already deterministic for a given
        /// map load, same reason <see cref="MaxWorlds.Arena.ReefFloorDetail"/>'s own grate/decal
        /// placement hashes a candidate's position rather than a loop index that would shift if an
        /// earlier piece were ever skipped).</summary>
        private static uint StableHash(Vector3 position)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (float f in new[] { position.x, position.y, position.z })
                {
                    int bits = Mathf.RoundToInt(f * 100f);
                    h = (h ^ (uint)bits) * 16777619;
                }
                return h;
            }
        }
    }
}
