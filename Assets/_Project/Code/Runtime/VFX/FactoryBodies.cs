// MV-693 EXCEPTION: hand-authored, same reason as RobotBodies' own BuildBolter/BuildLurker/
// BuildTurret/BuildSludger — robot-gen-mesh.html is an interactive browser tool this worker
// cannot drive headlessly. Flagged for Lee to fold into the design source at his convenience.
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
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
            /// <summary>MV-834: the big green roof beacon, mounted ON the box — dark/inactive except for
            /// the window from a robot being fully inside the box until the second twin has emitted (plus
            /// a 0.4 s linger). See <see cref="Replicator.TickConsumption"/>. MV-823's own floor pool
            /// (<c>ReplicationPool</c>) is deleted outright — Lee: "a stupid, basic big yellow circle
            /// around it that is illogical". No floor pool of any colour replaces it.</summary>
            public readonly MeshRenderer ReplicationBeacon;
            /// <summary>MV-1067: the guaranteed-arrival pull's own visible tell — "a green beam ...
            /// from the hatch to the robot for the whole pull" (Lee, 2026-10-02: "I don't want robots
            /// just suddenly disappearing"). Built disabled, two positions, world space; <see cref="Replicator"/>'s
            /// own TickConsumption aims and toggles it every tick a pull is live.</summary>
            public readonly LineRenderer PullBeam;
            /// <summary>MV-1201: every World 2 hazard-stripe piece (the roofline band and the base
            /// banding) — <see cref="Replicator.ApplyCityRelaySkin"/> hides these before laying its own
            /// Teleport Relay dressing over the top, the same "disable the old renderer, build fresh
            /// over it" idiom <c>AreaGate.ApplyCitySkin</c> already uses. Never touched for World 2.</summary>
            public readonly Transform[] HazardDressing;

            public ReplicatorParts(Transform hatch, MeshRenderer hatchGlow, MeshRenderer emitFlash, MeshRenderer led,
                                   Transform fan, Transform outputLip, MeshRenderer statusRing,
                                   MeshRenderer replicationBeacon, LineRenderer pullBeam, Transform[] hazardDressing)
            {
                Hatch = hatch; HatchGlow = hatchGlow; EmitFlash = emitFlash; Led = led; Fan = fan;
                OutputLip = outputLip; StatusRing = statusRing;
                ReplicationBeacon = replicationBeacon; PullBeam = pullBeam; HazardDressing = hazardDressing;
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
            var hazardDressing = new List<Transform>(8);
            float bandH = size.y * 0.05f;
            float bandY = hh - size.y * 0.09f;
            hazardDressing.Add(Add(root, CharacterMeshes.Prism(4, diag * 1.012f, diag * 1.012f, bandH, 0.25f), s_hazard,
                new Vector3(0f, bandY, 0f), Quaternion.identity, new Vector3(hw, 1f, hd), "HazardBand"));
            hazardDressing.Add(Add(root, CharacterMeshes.Prism(4, diag * 1.02f, diag * 1.02f, bandH, 0.25f), s_dark,
                new Vector3(0f, bandY - bandH, 0f), Quaternion.identity, new Vector3(hw, 1f, hd), "HazardBandDark"));

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
            hazardDressing.Add(StormdrainKit.BuildHazardBanding(root, new Vector3(hw, baseBandY, 0f),
                size.z * 0.92f, baseBandHeight, alongX: false, baseBandDepth).transform);
            hazardDressing.Add(StormdrainKit.BuildHazardBanding(root, new Vector3(-hw, baseBandY, 0f),
                size.z * 0.92f, baseBandHeight, alongX: false, baseBandDepth).transform);

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
                    hazardDressing.Add(StormdrainKit.BuildHazardBanding(root, new Vector3(flankCentreX, baseBandY, faceZ),
                        flankBandLength, baseBandHeight, alongX: true, baseBandDepth).transform);
                    hazardDressing.Add(StormdrainKit.BuildHazardBanding(root, new Vector3(-flankCentreX, baseBandY, faceZ),
                        flankBandLength, baseBandHeight, alongX: true, baseBandDepth).transform);
                }
            }

            // MV-813: the status ring — centred on the top face, unlit additive so it reads as its own
            // light source (StormdrainLightKit's own fitting material family), seeded amber (idle, MV-834);
            // Replicator.LateUpdate repaints and pulses it every frame off the same colour the small
            // LED above already takes.
            MeshRenderer statusRing = StormdrainLightKit.BuildStatusRing(root, "StatusRing",
                new Vector3(0f, hh + 0.01f, 0f), new Color(0.60f, 0.45f, 0.15f));

            // MV-834: the replication tell — a big green lamp mounted on the box's own roof, off by
            // default, switched on by Replicator.TickConsumption for exactly the window Lee asked for
            // ("a big green light on the replicator" while it replicates). A lens (same additive-glow
            // material family HatchGlow/EmitFlash already use, driven the same MaterialPropertyBlock
            // way). MV-823's own floor pool is gone outright — Lee rejected it as "a stupid, basic big
            // yellow circle ... illogical"; there is no floor pool of any colour on a Replicator any more.
            const float beaconDiameter = 1.2f;
            MeshRenderer replicationBeacon = CharacterPart.AddLens(root, CharacterMeshes.Sphere(16),
                new Vector3(0f, hh + beaconDiameter * 0.2f, 0f), Quaternion.identity, Vector3.one * beaconDiameter);
            replicationBeacon.gameObject.name = "ReplicationBeacon";
            replicationBeacon.gameObject.SetActive(false);

            // MV-1067: the pull beam — built disabled and colourless; Replicator paints it the same
            // ReplicationLightColor the beacon/status-ring/hatch-glow already take, the instant a
            // guaranteed-arrival pull goes live, same "built inert, painted by the owner" convention
            // every other tell in this file already follows.
            var pullBeamGo = new GameObject("PullBeam");
            pullBeamGo.transform.SetParent(root, worldPositionStays: false);
            LineRenderer pullBeam = pullBeamGo.AddComponent<LineRenderer>();
            pullBeam.positionCount = 2;
            pullBeam.useWorldSpace = true;
            pullBeam.widthMultiplier = 0.12f;
            pullBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pullBeam.receiveShadows = false;
            pullBeam.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            pullBeamGo.SetActive(false);

            return new ReplicatorParts(hatch, hatchGlow, emitFlash, led.GetComponent<MeshRenderer>(), fan,
                outputLipGo.transform, statusRing, replicationBeacon, pullBeam, hazardDressing.ToArray());
        }

        // ---------------------------------------------------------------- MV-1201: City Teleport Relay

        private static Material s_citySteel;

        /// <summary>MV-1201: the relay's own reserved red (robots hold it — see
        /// <see cref="MaxWorlds.Rendering.CityKit.IsReservedRelayColour"/>'s own red/amber/cyan bands).</summary>
        private static readonly Color RelayGlowRed = new Color(1.00f, 0.08f, 0.05f);

        /// <summary>MV-1201: the relay's own reserved gold — the mast's ring and eye lamp.</summary>
        private static readonly Color RelayGold = new Color(1.00f, 0.78f, 0.15f);

        /// <summary>MV-1201: Lee's own authored figure — "a 2.8 m mast on top".</summary>
        private const float RelayMastHeight = 2.8f;
        private const float RelayMastRadius = 0.07f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Builds the City's Teleport Relay topside (MV-1201) onto an otherwise-empty
        /// <paramref name="root"/> — <see cref="Replicator.ApplyCityRelaySkin"/>'s own dressing root,
        /// laid over a box that already carries <see cref="BuildReplicator"/>'s default World 2 look
        /// with its hazard striping hidden. A red glow ring at the base ("because the robots hold it",
        /// Lee 16 Oct) and a mast — named "RelayMast", read back by <c>Replicator</c>'s own EditMode
        /// test — carrying one gold ring and one gold eye lamp. <paramref name="size"/> is the same
        /// box footprint <see cref="BuildReplicator"/> took.</summary>
        public static void BuildTeleportRelayDressing(Transform root, Vector3 size)
        {
            EnsureCityMaterials();

            float hw = size.x * 0.5f, hh = size.y * 0.5f, hd = size.z * 0.5f;
            float baseRadius = Mathf.Min(hw, hd);

            MeshRenderer baseGlow = CharacterPart.AddLens(root, CharacterMeshes.Ring(baseRadius * 0.92f, baseRadius * 1.08f, 0.04f),
                new Vector3(0f, -hh + 0.02f, 0f), Quaternion.identity, Vector3.one);
            baseGlow.gameObject.name = "RelayGlow";
            TintLens(baseGlow, RelayGlowRed);

            // The mast — rooted at the hull's own top, so "2.8 m on top" reads off the real box height.
            var mast = new GameObject("RelayMast");
            mast.transform.SetParent(root, worldPositionStays: false);
            mast.transform.localPosition = new Vector3(0f, hh, 0f);

            Add(mast.transform, CharacterMeshes.Prism(8, RelayMastRadius, RelayMastRadius, RelayMastHeight, 0.015f),
                s_citySteel, new Vector3(0f, RelayMastHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "MastPole");

            MeshRenderer ring = CharacterPart.AddLens(mast.transform,
                CharacterMeshes.Ring(RelayMastRadius * 1.6f, RelayMastRadius * 2.4f, 0.05f),
                new Vector3(0f, RelayMastHeight * 0.62f, 0f), Quaternion.identity, Vector3.one);
            ring.gameObject.name = "RelayRing";
            TintLens(ring, RelayGold);

            MeshRenderer eye = CharacterPart.AddLens(mast.transform, CharacterMeshes.Sphere(14),
                new Vector3(0f, RelayMastHeight, 0f), Quaternion.identity, Vector3.one * 0.28f);
            eye.gameObject.name = "RelayEye";
            TintLens(eye, RelayGold);
        }

        /// <summary>Public (MV-1297): <see cref="MaxWorlds.Factories.AssemblyCradle"/> re-tints its own
        /// top lens for the STANDBY dim/pulse, the same MaterialPropertyBlock mechanism every other lens
        /// in this file already uses.</summary>
        public static void TintLens(MeshRenderer r, Color c)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }

        private static void EnsureCityMaterials()
        {
            if (s_citySteel != null) return;
            s_citySteel = NewMaterial("Factory_CitySteel", new Color(0.58f, 0.60f, 0.64f));
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

        // ---------------------------------------------------------------- MV-1227: Mothership Assembly Cradle

        /// <summary>MV-1227: the cradle's own reserved red — "Red is reserved for cradles in this world"
        /// (ticket), #E2483D.</summary>
        private static readonly Color CradleRed = new Color(0.886f, 0.282f, 0.239f);

        /// <summary>Public (MV-1297): <see cref="MaxWorlds.Factories.AssemblyCradle"/> restores its top
        /// lens to this exact tint when leaving STANDBY, so the two never drift to slightly different
        /// golds.</summary>
        public static readonly Color CradleGold = new Color(1.00f, 0.80f, 0.20f);
        private static readonly Color CradlePreviewGrey = new Color(0.55f, 0.55f, 0.58f);

        /// <summary>The same idle amber every live robot's own eye rests at (RobotRig's own
        /// EyeIdle) — so a cradle's "Done, eye lit" preview reads as the exact tell the finished robot
        /// will carry once it steps out.</summary>
        private static readonly Color CradleEyeLit = new Color(0.90f, 0.72f, 0.22f);

        private static Material s_cradleRed;
        private static Material s_cradleGrey;
        private static Material s_previewCool;
        private static Material s_previewDark;
        private static Material s_previewGold;

        /// <summary>Cached per <see cref="EnemyKind"/> (MV-1227) — rebuilt every ~4 s build cycle while a
        /// cradle is alive, so a fresh <c>Material</c> per call would leak native memory over a long
        /// session; keyed and created once, same reasoning as <see cref="RobotRig"/>'s own
        /// <c>s_sharedCharacterMats</c>.</summary>
        private static readonly Dictionary<EnemyKind, Material> s_previewWarmByKind = new Dictionary<EnemyKind, Material>(16);

        /// <summary>What <see cref="MaxWorlds.Factories.AssemblyCradle"/> needs back from
        /// <see cref="BuildAssemblyCradle"/>: the two arms to swing while building, the turret housings
        /// (MV-1227 AC1 measures their own resolved width), and the empty bay root
        /// <see cref="BuildCradlePreview"/> populates once a build cycle's kind is known.</summary>
        public readonly struct CradleGantry
        {
            public readonly Transform[] Arms;
            public readonly Transform BayRoot;
            public readonly Transform[] TurretHousings;

            /// <summary>MV-1297: the gold lens on top — the one static, always-visible light
            /// <see cref="MaxWorlds.Factories.AssemblyCradle"/> dims and slow-pulses for its STANDBY
            /// tell, restoring <see cref="CradleGold"/> the rest of the time.</summary>
            public readonly MeshRenderer TopLens;

            public CradleGantry(Transform[] arms, Transform bayRoot, Transform[] turretHousings, MeshRenderer topLens)
            {
                Arms = arms; BayRoot = bayRoot; TurretHousings = turretHousings; TopLens = topLens;
            }
        }

        /// <summary>What one <see cref="BuildCradlePreview"/> call hands back — the three stage roots
        /// <see cref="MaxWorlds.Factories.AssemblyCradle"/> shows one-at-a-time by toggling every
        /// renderer's own <c>enabled</c> flag underneath (never <c>SetActive</c> — MV-1227 AC1 reads the
        /// renderer's own resolved flag), one per <c>AssemblyCradle.BuildStage</c>.</summary>
        public readonly struct CradlePreview
        {
            public readonly Transform Frame;
            public readonly Transform Plates;
            public readonly Transform Done;

            public CradlePreview(Transform frame, Transform plates, Transform done)
            {
                Frame = frame; Plates = plates; Done = done;
            }
        }

        /// <summary>Builds the cradle's static gantry (MV-1227) onto <paramref name="root"/> — a
        /// metre-space container at the cradle's own centre, the same <see cref="MaxWorlds.Core.ParentScale.MakeMetreSpace"/>
        /// convention every other factory body in this file uses. <paramref name="size"/> is the
        /// ticket's 3x3x3 m footprint. The build bay itself comes back EMPTY
        /// (<see cref="CradleGantry.BayRoot"/>) — <see cref="BuildCradlePreview"/> populates it once per
        /// build cycle, the moment the kind being built is actually known (the cadence's own
        /// <c>TryNextKind</c> cannot be peeked without consuming it — see
        /// <see cref="MaxWorlds.Factories.AssemblyCradle"/>'s own doc on why).</summary>
        public static CradleGantry BuildAssemblyCradle(Transform root, Vector3 size)
        {
            EnsureCradleMaterials();

            float hw = size.x * 0.5f, hh = size.y * 0.5f, hd = size.z * 0.5f;
            const float postSize = 0.22f;

            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    Add(root, CharacterMeshes.Bevelled(new Vector3(postSize, size.y, postSize), 0.02f), s_cradleRed,
                        new Vector3(sx * (hw - postSize * 0.5f), 0f, sz * (hd - postSize * 0.5f)), Quaternion.identity,
                        Vector3.one, "CornerPost");
                }
            }

            const float railSize = 0.14f;
            Add(root, CharacterMeshes.Bevelled(new Vector3(size.x, railSize, railSize), 0.015f), s_cradleRed,
                new Vector3(0f, hh - railSize * 0.5f, hd - postSize * 0.5f), Quaternion.identity, Vector3.one, "TopRailFront");
            Add(root, CharacterMeshes.Bevelled(new Vector3(size.x, railSize, railSize), 0.015f), s_cradleRed,
                new Vector3(0f, hh - railSize * 0.5f, -(hd - postSize * 0.5f)), Quaternion.identity, Vector3.one, "TopRailBack");

            // The gold lens on top (ticket).
            MeshRenderer topLens = CharacterPart.AddLens(root, CharacterMeshes.Sphere(14),
                new Vector3(0f, hh + 0.14f, 0f), Quaternion.identity, Vector3.one * 0.26f);
            topLens.gameObject.name = "TopLens";
            TintLens(topLens, CradleGold);

            // Two articulated arms reaching into the bay, hinged at the top rail so AssemblyCradle can
            // swing them while a build is in progress (ticket's own "arms move while building").
            var arms = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                var hinge = new GameObject($"Arm{i}").transform;
                hinge.SetParent(root, worldPositionStays: false);
                hinge.localPosition = new Vector3(sx * (hw - postSize), hh - railSize, -(hd - postSize) * 0.4f);
                Add(hinge, CharacterMeshes.Beam(hh * 0.9f, 0.05f, 0.03f, 6), s_cradleRed,
                    new Vector3(0f, -hh * 0.45f, 0f), Quaternion.Euler(0f, 0f, -sx * 35f), Vector3.one, "ArmLink");
                arms[i] = hinge;
            }

            // The 2x2 m out pad (ticket), dashed red, flat, clear of the front face (-Z, the same
            // player-facing convention BuildReplicator's own hatch uses).
            BuildOutPadOutline(root, new Vector3(0f, -hh + 0.01f, -(hd + 1.3f)), 2f, 2f);

            // The two front-top turrets — never plain cubes (Lee, 1/3 Oct: "the big ones looked
            // ridiculous"): a hexagonal housing with a long visible barrel.
            var turrets = new Transform[2];
            const float housingSize = 0.3f; // ticket's own "about 0.3 m (10% of a side)"
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                Vector3 at = new Vector3(sx * (hw - housingSize * 0.7f), hh - housingSize * 0.6f, -(hd - housingSize * 0.3f));
                turrets[i] = BuildCradleTurret(root, at, housingSize, i == 0 ? "TurretLeft" : "TurretRight");
            }

            var bayRoot = new GameObject("Bay").transform;
            bayRoot.SetParent(root, worldPositionStays: false);
            bayRoot.localPosition = new Vector3(0f, -hh, 0f);

            return new CradleGantry(arms, bayRoot, turrets, topLens);
        }

        /// <summary>The front-top turret housing plus barrel — a hexagonal drum, never a plain cube
        /// (Lee, 1/3 Oct: "the big ones looked ridiculous"). Returns the HOUSING transform alone (not
        /// the barrel), so MV-1227 AC1 can measure its own resolved renderer width without the barrel
        /// skewing the bound.</summary>
        private static Transform BuildCradleTurret(Transform root, Vector3 at, float housingSize, string name)
        {
            var turretRoot = new GameObject(name).transform;
            turretRoot.SetParent(root, worldPositionStays: false);
            turretRoot.localPosition = at;

            Transform housing = Add(turretRoot, CharacterMeshes.Bevelled(Vector3.one * housingSize, housingSize * 0.15f),
                s_cradleRed, Vector3.zero, Quaternion.identity, Vector3.one, "Housing");

            const float barrelLength = 0.5f; // ticket's own "long visible barrels"
            Add(turretRoot, CharacterMeshes.Beam(barrelLength, 0.045f, 0.035f, 8), s_cradleGrey,
                new Vector3(0f, 0f, -(housingSize * 0.5f + barrelLength * 0.5f)), Quaternion.Euler(-90f, 0f, 0f),
                Vector3.one, "Barrel");

            return housing;
        }

        /// <summary>The out pad's dashed red outline (ticket: "2x2 m in front, dashed red outline,
        /// flat") — same dashed-segment idiom <see cref="MaxWorlds.Rendering.CityKit.BuildBayOutline"/>
        /// uses, just in the cradle's own reserved red rather than CityKit's road-paint white, and
        /// self-contained here since every pad edge is axis-aligned (no need for CityKit's own
        /// arbitrary-direction helpers).</summary>
        private static void BuildOutPadOutline(Transform root, Vector3 centre, float width, float depth)
        {
            var pad = new GameObject("OutPad").transform;
            pad.SetParent(root, worldPositionStays: false);
            pad.localPosition = centre;

            float hw = width * 0.5f, hd = depth * 0.5f;
            Vector2 a = new Vector2(-hw, -hd), b = new Vector2(hw, -hd), c = new Vector2(hw, hd), d = new Vector2(-hw, hd);
            AppendDashedOutline(pad, a, b);
            AppendDashedOutline(pad, b, c);
            AppendDashedOutline(pad, c, d);
            AppendDashedOutline(pad, d, a);
        }

        private static void AppendDashedOutline(Transform parent, Vector2 a, Vector2 b)
        {
            const float dash = 0.3f, gap = 0.2f, lineWidth = 0.08f, thickness = 0.015f;
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.001f) return;
            Vector2 dir = delta / length;
            float pitch = dash + gap;
            int count = Mathf.Max(1, Mathf.FloorToInt((length + gap) / pitch));
            bool alongX = Mathf.Abs(dir.x) > Mathf.Abs(dir.y);

            for (int i = 0; i < count; i++)
            {
                float start = i * pitch;
                float end = Mathf.Min(start + dash, length);
                if (end <= start) continue;
                Vector2 mid = a + dir * ((start + end) * 0.5f);
                float segLength = end - start;
                Vector3 segSize = alongX
                    ? new Vector3(segLength, thickness, lineWidth)
                    : new Vector3(lineWidth, thickness, segLength);
                Add(parent, CharacterMeshes.Bevelled(segSize, 0.01f), s_cradleRed,
                    new Vector3(mid.x, 0f, mid.y), Quaternion.identity, Vector3.one, $"Dash{i}");
            }
        }

        /// <summary>Rebuilds the bay's three build-stage bodies for <paramref name="kind"/> — called
        /// once per build cycle, the instant the cycle's kind is chosen (MV-1227; see
        /// <see cref="MaxWorlds.Factories.AssemblyCradle"/>'s own doc on why this can only ever run once
        /// per cycle). Destroys whatever the previous cycle left in <paramref name="bayRoot"/> first.
        ///
        /// Frame = a grey skeleton of the kind's own body (ticket) — the SAME generated silhouette as
        /// Plates/Done, just wearing one flat grey material and a dark eye, so "never a generic blob"
        /// (ticket's own do-not-re-raise) holds even at the frame stage. Plates = the body in its own
        /// roster colours (<see cref="MaxWorlds.VFX.CharacterSkin.ResolveBodyColor"/> — the exact colour
        /// the finished robot will wear), eye dark. Done = the same body, eye lit
        /// (<see cref="CradleEyeLit"/>, the same idle amber every live robot's own eye rests at). Built
        /// through the Mothership's own silhouette skin (MV-1226, "mothership") so a Drone/Sentry
        /// previews its ship body, not the base roster one.</summary>
        public static CradlePreview BuildCradlePreview(Transform bayRoot, EnemyKind kind)
        {
            EnsureCradleMaterials();

            for (int i = bayRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = bayRoot.GetChild(i).gameObject;
                // MV-464's own trap: Destroy() is a no-op outside Play mode.
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }

            var frame = new GameObject("StageFrame").transform;
            frame.SetParent(bayRoot, worldPositionStays: false);
            var plates = new GameObject("StagePlates").transform;
            plates.SetParent(bayRoot, worldPositionStays: false);
            var done = new GameObject("StageDone").transform;
            done.SetParent(bayRoot, worldPositionStays: false);

            var greyPalette = new RobotPalette(s_cradleGrey, s_cradleGrey, s_cradleGrey, s_cradleGrey);
            RobotBodies.Body frameBody = RobotBodies.Build(kind, frame, greyPalette, skin: "mothership");
            foreach (MeshRenderer eye in frameBody.Eyes) TintLens(eye, Color.black);

            var realPalette = new RobotPalette(PreviewWarmFor(kind), s_previewCool, s_previewDark, s_previewGold);
            RobotBodies.Body platesBody = RobotBodies.Build(kind, plates, realPalette, skin: "mothership");
            foreach (MeshRenderer eye in platesBody.Eyes) TintLens(eye, Color.black);

            RobotBodies.Body doneBody = RobotBodies.Build(kind, done, realPalette, skin: "mothership");
            foreach (MeshRenderer eye in doneBody.Eyes) TintLens(eye, CradleEyeLit);

            return new CradlePreview(frame, plates, done);
        }

        private static Material PreviewWarmFor(EnemyKind kind)
        {
            if (s_previewWarmByKind.TryGetValue(kind, out Material mat) && mat != null) return mat;
            CharacterRole role = CharacterSkin.RoleFor(kind);
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            Color bodyColor = CharacterSkin.ResolveBodyColor(role, archetype.Skin, archetype.ColourRole);
            mat = NewMaterial("CradlePreview_Warm_" + kind, bodyColor);
            s_previewWarmByKind[kind] = mat;
            return mat;
        }

        /// <summary>MV-1338: the Mothership deck's own bright ambient/key light washed
        /// <see cref="CradleRed"/> toward grey — <c>StylizedCharacter.shader</c>'s <c>LitFrag</c>
        /// computes <c>_BaseColor.rgb * (ambient + mainLight.color * ndotl * shadow)</c>, so a plain
        /// diffuse red reads however bright the incident light happens to be, same mechanism documented
        /// on every other reserved hazard tone in this project (<see cref="MaxWorlds.Rendering.WorldMaterials.M_Hazard"/>,
        /// every emissive sink in <see cref="MaxWorlds.Rendering.ShipKit"/>) — all of which carry their
        /// own emission for exactly that reason, and none of which the cradle's gantry ever got. Emission
        /// is ADDED after the lit term, never multiplied by it, so it reads this exact red regardless of
        /// how bright or dim the deck's own lighting is.</summary>
        private static void TintCradleRedEmissive(Material m)
        {
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", CradleRed);
        }

        private static void EnsureCradleMaterials()
        {
            if (s_cradleRed != null) return;
            s_cradleRed = NewMaterial("Cradle_Red", CradleRed);
            TintCradleRedEmissive(s_cradleRed);
            s_cradleGrey = NewMaterial("Cradle_Grey", CradlePreviewGrey);
            s_previewCool = NewMaterial("CradlePreview_Cool", CharacterSkin.RobotCool);
            s_previewDark = NewMaterial("CradlePreview_Dark", CharacterSkin.RobotDark);
            s_previewGold = NewMaterial("CradlePreview_Gold", CharacterSkin.RobotGold);
        }
    }
}
