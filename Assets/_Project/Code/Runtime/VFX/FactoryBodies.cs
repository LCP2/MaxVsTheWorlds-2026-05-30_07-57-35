// MV-693 EXCEPTION: hand-authored, same reason as RobotBodies' own BuildBolter/BuildLurker/
// BuildTurret/BuildSludger — robot-gen-mesh.html is an interactive browser tool this worker
// cannot drive headlessly. Flagged for Lee to fold into the design source at his convenience.
using System.Collections.Generic;
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
            /// <summary>MV-1208: "a short green flash marks it" — a lens at the output face, built for
            /// every Replicator (cheap, inert, same as <see cref="ReplicationBeacon"/>) but only ever
            /// triggered by a City relay's beam-in Exit beat; World 2 never touches it.</summary>
            public readonly MeshRenderer ExitFlashGreen;

            public ReplicatorParts(Transform hatch, MeshRenderer hatchGlow, MeshRenderer emitFlash, MeshRenderer led,
                                   Transform fan, Transform outputLip, MeshRenderer statusRing,
                                   MeshRenderer replicationBeacon, LineRenderer pullBeam, Transform[] hazardDressing,
                                   MeshRenderer exitFlashGreen)
            {
                Hatch = hatch; HatchGlow = hatchGlow; EmitFlash = emitFlash; Led = led; Fan = fan;
                OutputLip = outputLip; StatusRing = statusRing;
                ReplicationBeacon = replicationBeacon; PullBeam = pullBeam; HazardDressing = hazardDressing;
                ExitFlashGreen = exitFlashGreen;
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

            // MV-1208: the City beam-in's "a short green flash marks it" tell — mirrors EmitFlash's own
            // lens-at-the-face placement, just on the OUTPUT face instead of the hatch, and coloured by
            // the Replicator that owns it rather than at build time (same "built inert, painted by the
            // owner" convention as ReplicationBeacon/PullBeam above). Harmless and unused for World 2.
            MeshRenderer exitFlashGreen = CharacterPart.AddLens(root, CharacterMeshes.Sphere(14),
                outputLipGo.transform.localPosition + new Vector3(0f, 0f, 0.14f), Quaternion.identity,
                Vector3.one * (hatchHalfW * 1.1f));
            exitFlashGreen.gameObject.name = "CityExitFlash";

            return new ReplicatorParts(hatch, hatchGlow, emitFlash, led.GetComponent<MeshRenderer>(), fan,
                outputLipGo.transform, statusRing, replicationBeacon, pullBeam, hazardDressing.ToArray(),
                exitFlashGreen);
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

        /// <summary>MV-1208: what <see cref="Replicator"/> needs back from the City dressing pass —
        /// the aperture rim (pulsed brighter by the owning Replicator while a robot is arriving) and
        /// its anchor point (the descent target, and the world point the pooled column/sky-line VFX
        /// and the reused <see cref="ReplicatorParts.PullBeam"/> all aim at), plus the pooled beam
        /// VFX themselves — built once here, never per-arrival, per the ticket's own "no per-arrival
        /// allocations" rule.</summary>
        public readonly struct TeleportRelayParts
        {
            public readonly MeshRenderer ApertureRim;
            public readonly Transform ApertureAnchor;
            public readonly MeshRenderer[] BeamColumns;
            public readonly LineRenderer[] SkyLines;

            public TeleportRelayParts(MeshRenderer apertureRim, Transform apertureAnchor,
                MeshRenderer[] beamColumns, LineRenderer[] skyLines)
            {
                ApertureRim = apertureRim; ApertureAnchor = apertureAnchor;
                BeamColumns = beamColumns; SkyLines = skyLines;
            }
        }

        /// <summary>MV-1208: how many concurrent descents this relay can show at once without a
        /// per-arrival allocation — comfortably above the steady-state of ~1 (a 2.5 s beam-in interval
        /// against a 1.2 s descent), so a timing edge never has to drop a visual.</summary>
        private const int CityBeamVfxPoolSize = 2;

        /// <summary>MV-1208: the relay's own reserved green — the pull beam, the teleport column, the
        /// sky line and the aperture rim all share it, matching <see cref="Replicator.ReplicationLightColor"/>
        /// (the ticket's own "reuses existing World 2 ... effects").</summary>
        private static readonly Color RelayTeleportGreen = new Color(0.20f, 1.00f, 0.35f);

        private static Material s_teleportColumn;

        /// <summary>Builds the City's Teleport Relay topside (MV-1201) onto an otherwise-empty
        /// <paramref name="root"/> — <see cref="Replicator.ApplyCityRelaySkin"/>'s own dressing root,
        /// laid over a box that already carries <see cref="BuildReplicator"/>'s default World 2 look
        /// with its hazard striping hidden. A red glow ring at the base ("because the robots hold it",
        /// Lee 16 Oct) and a mast — named "RelayMast", read back by <c>Replicator</c>'s own EditMode
        /// test — carrying one gold ring and one gold eye lamp. <paramref name="size"/> is the same
        /// box footprint <see cref="BuildReplicator"/> took.
        ///
        /// MV-1208 adds the round aperture robots are beamed down into (a dark disc with a glowing
        /// green rim) and the pooled teleport-column/sky-line VFX a beam-in descent drives — see
        /// <see cref="TeleportRelayParts"/>.</summary>
        public static TeleportRelayParts BuildTeleportRelayDressing(Transform root, Vector3 size)
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

            // MV-1208: the aperture — "a round opening 1.0 m across: a dark disc with a glowing green
            // rim" — on the hull top, clear of the mast's own footprint (RelayMastRadius is tiny by
            // comparison, no overlap to worry about).
            const float apertureDiameter = 1.0f;
            float apertureOuter = apertureDiameter * 0.5f;
            Vector3 apertureLocal = new Vector3(0f, hh + 0.02f, 0f);

            var apertureAnchorGo = new GameObject("Aperture");
            apertureAnchorGo.transform.SetParent(root, worldPositionStays: false);
            apertureAnchorGo.transform.localPosition = apertureLocal;

            Add(root, CharacterMeshes.Prism(16, apertureOuter, apertureOuter, 0.02f, 0.01f), s_dark,
                apertureLocal, Quaternion.identity, Vector3.one, "ApertureDisc");

            MeshRenderer apertureRim = CharacterPart.AddLens(root,
                CharacterMeshes.Ring(apertureOuter * 0.8f, apertureOuter, 0.03f),
                apertureLocal + Vector3.up * 0.01f, Quaternion.identity, Vector3.one);
            apertureRim.gameObject.name = "ApertureRim";
            TintLens(apertureRim, RelayTeleportGreen * 0.6f);

            // MV-1208: the pooled descent VFX — a translucent column (built the same way as Max's own
            // force field, see BuildBeamColumn) and a thin sky line, reused across arrivals via an
            // index into these arrays rather than built fresh each time.
            var columns = new MeshRenderer[CityBeamVfxPoolSize];
            var skyLines = new LineRenderer[CityBeamVfxPoolSize];
            for (int i = 0; i < CityBeamVfxPoolSize; i++)
            {
                columns[i] = BuildBeamColumn(root, $"TeleportColumn{i}");
                skyLines[i] = BuildSkyLine(root, $"TeleportSkyLine{i}");
            }

            return new TeleportRelayParts(apertureRim, apertureAnchorGo.transform, columns, skyLines);
        }

        /// <summary>MV-1208: "a wider glowing column like Max's force field" — same shader
        /// (<c>MaxWorlds/ForceFieldShield</c>, Fresnel rim + alpha, see <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>)
        /// and the same stock-primitive-plus-<see cref="SelfDrivenTint"/> construction, swapping the
        /// bubble's Sphere for a Cylinder. Built inactive, scaled and repositioned every tick by the
        /// owning Replicator — never rebuilt per arrival.</summary>
        private static MeshRenderer BuildBeamColumn(Transform root, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(col);
                else UnityEngine.Object.DestroyImmediate(col);
            }
            go.transform.SetParent(root, worldPositionStays: false);
            go.AddComponent<MaxWorlds.Core.SelfDrivenTint>();

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = TeleportColumnMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return mr;
        }

        /// <summary>MV-1208: "the green pull beam ... from the aperture to the robot" reuses
        /// <see cref="ReplicatorParts.PullBeam"/> directly; this is the SECOND green line the ticket
        /// asks for — "a thin line extends 6 m up from the robot into the sky" — pooled the same way
        /// as <see cref="BuildBeamColumn"/>.</summary>
        private static LineRenderer BuildSkyLine(Transform root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, worldPositionStays: false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.05f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            go.SetActive(false);
            return lr;
        }

        private static Material TeleportColumnMaterial()
        {
            if (s_teleportColumn != null) return s_teleportColumn;
            var shader = Shader.Find("MaxWorlds/ForceFieldShield");
            s_teleportColumn = shader != null && shader.isSupported
                ? new Material(shader) { name = "TeleportColumnShield", hideFlags = HideFlags.HideAndDontSave }
                : VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            return s_teleportColumn;
        }

        private static void TintLens(MeshRenderer r, Color c)
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
    }
}
