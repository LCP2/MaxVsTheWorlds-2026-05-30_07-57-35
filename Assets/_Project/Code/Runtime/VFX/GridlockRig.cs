using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// GRIDLOCK's body (MV-1299) — Lee's approved "The Core" concept (option B, red, 10 Oct): a
    /// six-sided drum of rack lights, white ceramic over black glass split on one hard straight line,
    /// a turning crystal core on a roof dial ringed by a clock track (MV-1306 drives the fill — this
    /// ticket only builds the ring's geometry), one big gold eye off-centre, one thin gold ring, and
    /// six needle legs that light the floor round her in a 1 m grid as she walks.
    ///
    /// Built from <see cref="CharacterMeshes"/>'s prism/beam/ring vocabulary only — no
    /// <c>GameObject.CreatePrimitive</c> anywhere on the character or its floor dressing (the ticket's
    /// own "generated meshes only").
    ///
    /// The six legs are a single straight beam each (no knee — the image shows a straight needle, not
    /// a bent limb), planted by the same <see cref="HexapodGaitDriver"/> every other six-legged boss
    /// walks on (<see cref="SludgequeenRig"/>'s own "built for Sludgequeen, kept generic" driver). A
    /// needle leg's hip-to-foot distance genuinely changes every step (there is no knee to absorb it),
    /// so unlike a jointed limb's fixed-length beam this one is built once at a canonical UNIT length
    /// and re-stretched every tick via a non-uniform local scale on Y only (see
    /// <see cref="UpdateLeg"/>) — cheaper than rebuilding geometry, and the radius never distorts
    /// because only Y is touched.
    ///
    /// Unparented and follows the boss each LateUpdate (position + yaw), same reasoning as every other
    /// boss rig's own <c>Follow</c>. The floor-light grid follows POSITION ONLY — an axis-aligned grid
    /// that rotated with her facing would read as broken, not lit.
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("rig")]
    public sealed class GridlockRig : MonoBehaviour
    {
        /// <summary>Give <paramref name="boss"/> its own rig, bound to it alone — same per-instance
        /// reasoning as every other boss rig (MV-573): a multi-boss arena must not have only its first
        /// boss grow a body.</summary>
        public static GridlockRig CreateFor(GridlockBoss boss)
        {
            if (boss == null) return null;
            var rig = new GameObject("GridlockRig").AddComponent<GridlockRig>();
            rig.Bind(boss);
            return rig;
        }

        // ---------------------------------------------------------------- dimensions (authored for
        // the ticket's own 6.8 m across / 4.9 m tall; the whole rig is scaled by authored width / 6.8
        // in Bind, same convention as every other boss rig)

        private const float AuthoredBodyWidth = 6.8f;
        private const int DrumSides = 6;
        private const float DrumRadius = 3.4f;

        private const float DrumBottomY = 1.6f;
        private const float DrumLowerH = 1.0f;   // white ceramic band
        private const float DrumUpperH = 0.8f;   // black glass band
        private static float DrumTopY => DrumBottomY + DrumLowerH + DrumUpperH;

        private const float DialRadius = 2.0f, DialHeight = 0.15f;
        private const float ClockRingInner = 2.05f, ClockRingOuter = 2.35f, ClockRingThickness = 0.12f;

        private const float CoreR0 = 0.55f, CoreR1 = 0.03f, CoreHeight = 1.35f;
        private const float CoreSpinDegPerSec = 45f;

        private static readonly Vector3 EyeAnchorDir = new Vector3(-0.85f, 0f, 0.4f);
        private const float EyeAnchorY = 2.3f;
        private const float EyeBezelRadius = 0.6f, EyeLensRadius = 0.4f, EyeHighlightRadius = 0.16f;

        private const float GoldRingPostHeight = 0.5f, GoldRingOuter = 0.42f, GoldRingY = 0.5f;
        private static readonly Vector3 GoldRingOffset = new Vector3(0.6f, 0f, -0.3f);

        public const int LegCount = 6;
        private const float HipRadius = 2.6f, FootRadius = 3.6f, FootY = 0.05f;
        private const float LegR0 = 0.11f, LegR1 = 0.06f;
        private const int LegSides = 8;

        // A sparse 1 m grid round her feet, clear of her own 3.6 m foot radius -- a half-extent that
        // only reached 2 m sat entirely UNDER the drum once she was built at her true 6.8 m scale.
        private const int FloorGridHalfExtent = 6;
        private const float FloorGridClearRadius = FootRadius + 0.5f;
        private const float FloorTileSize = 0.92f, FloorTileHeight = 0.02f;

        // ---------------------------------------------------------------- pylon shield (MV-1309): one
        // red ring round her per live pylon (Pylon.LiveFeedersOf, via GridlockBoss.ShieldRingCount),
        // same concentric-ring-per-feeder shape RelayDome already uses for a relay's own dome, minus
        // the dome fill -- the ticket's own prose asks for "a ring round her", not a dome.
        private const int MaxShieldRings = 4;
        private const float ShieldRingBaseRadius = FootRadius + 0.6f;
        private const float ShieldRingSpacing = 0.35f;
        private const float ShieldRingY = 0.06f;
        private const float ShieldRingFlashSeconds = 0.15f; // ticket point 2: "a blocked hit flashes the ring"
        private static readonly Color ShieldRingColor = new Color(1f, 0.15f, 0.12f, 1f);
        private static readonly Color ShieldRingFlashColor = Color.white;

        private readonly GameObject[] _shieldRings = new GameObject[MaxShieldRings];
        private float _shieldFlashRemaining;

        // ---------------------------------------------------------------- palette (the ticket's own
        // red accent #ff2a3a, white rack lights, white ceramic / black glass split, gold eye+ring)

        private static readonly Color WhiteCeramic = new Color(0.86f, 0.86f, 0.88f);
        private static readonly Color BlackGlass = new Color(0.06f, 0.06f, 0.08f);
        private static readonly Color DarkSteel = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color AccentRed = new Color32(0xFF, 0x2A, 0x3A, 0xFF);
        private static readonly Color GoldColor = new Color32(0xFF, 0xB0, 0x20, 0xFF);
        private static readonly Color RackWhite = Color.white;
        private static readonly Color FloorGlow = new Color(0.9f, 0.3f, 0.32f);

        private static Material s_white, s_black, s_steel, s_accent, s_gold;

        private static void EnsureMaterials()
        {
            if (s_white != null) return;
            s_white = NewMaterial("Gridlock_WhiteCeramic", WhiteCeramic);
            s_black = NewMaterial("Gridlock_BlackGlass", BlackGlass);
            s_steel = NewMaterial("Gridlock_DarkSteel", DarkSteel);
            s_accent = NewMaterial("Gridlock_AccentRed", AccentRed);
            s_gold = NewMaterial("Gridlock_Gold", GoldColor);
        }

        private static Material NewMaterial(string name, Color color)
        {
            // No character shader in this build is a look regression, never a magenta one (YT-58): a
            // plain lit material still draws the right colour, just without the outline.
            var template = MaterialLibrary.Character();
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);
            if (m == null) return null;
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ---------------------------------------------------------------- state

        private GridlockBoss _boss;
        private MaterialPropertyBlock _mpb;
        private Transform _colliderRoot;
        private Transform _coreTransform;
        private Transform _floorGridRoot;

        private readonly Transform[] _legHips = new Transform[LegCount];
        private readonly Transform[] _legFeet = new Transform[LegCount];
        private readonly Transform[] _legBeams = new Transform[LegCount];
        private readonly float[] _legAngleDeg = new float[LegCount];

        private HexapodGaitDriver _gait;
        private float _rigScale;
        private bool _dying;

        /// <summary>The boss this rig is bound to — same "a caller can match a rig back to its own
        /// boss" reasoning as every other boss rig's own <c>Boss</c> getter.</summary>
        public GridlockBoss Boss => _boss;

        private void OnEnable()
        {
            HudSignals.BossDefeated += OnDefeated;
            HudSignals.GridlockShieldBlocked += OnShieldBlocked;
        }

        private void OnDisable()
        {
            HudSignals.BossDefeated -= OnDefeated;
            HudSignals.GridlockShieldBlocked -= OnShieldBlocked;
        }

        /// <summary>MV-1309: only one GRIDLOCK (and one rig) ever exists in the game, so unlike
        /// <see cref="OnDefeated"/>'s own "every rig built so far hears it" caveat (shared across every
        /// boss kind), this signal has no identity to mismatch.</summary>
        private void OnShieldBlocked(Vector3 pos) => _shieldFlashRemaining = ShieldRingFlashSeconds;

        private void Bind(GridlockBoss boss)
        {
            _boss = boss;
            gameObject.AddComponent<KeepsOwnMaterial>();
            _mpb = new MaterialPropertyBlock();

            // The greybox cube goes; its collider stays (the CharacterController is what Max and the
            // player's weapons actually hit) — same split every other boss rig's own Bind uses.
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            _rigScale = _boss.transform.localScale.x / AuthoredBodyWidth;
            transform.localScale = Vector3.one * _rigScale;

            Build();
            Follow();

            _gait = new HexapodGaitDriver(_legAngleDeg, FootRadius * _rigScale, FootY * _rigScale,
                _rigScale, transform.position, transform.eulerAngles.y);

            _boss.FitColliderTo(RenderedBoundsRelativeTo(_boss.transform, _colliderRoot));

            _floorGridRoot = new GameObject("GridlockFloorGrid").transform;
            BuildFloorGrid();
        }

        private void Build()
        {
            EnsureMaterials();

            var colliderGo = new GameObject("Drum");
            colliderGo.transform.SetParent(transform, worldPositionStays: false);
            _colliderRoot = colliderGo.transform;

            BuildDrum();
            BuildRackLights();
            BuildDial();
            BuildCore();
            BuildEye();
            BuildGoldRing();
            BuildLegs();
            BuildTelegraph();
            BuildShieldRings();
        }

        /// <summary>The six-sided housing itself: white ceramic below, black glass above, split on one
        /// hard straight horizontal line (the ticket's own words) — two stacked prism sections rather
        /// than a blended/diagonal split, so the boundary is a literal, single, straight edge running
        /// all the way round, not an approximation of one.</summary>
        private void BuildDrum()
        {
            float lowerCenterY = DrumBottomY + DrumLowerH * 0.5f;
            CharacterPart.Add(_colliderRoot, CharacterMeshes.Prism(DrumSides, DrumRadius, DrumRadius, DrumLowerH),
                s_white, new Vector3(0f, lowerCenterY, 0f), Quaternion.identity, Vector3.one, "DrumLowerCeramic");

            float upperCenterY = DrumBottomY + DrumLowerH + DrumUpperH * 0.5f;
            CharacterPart.Add(_colliderRoot, CharacterMeshes.Prism(DrumSides, DrumRadius, DrumRadius, DrumUpperH),
                s_black, new Vector3(0f, upperCenterY, 0f), Quaternion.identity, Vector3.one, "DrumUpperGlass");
        }

        /// <summary>Small white "rack light" lenses studding the white ceramic band — two rows round
        /// each of the drum's six faces, read as the lit equipment-rack grid the ticket's own "of rack
        /// lights" calls for. Every third one burns the accent red instead of white, the one place on
        /// the housing itself (besides the core/dial/ring) the ticket's accent colour shows.</summary>
        private void BuildRackLights()
        {
            const int rows = 2, perFaceCols = 2;
            float faceInset = DrumRadius * 0.98f;
            int lightIndex = 0;

            for (int face = 0; face < DrumSides; face++)
            {
                float faceAngle = face * (360f / DrumSides);
                Vector3 faceNormal = AngleDir(faceAngle);
                Vector3 faceTangent = new Vector3(faceNormal.z, 0f, -faceNormal.x);

                for (int row = 0; row < rows; row++)
                {
                    float y = DrumBottomY + DrumLowerH * (0.3f + 0.4f * row);
                    for (int col = 0; col < perFaceCols; col++)
                    {
                        float alongFace = (col - (perFaceCols - 1) * 0.5f) * 0.6f;
                        Vector3 pos = faceNormal * faceInset + faceTangent * alongFace + Vector3.up * y;

                        var lens = CharacterPart.AddLens(transform, CharacterMeshes.Bevelled(new Vector3(0.22f, 0.22f, 0.04f), 0.03f),
                            pos, Quaternion.LookRotation(faceNormal, Vector3.up), Vector3.one);
                        lens.gameObject.name = "RackLight";
                        ApplyLensColor(lens, (lightIndex % 3 == 0) ? AccentRed : RackWhite);
                        lightIndex++;
                    }
                }
            }
        }

        /// <summary>The roof dial and its clock ring track. <see cref="BuildTelegraph"/> is what
        /// actually fills the ring toward her next attack (MV-1306) and shows the warning shapes.</summary>
        private void BuildDial()
        {
            CharacterPart.Add(transform, CharacterMeshes.Prism(24, DialRadius, DialRadius, DialHeight),
                s_steel, new Vector3(0f, DrumTopY + DialHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "RoofDial");

            CharacterPart.Add(transform, CharacterMeshes.Ring(ClockRingInner, ClockRingOuter, ClockRingThickness),
                s_accent, new Vector3(0f, DrumTopY + DialHeight + 0.01f, 0f), Quaternion.identity, Vector3.one, "ClockRing");
        }

        // ---------------------------------------------------------------- warning telegraph (MV-1306)

        private const float TellY = 0.02f;
        private const float TellTriBase = 0.22f, TellTriTip = 0.02f, TellTriHeight = 0.14f;
        private const float TellCrossLength = 0.85f, TellCrossThickness = 0.1f, TellCrossHeight = 0.1f;
        private const int FillPipCount = 12;

        private Transform _squadTell, _jumpTell, _strikeTell;
        private MeshRenderer[] _fillPips;

        /// <summary>The three white SHAPES the ticket's own "never a traffic-light colour" calls for
        /// (chevrons/double-arrow/crosshair), plus a ring of discrete pip lights standing in for the
        /// clock ring's fill — same "a light is a lit/unlit discrete element" idiom the rack lights
        /// and floor grid already use on this rig, rather than a partial-arc shader
        /// <see cref="CharacterMeshes.Ring"/> has no support for. Built once, inactive; toggled per
        /// frame in <see cref="UpdateTelegraph"/> off <see cref="GridlockBoss"/>'s own read-only
        /// state — gameplay says what's imminent, the rig only shows it.</summary>
        private void BuildTelegraph()
        {
            float y = DrumTopY + DialHeight + TellY;

            _squadTell = NewTellRoot("SquadTell", y);
            AddTellMarker(_squadTell, -0.32f);
            AddTellMarker(_squadTell, 0f);
            AddTellMarker(_squadTell, 0.32f);

            _jumpTell = NewTellRoot("JumpTell", y);
            AddTellMarker(_jumpTell, -0.22f);
            AddTellMarker(_jumpTell, 0.22f);

            _strikeTell = NewTellRoot("StrikeTell", y);
            Vector3 crossCenter = new Vector3(0f, TellCrossHeight * 0.5f, 0f);
            var barA = CharacterPart.AddLens(_strikeTell,
                CharacterMeshes.Bevelled(new Vector3(TellCrossLength, TellCrossHeight, TellCrossThickness), 0.01f),
                crossCenter, Quaternion.identity, Vector3.one);
            ApplyLensColor(barA, Color.white);
            var barB = CharacterPart.AddLens(_strikeTell,
                CharacterMeshes.Bevelled(new Vector3(TellCrossThickness, TellCrossHeight, TellCrossLength), 0.01f),
                crossCenter, Quaternion.identity, Vector3.one);
            ApplyLensColor(barB, Color.white);

            BuildFillPips(y + 0.01f);
        }

        private Transform NewTellRoot(string name, float y)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, worldPositionStays: false);
            root.localPosition = new Vector3(0f, y, 0f);
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>One small white triangular nub standing on the dial at local Z
        /// <paramref name="localZ"/> — three of these in a row read as the SQUAD tell's chevrons,
        /// two side by side read as the JUMP tell's double arrow (same vertical-Prism convention
        /// <see cref="BuildCore"/>'s own crystal spike already uses, so no new rotation maths is
        /// needed to keep it readable from the fixed top-down camera).</summary>
        private void AddTellMarker(Transform parent, float localZ)
        {
            Vector3 pos = new Vector3(0f, TellTriHeight * 0.5f, localZ);
            var marker = CharacterPart.AddLens(parent, CharacterMeshes.Prism(3, TellTriBase, TellTriTip, TellTriHeight),
                pos, Quaternion.identity, Vector3.one);
            ApplyLensColor(marker, Color.white);
        }

        private void BuildFillPips(float y)
        {
            float radius = (ClockRingInner + ClockRingOuter) * 0.5f;
            _fillPips = new MeshRenderer[FillPipCount];
            for (int i = 0; i < FillPipCount; i++)
            {
                float angleDeg = i * (360f / FillPipCount);
                Vector3 pos = AngleDir(angleDeg) * radius + Vector3.up * (y + 0.015f);
                var pip = CharacterPart.AddLens(transform, CharacterMeshes.Bevelled(new Vector3(0.1f, 0.03f, 0.1f), 0.015f),
                    pos, Quaternion.identity, Vector3.one);
                pip.gameObject.name = "ClockPip";
                ApplyLensColor(pip, DarkSteel);
                _fillPips[i] = pip;
            }
        }

        /// <summary>Shows exactly one warning shape at a time (the ticket's own three attacks never
        /// telegraph simultaneously — each owns its own cadence) and lights the clock-ring pips up to
        /// <see cref="GridlockBoss.NextAttackProgress01"/>.</summary>
        private void UpdateTelegraph()
        {
            if (_boss == null) return;

            if (_squadTell != null) _squadTell.gameObject.SetActive(_boss.SquadWarningActive);
            if (_jumpTell != null) _jumpTell.gameObject.SetActive(_boss.JumpWarningActive);
            if (_strikeTell != null) _strikeTell.gameObject.SetActive(_boss.StrikeArming);

            UpdateFillPips(_boss.NextAttackProgress01);
        }

        private void UpdateFillPips(float progress01)
        {
            if (_fillPips == null) return;
            int lit = Mathf.RoundToInt(Mathf.Clamp01(progress01) * _fillPips.Length);
            for (int i = 0; i < _fillPips.Length; i++)
                ApplyLensColor(_fillPips[i], i < lit ? Color.white : DarkSteel);
        }

        // ---------------------------------------------------------------- pylon shield (MV-1309)

        /// <summary>Up to <see cref="MaxShieldRings"/> concentric ground rings just outside her feet,
        /// one lit per live pylon (<see cref="UpdateShieldRings"/>) — built once, inactive, same
        /// "toggle per frame off read-only boss state" idiom <see cref="BuildTelegraph"/> already uses.
        /// Parented to the yaw-following rig root like every other part here: a ring is radially
        /// symmetric, so rotating with her facing is invisible.</summary>
        private void BuildShieldRings()
        {
            for (int i = 0; i < MaxShieldRings; i++)
            {
                var go = new GameObject($"ShieldRing{i}");
                go.transform.SetParent(transform, worldPositionStays: false);

                var lr = go.AddComponent<LineRenderer>();
                const int segments = 32;
                lr.positionCount = segments + 1;
                lr.useWorldSpace = false;
                lr.widthMultiplier = 0.12f;
                lr.material = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
                lr.startColor = lr.endColor = ShieldRingColor;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                float ringRadius = ShieldRingBaseRadius + i * ShieldRingSpacing;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments * Mathf.PI * 2f;
                    lr.SetPosition(s, new Vector3(Mathf.Cos(t) * ringRadius, ShieldRingY, Mathf.Sin(t) * ringRadius));
                }

                go.SetActive(false);
                _shieldRings[i] = go;
            }
        }

        /// <summary>Ticket points 2/3: one ring lit per <see cref="GridlockBoss.ShieldRingCount"/> live
        /// pylon, each pylon's death bursting the outermost one (a ring simply goes inactive — no
        /// separate burst animation the way <c>RelayDome</c>'s own dome plays, since a flat ring has no
        /// comparable "flare outward" read), and every lit ring flashing white for
        /// <see cref="ShieldRingFlashSeconds"/> on a blocked hit (<see cref="OnShieldBlocked"/>).</summary>
        private void UpdateShieldRings(float dt)
        {
            if (_shieldFlashRemaining > 0f) _shieldFlashRemaining = Mathf.Max(0f, _shieldFlashRemaining - dt);

            int live = _boss != null ? Mathf.Clamp(_boss.ShieldRingCount, 0, MaxShieldRings) : 0;
            Color color = _shieldFlashRemaining > 0f ? ShieldRingFlashColor : ShieldRingColor;

            for (int i = 0; i < MaxShieldRings; i++)
            {
                GameObject ring = _shieldRings[i];
                if (ring == null) continue;
                bool shouldShow = i < live;
                if (ring.activeSelf != shouldShow) ring.SetActive(shouldShow);
                if (!shouldShow) continue;

                var lr = ring.GetComponent<LineRenderer>();
                if (lr != null) lr.startColor = lr.endColor = color;
            }
        }

        /// <summary>The turning crystal core — a faceted red spike, spinning continuously on its own Y
        /// axis (<see cref="SpinCore"/>), the one part of her that never stops moving even standing
        /// still, same "never reads as a statue" intent as her own feet drifting at standoff.</summary>
        private void BuildCore()
        {
            float centerY = DrumTopY + DialHeight + CoreHeight * 0.5f;
            Transform core = CharacterPart.Add(transform, CharacterMeshes.Prism(4, CoreR0, CoreR1, CoreHeight, 0.05f),
                s_accent, new Vector3(0f, centerY, 0f), Quaternion.identity, Vector3.one, "CrystalCore");
            _coreTransform = core;
        }

        /// <summary>The one big off-centre gold eye: a dark bezel, a gold lens proud of it, and a small
        /// highlight proud of that — same nested-sphere construction every other boss's single eye
        /// uses, each offset far enough along the outward direction that their axis-aligned bounds
        /// never overlap.</summary>
        private void BuildEye()
        {
            Vector3 outDir = EyeAnchorDir.normalized;
            Vector3 anchor = outDir * (DrumRadius + EyeBezelRadius * 0.3f) + Vector3.up * EyeAnchorY;
            const float clearanceMargin = 0.05f;

            CharacterPart.Add(transform, CharacterMeshes.Sphere(14), s_steel, anchor,
                Quaternion.identity, Vector3.one * (EyeBezelRadius * 2f), "EyeBezel");

            Vector3 lensPos = OffsetClearingAabb(anchor, outDir, EyeBezelRadius + EyeLensRadius + clearanceMargin);
            var lens = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(14), lensPos,
                Quaternion.identity, Vector3.one * (EyeLensRadius * 2f));
            lens.gameObject.name = "EyeLens";
            ApplyLensColor(lens, GoldColor);

            Vector3 highlightPos = OffsetClearingAabb(lensPos, outDir, EyeLensRadius + EyeHighlightRadius + clearanceMargin);
            var highlight = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(10), highlightPos,
                Quaternion.identity, Vector3.one * (EyeHighlightRadius * 2f));
            highlight.gameObject.name = "EyeHighlight";
            ApplyLensColor(highlight, Color.white);
        }

        /// <summary>Offsets <paramref name="from"/> along <paramref name="dir"/> far enough that the
        /// result's axis-aligned bounds clear a sphere of <paramref name="clearance"/> radius centred
        /// at <paramref name="from"/> — same construction as every other rig's own eye-nesting helper.</summary>
        private static Vector3 OffsetClearingAabb(Vector3 from, Vector3 dir, float clearance)
        {
            float dominantAxis = Mathf.Max(Mathf.Abs(dir.x), Mathf.Max(Mathf.Abs(dir.y), Mathf.Abs(dir.z)));
            float distance = clearance / Mathf.Max(dominantAxis, 1e-4f);
            return from + dir * distance;
        }

        /// <summary>The one thin gold ring — standing proud on its own short post above the dial, the
        /// design's own "ONE GOLD RING" callout.</summary>
        private void BuildGoldRing()
        {
            Vector3 postBase = GoldRingOffset + Vector3.up * (DrumTopY + DialHeight);
            CharacterPart.Add(transform, CharacterMeshes.Beam(GoldRingPostHeight, 0.04f, 0.04f, 6),
                s_steel, postBase + Vector3.up * (GoldRingPostHeight * 0.5f), Quaternion.identity, Vector3.one, "GoldRingPost");

            CharacterPart.Add(transform, CharacterMeshes.Ring(GoldRingOuter - 0.05f, GoldRingOuter, 0.04f),
                s_gold, postBase + Vector3.up * GoldRingPostHeight, Quaternion.identity, Vector3.one, "GoldRing");
        }

        /// <summary>Six needle legs, evenly spaced 60 degrees apart, each a hip root plus a single
        /// straight beam down to its own gait-planted foot — see the class doc for why a needle leg is
        /// one re-stretched beam rather than a two-bone limb.</summary>
        private void BuildLegs()
        {
            for (int i = 0; i < LegCount; i++)
            {
                float angleDeg = 30f + i * (360f / LegCount);
                _legAngleDeg[i] = angleDeg;
                Vector3 dir = AngleDir(angleDeg);

                Vector3 hipPos = dir * HipRadius + Vector3.up * DrumBottomY;
                Vector3 footPos = dir * FootRadius + Vector3.up * FootY;

                var hipGo = new GameObject($"Leg{i}Hip");
                hipGo.transform.SetParent(transform, worldPositionStays: false);
                hipGo.transform.localPosition = hipPos;
                _legHips[i] = hipGo.transform;

                _legBeams[i] = CharacterPart.Add(hipGo.transform, CharacterMeshes.Beam(1f, LegR0, LegR1, LegSides),
                    s_steel, Vector3.zero, Quaternion.identity, Vector3.one, "NeedleLeg");

                var footGo = new GameObject($"Leg{i}Foot");
                footGo.transform.SetParent(transform, worldPositionStays: false);
                footGo.transform.localPosition = footPos;
                _legFeet[i] = footGo.transform;

                CharacterPart.Add(footGo.transform, CharacterMeshes.Prism(6, 0.14f, 0.1f, 0.06f, 0.3f),
                    s_steel, Vector3.zero, Quaternion.identity, Vector3.one, "FootPad");
            }
        }

        /// <summary>A sparse 1 m grid of floor tiles round her feet (the ticket's own "lights the floor
        /// round her in a grid of 1 m squares"), world-axis-aligned and never rotated — see the class
        /// doc for why this lives outside the rotating rig transform.</summary>
        private void BuildFloorGrid()
        {
            for (int gx = -FloorGridHalfExtent; gx <= FloorGridHalfExtent; gx++)
            {
                for (int gz = -FloorGridHalfExtent; gz <= FloorGridHalfExtent; gz++)
                {
                    if (gx * gx + gz * gz < FloorGridClearRadius * FloorGridClearRadius) continue; // under the body/legs -- nothing to see there

                    Vector3 local = new Vector3(gx, 0f, gz) * _rigScale;
                    var tile = CharacterPart.AddLens(_floorGridRoot,
                        CharacterMeshes.Bevelled(new Vector3(FloorTileSize, FloorTileHeight, FloorTileSize) * _rigScale, 0.02f * _rigScale),
                        local, Quaternion.identity, Vector3.one);
                    tile.gameObject.name = "FloorGridTile";
                    ApplyLensColor(tile, FloorGlow);
                }
            }
        }

        /// <summary>0 degrees = local +Z ("the front"), increasing toward +X — the shared angle
        /// convention every other boss rig's own ring/leg placement uses.</summary>
        private static Vector3 AngleDir(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>Same axis-aligned-in-another-frame rebuild as every other rig's own collider-fit
        /// helper (MV-613), scoped to <paramref name="scopeRoot"/>'s own renderers only — the drum, not
        /// the legs (the ticket's own "collider fitted to the drum, not the legs").</summary>
        private Bounds RenderedBoundsRelativeTo(Transform reference, Transform scopeRoot)
        {
            var renderers = scopeRoot.GetComponentsInChildren<MeshRenderer>();
            Bounds world = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

            Vector3 c = world.center, e = world.extents;
            var local = new Bounds(reference.InverseTransformPoint(c), Vector3.zero);
            for (int xi = -1; xi <= 1; xi += 2)
                for (int yi = -1; yi <= 1; yi += 2)
                    for (int zi = -1; zi <= 1; zi += 2)
                        local.Encapsulate(reference.InverseTransformPoint(c + Vector3.Scale(e, new Vector3(xi, yi, zi))));
            return local;
        }

        private void LateUpdate()
        {
            if (_boss == null) return;
            Tick(_dying ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        /// <summary>Everything this rig does once a frame, with an explicit <paramref name="dt"/> —
        /// pulled out of <see cref="LateUpdate"/> so a test can drive it at a controlled rate, same
        /// shape as every other boss rig's own <c>Tick</c>.</summary>
        private void Tick(float dt)
        {
            if (_dying)
            {
                if (_floorGridRoot != null) _floorGridRoot.gameObject.SetActive(false);
                return;
            }

            Follow();
            UpdateGait(dt);
            SpinCore(dt);
            FollowFloorGrid();
            UpdateTelegraph();
            UpdateShieldRings(dt);
        }

        private void Follow()
        {
            Vector3 p = _boss.transform.position;
            var at = new Vector3(p.x, 0f, p.z);
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }

        /// <summary>Position-only follow — an axis-aligned floor grid must never rotate with her
        /// facing, or the squares would skew off the 1 m world grid they're meant to light.</summary>
        private void FollowFloorGrid()
        {
            if (_floorGridRoot == null) return;
            Vector3 p = _boss.transform.position;
            _floorGridRoot.position = new Vector3(p.x, FloorTileHeight * _rigScale, p.z);
        }

        private void SpinCore(float dt)
        {
            if (_coreTransform == null) return;
            // MV-1306: "at half health, her lights run twice as fast" -- the spinning core is the one
            // part of her that's always moving (see this field's own doc comment), so it's what reads
            // as her "lights" speeding up.
            float speedScale = _boss != null ? _boss.LightSpeedScale : 1f;
            _coreTransform.Rotate(Vector3.up, CoreSpinDegPerSec * speedScale * dt, Space.Self);
        }

        /// <summary>Advances the planted-foot walk and re-stretches every needle leg to join its own
        /// fixed hip to the live (gait-driven) foot — see the class doc for why a needle leg stretches
        /// rather than bends.</summary>
        private void UpdateGait(float dt)
        {
            if (_gait == null) return;

            _gait.Tick(transform.position, transform.eulerAngles.y, dt);

            for (int i = 0; i < LegCount; i++)
            {
                Vector3 footWorld = _gait.FootPosition(i);
                _legFeet[i].position = footWorld;
                UpdateLeg(i, footWorld);
            }
        }

        /// <summary>Re-aims and re-stretches leg <paramref name="leg"/>'s single beam to span its own
        /// hip and <paramref name="footWorld"/> — the mesh is baked once at a canonical unit length, so
        /// only position/rotation/scale change here, never the geometry itself.</summary>
        private void UpdateLeg(int leg, Vector3 footWorld)
        {
            Transform beam = _legBeams[leg];
            if (beam == null) return;

            Vector3 hipWorld = _legHips[leg].position;
            Vector3 d = footWorld - hipWorld;
            float dist = d.magnitude;
            if (dist <= 1e-5f) return;

            beam.position = (hipWorld + footWorld) * 0.5f;
            beam.rotation = Quaternion.FromToRotation(Vector3.up, d / dist);
            beam.localScale = new Vector3(1f, dist / Mathf.Max(_rigScale, 1e-4f), 1f);
        }

        private void ApplyLensColor(MeshRenderer r, Color c)
        {
            if (r == null) return;
            c.a = 1f;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        private void OnDefeated()
        {
            // Same MV-625 guard every other boss rig's own OnDefeated uses: HudSignals.BossDefeated
            // carries no identity, and every rig built so far hears it.
            if (_boss == null || !_boss.IsDead) return;
            _dying = true;
            gameObject.SetActive(false);
            if (_floorGridRoot != null) Destroy(_floorGridRoot.gameObject);
        }

        private void OnDestroy()
        {
            if (_floorGridRoot != null) Destroy(_floorGridRoot.gameObject);
        }
    }
}
