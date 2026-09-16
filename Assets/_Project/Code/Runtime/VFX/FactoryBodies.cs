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
        /// tells: the hatch (MV-775: swung open on its own local Y for the Lure/Intake beats, no
        /// longer inert), the hatch-open glow lens (lit while something is mid-consume), the
        /// emit-flash lens (the 0.6 s "twin" flash), the status LED renderer (driven exactly as
        /// MV-706 already drove it, just off a generated mesh now), and the roof fan (MV-775: the
        /// ticket's own "the only moving thing in a quiet room" — the rim/hub/spokes <see cref="BuildValveWheel"/>
        /// already built as a static prop, now spun continuously by the Replicator that owns it).</summary>
        public readonly struct ReplicatorParts
        {
            public readonly Transform Hatch;
            public readonly MeshRenderer HatchGlow;
            public readonly MeshRenderer EmitFlash;
            public readonly MeshRenderer Led;
            public readonly Transform Fan;
            /// <summary>MV-808: the output face's own reference point, mirroring <see cref="Hatch"/> on
            /// the opposite side — where a doubled twin's out-ramp foot is measured from. A bare
            /// Transform, never a rendered part: there is no door to swing on this face.</summary>
            public readonly Transform OutputLip;
            /// <summary>MV-813: the big top-face beacon — the same busy/idle/spent colour <see cref="Led"/>
            /// already carries, just legible at the play camera's scale. See <see cref="Replicator"/>'s
            /// own LateUpdate for what drives it.</summary>
            public readonly MeshRenderer StatusRing;
            /// <summary>MV-823: the roof beacon dome — dark/inactive except for the window from a robot
            /// being fully inside the box until the second twin has emitted (plus a 0.4 s linger). See
            /// <see cref="Replicator.TickConsumption"/>.</summary>
            public readonly MeshRenderer ReplicationBeacon;
            /// <summary>MV-823: the wide additive floor pool that switches on for the same window as
            /// <see cref="ReplicationBeacon"/> — unmistakably brighter (strength 0.60) than any ordinary
            /// wall-lamp pool (<see cref="MaxWorlds.Rendering.StormdrainLightKit.PoolStrength"/> 0.17).</summary>
            public readonly MeshRenderer ReplicationPool;

            public ReplicatorParts(Transform hatch, MeshRenderer hatchGlow, MeshRenderer emitFlash, MeshRenderer led,
                                   Transform fan, Transform outputLip, MeshRenderer statusRing,
                                   MeshRenderer replicationBeacon, MeshRenderer replicationPool)
            {
                Hatch = hatch; HatchGlow = hatchGlow; EmitFlash = emitFlash; Led = led; Fan = fan;
                OutputLip = outputLip; StatusRing = statusRing;
                ReplicationBeacon = replicationBeacon; ReplicationPool = replicationPool;
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

            // The extractor fan on top (MV-775) — Sludgequeen's own valve-wheel motif (a rusted rim, a
            // dark hub, four spokes) repurposed as the box's one idle tell: this is the part
            // Replicator.Update spins continuously, "the only moving thing in a quiet room".
            Transform fan = BuildValveWheel(root, new Vector3(-hw * 0.4f, hh + 0.02f, hd * 0.15f));

            // MV-808: the output lip, mirroring the hatch on the +Z face — no door, no glow, just the
            // point Replicator.OutputPosition reads back to place a doubled twin at the out-ramp foot.
            var outputLipGo = new GameObject("OutputLip");
            outputLipGo.transform.SetParent(root, worldPositionStays: false);
            outputLipGo.transform.localPosition = new Vector3(0f, -hh * 0.05f, hd + 0.02f);
            outputLipGo.transform.localRotation = Quaternion.identity;

            // MV-808: ramp in/out — a slatted steel slope from ground level up to the hatch lip (and,
            // mirrored, from the output lip back down to ground), so the Intake walk-up and the twins'
            // walk-out both read as a robot using a real crossing rather than appearing/disappearing at
            // the box's face. Dressing only: built with CharacterPart.Add, which never attaches a
            // Collider, so it can never alter navigation (the ticket's own "no collider" requirement) —
            // ramp geometry is deliberately kept off the box's own destructible collider entirely.
            const float rampSlopeLength = 1.6f; // the ticket's own authored figure
            float groundLocalY = -hh;
            float rise = hatchAt.y - groundLocalY;
            float run = Mathf.Sqrt(Mathf.Max(0f, rampSlopeLength * rampSlopeLength - rise * rise));
            BuildRamp(root, new Vector3(0f, groundLocalY, hatchAt.z - run), new Vector3(0f, hatchAt.y, hatchAt.z),
                hatchHalfW, s_rust, "RampIn");
            BuildRamp(root, new Vector3(0f, groundLocalY, hd + 0.02f + run), new Vector3(0f, hatchAt.y, hd + 0.02f),
                hatchHalfW, s_rust, "RampOut");

            // MV-822: hazard banding around the base — a Replicator lures and consumes robots, and the
            // ticket's own placement rule is "only on what can hurt you or what you must act on". One
            // straight band per side of the box, at floor level, at least 0.42 m tall (MV-822's own
            // figure; the old 0.14x-of-height formula capped at 0.22 m read as a 5 px sliver at the play
            // camera).
            float baseBandHeight = Mathf.Max(0.42f, size.y * 0.14f);
            float baseBandY = -hh + baseBandHeight * 0.5f + 0.01f;
            const float baseBandDepth = 0.02f;
            StormdrainKit.BuildHazardBanding(root, new Vector3(hw, baseBandY, 0f),
                size.z * 0.92f, baseBandHeight, alongX: false, baseBandDepth);
            StormdrainKit.BuildHazardBanding(root, new Vector3(-hw, baseBandY, 0f),
                size.z * 0.92f, baseBandHeight, alongX: false, baseBandDepth);

            // MV-822: the hatch/output faces' own bands used to run the plate's full width, straight
            // through the footprint RampIn/RampOut occupy (+-hatchHalfW) — the ramp's own sloped deck
            // then sat on top of the band's middle ~55%, exactly like a hazard stripe painted on a floor
            // and then carpeted over. Flanking segments either side of the ramp's own width keep the
            // band on plate the ramp never covers, rather than raising or shrinking it into invisibility.
            // A real gap (not just an abutting edge) from the ramp's own halfWidth — Bounds.Intersects
            // treats exactly-touching AABBs as intersecting, and the ramp's rotation is purely about
            // local X (both its ends share local X = 0, per BuildRamp's own doc), so it never bleeds
            // past +-hatchHalfW in X and a small clearance here is sufficient on its own.
            const float rampClearance = 0.05f;
            float flankBandLength = Mathf.Max(0f, size.x * 0.46f - hatchHalfW - rampClearance);
            if (flankBandLength > 0.05f)
            {
                float flankCentreX = hatchHalfW + rampClearance + flankBandLength * 0.5f;
                foreach (float faceZ in new[] { hd, -hd })
                {
                    StormdrainKit.BuildHazardBanding(root, new Vector3(flankCentreX, baseBandY, faceZ),
                        flankBandLength, baseBandHeight, alongX: true, baseBandDepth);
                    StormdrainKit.BuildHazardBanding(root, new Vector3(-flankCentreX, baseBandY, faceZ),
                        flankBandLength, baseBandHeight, alongX: true, baseBandDepth);
                }
            }

            // MV-813: the status ring — centred on the top face, unlit additive so it reads as its own
            // light source (StormdrainLightKit's own fitting material family), seeded green (idle);
            // Replicator.LateUpdate repaints and pulses it every frame off the same colour the small
            // LED above already takes.
            MeshRenderer statusRing = StormdrainLightKit.BuildStatusRing(root, "StatusRing",
                new Vector3(0f, hh + 0.01f, 0f), new Color(0.30f, 0.95f, 0.35f));

            // MV-823: the replication tells — off by default, switched on by Replicator.TickConsumption
            // for exactly the window Lee asked for ("a CLEAR BRIGHT LIGHT switch on when a replication
            // is occurring"). The beacon is a lens (same additive-glow material family HatchGlow/EmitFlash
            // already use, driven the same MaterialPropertyBlock way); the pool is a StormdrainLightKit
            // additive disc, same fitting family every other World 2 floor pool already uses.
            const float beaconDiameter = 1.2f;
            MeshRenderer replicationBeacon = CharacterPart.AddLens(root, CharacterMeshes.Sphere(16),
                new Vector3(0f, hh + beaconDiameter * 0.2f, 0f), Quaternion.identity, Vector3.one * beaconDiameter);
            replicationBeacon.gameObject.name = "ReplicationBeacon";
            replicationBeacon.gameObject.SetActive(false);

            const float replicationPoolRadius = 4.0f;
            MeshRenderer replicationPool = AddFloorPool(root, new Vector3(0f, -hh + 0.014f, 0f),
                replicationPoolRadius, new Color(1.00f, 0.85f, 0.45f), "ReplicationPool");

            return new ReplicatorParts(hatch, hatchGlow, emitFlash, led.GetComponent<MeshRenderer>(), fan,
                outputLipGo.transform, statusRing, replicationBeacon, replicationPool);
        }

        /// <summary>MV-823: a flat additive disc lying in local XZ — the same two-point-Lathe-profile
        /// technique <see cref="StormdrainLightKit"/>'s own (private) AddAdditiveDisc uses, duplicated
        /// here rather than exposed there since a Replicator's pool is driven per-frame by
        /// <see cref="Replicator"/> itself (colour/strength change with the replication state), not
        /// seeded once like an ordinary fitting's pool.</summary>
        private static MeshRenderer AddFloorPool(Transform root, Vector3 localPos, float radius, Color tone, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Lathe(
                new[] { new Vector2(0f, 0f), new Vector2(radius, 0f) }, 28);
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = StormdrainLightKit.AdditiveUnlit(tone, name);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            go.SetActive(false);
            return rend;
        }

        /// <summary>MV-808: one sloped deck plus a handful of cross-slats between <paramref name="footLocal"/>
        /// (ground level) and <paramref name="topLocal"/> (the hatch/output lip) — the same "slatted"
        /// read as the game's other plank crossings, built from <see cref="CharacterMeshes.Bevelled"/>
        /// and <see cref="CharacterMeshes.Beam"/> rather than a primitive, per this file's own rule.
        /// Both ends must share the same local X (0) — this only ever slopes along Z.</summary>
        private static void BuildRamp(Transform root, Vector3 footLocal, Vector3 topLocal, float halfWidth,
                                      Material mat, string name)
        {
            Vector3 delta = topLocal - footLocal;
            float length = delta.magnitude;
            if (length < 0.01f) return; // degenerate (zero rise/run) box — nothing to build

            var rampRoot = new GameObject(name).transform;
            rampRoot.SetParent(root, worldPositionStays: false);
            rampRoot.localPosition = (footLocal + topLocal) * 0.5f;
            // Both ends share local X = 0 (see doc comment), so this is always a pure rotation about X —
            // FromToRotation sidesteps hand-deriving that angle's sign for the mirrored in/out ramps.
            rampRoot.localRotation = Quaternion.FromToRotation(Vector3.forward, delta / length);

            const float deckThickness = 0.06f;
            Add(rampRoot, CharacterMeshes.Bevelled(new Vector3(halfWidth * 2f, deckThickness, length), 0.02f), mat,
                Vector3.zero, Quaternion.identity, Vector3.one, "Deck");

            const int slatCount = 5;
            for (int i = 1; i < slatCount; i++)
            {
                float t = (float)i / slatCount - 0.5f;
                Add(rampRoot, CharacterMeshes.Beam(halfWidth * 1.8f, 0.02f, 0.02f, 6), mat,
                    new Vector3(0f, deckThickness * 0.5f + 0.015f, t * length), Quaternion.Euler(0f, 0f, 90f),
                    Vector3.one, "Slat");
            }
        }

        /// <summary>Built under its own "Fan" parent, at <paramref name="at"/>, so the whole rim/hub/
        /// spoke assembly can be spun as one unit about its own local Y — the children below are
        /// therefore positioned relative to that parent, not <paramref name="root"/>.</summary>
        private static Transform BuildValveWheel(Transform root, Vector3 at)
        {
            var fan = new GameObject("Fan").transform;
            fan.SetParent(root, worldPositionStays: false);
            fan.localPosition = at;
            fan.localRotation = Quaternion.identity;

            Add(fan, CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0.16f, 0f), new Vector2(0.22f, 0.02f), new Vector2(0.22f, 0.05f), new Vector2(0.16f, 0.07f),
                }, 20), s_rust, Vector3.zero, Quaternion.identity, Vector3.one, "ValveRim");
            Add(fan, CharacterMeshes.Sphere(10), s_dark, Vector3.up * 0.035f, Quaternion.identity,
                new Vector3(0.06f, 0.06f, 0.06f), "ValveHub");
            for (int i = 0; i < 4; i++)
            {
                float thetaDeg = i * 90f;
                Add(fan, CharacterMeshes.Beam(0.32f, 0.02f, 0.014f, 6), s_rust,
                    Vector3.up * 0.035f, Quaternion.Euler(90f, thetaDeg, 0f), Vector3.one, "ValveSpoke");
            }

            return fan;
        }

        private static void EnsureMaterials()
        {
            if (s_dark != null) return;
            // MV-780: was (0.06, 0.06, 0.07) — ~15 resolved luma, darker than World 2's own
            // 18-26 luma floor tier (MV-777), so the hull had no silhouette against the ground it
            // stands on. Lifted into the cover/props tier (~70-90 luma), hue kept neutral-cool —
            // it's the machine chassis, not a warm surface.
            s_dark = NewMaterial("Factory_Dark", new Color(0.32f, 0.33f, 0.36f));
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
