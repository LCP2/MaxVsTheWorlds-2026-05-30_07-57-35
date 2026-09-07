using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-699: Sludgequeen's generated body, replacing MV-696's primitives-only greybox (a bare
    /// cylinder drum, two cube hatches, a cylinder wheel) with the same lathe/prism/beam vocabulary
    /// <see cref="FactoryBodies"/>/<see cref="RobotBodies"/> already build every other character
    /// from — no <c>GameObject.CreatePrimitive</c> anywhere on the character itself (the ticket's own
    /// "not Unity primitives" art-pipeline rule).
    ///
    /// A corroded 6x6 pump drum, a 2.5 m valve-wheel crown on top — the motif
    /// <see cref="FactoryBodies.BuildValveWheel"/> put on the Replicator's roof
    /// ("Sludgequeen's motif"), grown up to this boss's own scale (see <see cref="BuildValveWheelCrown"/>
    /// for why that isn't a naive scale-up) — two side intake hatches that flash the brood tell, a
    /// sludge-drip emissive under each, and a single faint violet orb:
    /// the first hint of the Curator's light. That hue is new (no earlier ticket claims a "Curator
    /// violet"); it is kept dim and distinct from the game's other established violets (the bruiser's
    /// skin, Blinker's phase-hue, the teleport flash) so it reads as its own, quieter thing.
    ///
    /// Also grows the VFX beats the ticket calls for: the flood's own rising sludge plane with a foam
    /// leading edge (tracking <see cref="SludgequeenBoss.FloodRect"/> — an ENVIRONMENT overlay, so it
    /// is built the same primitive-cube-plus-<see cref="SludgeFlow"/> way
    /// <c>MapRuntime.BuildSludge</c> already builds ground sludge, not the
    /// character rule above), a phase-2 alarm beacon on the crown, and the drain-away on death — the
    /// same unscaled <see cref="BigBermudaRig.TickDeath"/> shape, guarded the same MV-625 way against
    /// a multi-boss scene's other rigs hearing this one's death.
    ///
    /// Unparented and FOLLOWS the boss each LateUpdate (yaw only), same reasoning as
    /// <see cref="BigBermudaRig.Follow"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SludgequeenRig : MonoBehaviour
    {
        /// <summary>Give <paramref name="boss"/> its own rig, bound to it alone — same per-instance
        /// reasoning as <see cref="BigBermudaRig.CreateFor"/> (MV-573): a multi-boss arena must not have
        /// only its first boss grow a body.</summary>
        public static SludgequeenRig CreateFor(SludgequeenBoss boss)
        {
            if (boss == null) return null;
            var rig = new GameObject("SludgequeenRig").AddComponent<SludgequeenRig>();
            rig.Bind(boss);
            return rig;
        }

        // ---------------------------------------------------------------- dimensions (MV-696's own numbers)

        private const float DrumWidth = 6f;
        private const float DrumRadius = DrumWidth * 0.5f;
        private const float DrumHeight = 4f;
        private const float HatchHalf = 0.7f;
        private const float WheelRadius = 1.25f;   // 2.5 m across, the ticket's own number
        private const float WheelY = DrumHeight + 0.25f;

        /// <summary>Degrees/second the valve wheel spins while merely engaged, vs. flat-out while a
        /// brood wave is actually venting — the spin-up IS the attack tell.</summary>
        private const float IdleSpinSpeed = 90f;
        private const float VentingSpinSpeed = 720f;

        // ---------------------------------------------------------------- palette

        private static readonly Color HullColor = new Color(0.30f, 0.34f, 0.20f);     // corroded olive metal
        private static readonly Color RustColor = new Color(0.42f, 0.28f, 0.15f);     // rivets, hatch trim, wheel rim

        /// <summary>Same green <see cref="MaxWorlds.Enemies.CorrosiveGlob"/>'s globs and puddles wear —
        /// the sludge drips read as the same substance the boss lobs at you.</summary>
        private static readonly Color DripColor = new Color(0.24f, 0.55f, 0.18f);

        /// <summary>A NEW hue: "the first hint of the Curator's light" (the ticket's own words). Kept
        /// dim and picked away from the game's other violets — the bruiser's skin, Blinker's
        /// phase-hue, the teleport flash — so it never collides with an established meaning.</summary>
        private static readonly Color CuratorViolet = new Color(0.40f, 0.20f, 0.55f);
        private const float CuratorEyeIntensity = 0.35f;   // "faint" — the ticket's own word

        private static readonly Color AlarmColor = new Color(0.95f, 0.10f, 0.05f);

        private static readonly Color FloodColor = new Color(0.30f, 0.42f, 0.16f);

        /// <summary>Same number <c>MapRuntime.SludgeScrollSpeed</c> already authors for ground sludge —
        /// one flow speed for "sludge" everywhere in the game, not a bespoke one for this boss alone.</summary>
        private static readonly Vector2 FloodScrollSpeed = new Vector2(0f, 0.12f);

        private const float FloodThickness = 0.06f;
        private const float FoamHeight = 0.16f;
        private const float FoamDepth = 0.4f;

        // ---------------------------------------------------------------- materials (static, shared —
        // same reasoning as FactoryBodies: nothing here needs a per-instance emissive tween)

        private static Material s_hull;
        private static Material s_rust;

        private static void EnsureMaterials()
        {
            if (s_hull != null) return;
            s_hull = NewMaterial("Sludgequeen_Hull", HullColor);
            s_rust = NewMaterial("Sludgequeen_Rust", RustColor);
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

        private SludgequeenBoss _boss;
        private Transform _wheel;
        private float _wheelSpinDeg;
        private float _ventGlow;

        private MeshRenderer _eye;
        private readonly MeshRenderer[] _hatchGlows = new MeshRenderer[2];
        private readonly MeshRenderer[] _drips = new MeshRenderer[2];
        private MeshRenderer _alarmLens;
        private MaterialPropertyBlock _mpb;

        // The flood plane covers the ARENA (world X/Z), not the boss, so it is never parented under
        // this rig's own following transform — see RefreshFlood.
        private Transform _floodPlane;
        private Transform _foamEdge;

        private bool _dying;
        private float _dieTimer;

        /// <summary>The flood's own scrolling ground plane — a resolved value a test can read, same
        /// "public getter off private state" shape <see cref="BigBermudaRig.EyeColor"/> already uses.</summary>
        public SludgeFlow Flood { get; private set; }

        private void OnEnable() => HudSignals.BossDefeated += OnDefeated;
        private void OnDisable() => HudSignals.BossDefeated -= OnDefeated;

        private void Bind(SludgequeenBoss boss)
        {
            _boss = boss;
            gameObject.AddComponent<KeepsOwnMaterial>();
            _mpb = new MaterialPropertyBlock();

            // The greybox cube goes; its collider stays (the CharacterController is what Max and the
            // Water Blaster actually hit) — same split BigBermudaRig.Bind uses.
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            Build();
            BuildFlood();
            Follow();
            RefreshFlood();

            _boss.FitColliderTo(RenderedBoundsRelativeTo(_boss.transform));
        }

        private void Build()
        {
            EnsureMaterials();

            // The corroded pump drum — a 12-sided prism reads as a round drum at gameplay distance
            // while staying a generated, chamfered form rather than a bare cylinder primitive.
            CharacterPart.Add(transform, CharacterMeshes.Prism(12, DrumRadius, DrumRadius, DrumHeight, 0.08f),
                s_hull, new Vector3(0f, DrumHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "Drum");

            // A rusted base ring and roof ring — the "corroded" the ticket asks for, same hard-band
            // idiom FactoryBodies' hazard course uses rather than a texture that won't survive being a
            // few pixels tall at gameplay distance.
            CharacterPart.Add(transform, CharacterMeshes.Prism(12, DrumRadius * 1.03f, DrumRadius * 1.03f, 0.3f, 0.3f),
                s_rust, new Vector3(0f, 0.15f, 0f), Quaternion.identity, Vector3.one, "RustBase");
            CharacterPart.Add(transform, CharacterMeshes.Prism(12, DrumRadius * 1.03f, DrumRadius * 1.03f, 0.3f, 0.3f),
                s_rust, new Vector3(0f, DrumHeight - 0.15f, 0f), Quaternion.identity, Vector3.one, "RustCap");

            BuildHatch("HatchL", -1f, 0);
            BuildHatch("HatchR", 1f, 1);
            BuildValveWheelCrown();
            BuildEye();
        }

        /// <summary>One side intake hatch — a chamfered boss on the drum's flank, its glow lens
        /// carrying the brood tell (<see cref="SludgequeenBoss.IsVenting"/>), plus the sludge-drip
        /// emissive hanging under it.</summary>
        private void BuildHatch(string name, float side, int index)
        {
            Vector3 at = new Vector3(side * DrumRadius, DrumHeight * 0.5f, 0f);

            CharacterPart.Add(transform, CharacterMeshes.Prism(8, HatchHalf, HatchHalf, HatchHalf * 1.4f, 0.16f),
                s_rust, at, Quaternion.identity, Vector3.one, name);

            _hatchGlows[index] = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(10),
                at + new Vector3(side * (HatchHalf * 0.55f), 0f, 0f), Quaternion.identity,
                Vector3.one * (HatchHalf * 0.5f));
            _hatchGlows[index].gameObject.name = name + "Glow";
            ApplyLensColor(_hatchGlows[index], RustColor * 0.25f);

            // The sludge drip — a short bead of the same green the boss's own globs/puddles wear,
            // hung just under the hatch.
            _drips[index] = CharacterPart.AddLens(transform, CharacterMeshes.Beam(0.4f, 0.05f, 0.01f, 6),
                at + Vector3.down * (HatchHalf * 1.1f), Quaternion.identity, Vector3.one);
            _drips[index].gameObject.name = name + "Drip";
            ApplyLensColor(_drips[index], DripColor * 0.8f);
        }

        /// <summary>
        /// The valve-wheel crown — Sludgequeen's own scale-up of the same motif
        /// <see cref="FactoryBodies.BuildValveWheel"/> put on the Replicator's roof. That authored
        /// profile is NOT a naive scale of it, though: <c>CharacterMeshes.Lathe</c> always caps a
        /// profile end solid whenever its radius isn't ~0, so blowing the small motif's 0.22 m rim up
        /// to this crown's 2.5 m span turned its two end-caps into a huge flat disc that swallowed the
        /// hub and spokes into one plain dome — and a smooth dome spinning about its own axis shows NO
        /// visible motion at all, which would have silently broken the "the spin-up IS the attack
        /// tell" this crown exists for.
        ///
        /// So the crown is built the other way round: a squat disc (the housing) with four spoke RIBS
        /// and a hub sitting PROUD on top of it, in the hull's own colour against the disc's rust —
        /// raised height AND colour contrast both read the spokes from the fixed top-down camera,
        /// where a true open spoked wheel (unbuildable from these always-capped primitives) would
        /// mostly hide its spokes under its own rim anyway.
        /// </summary>
        private void BuildValveWheelCrown()
        {
            var pivotGo = new GameObject("ValveWheelCrown");
            pivotGo.transform.SetParent(transform, worldPositionStays: false);
            pivotGo.transform.localPosition = new Vector3(0f, WheelY, 0f);
            _wheel = pivotGo.transform;

            const float discThickness = 0.22f;
            CharacterPart.Add(_wheel, CharacterMeshes.Prism(20, WheelRadius, WheelRadius, discThickness, 0.22f),
                s_rust, Vector3.zero, Quaternion.identity, Vector3.one, "ValveDisc");

            // Raised ribs, proud of the disc's own top face — the height (not just the colour) is
            // what keeps them visible rather than flush with, or swallowed by, the disc beneath them.
            float ribY = discThickness * 0.5f + 0.02f;
            for (int i = 0; i < 4; i++)
            {
                float thetaDeg = i * 90f;
                CharacterPart.Add(_wheel, CharacterMeshes.Beam(WheelRadius * 1.7f, 0.09f, 0.05f, 6), s_hull,
                    Vector3.up * ribY, Quaternion.Euler(90f, thetaDeg, 0f), Vector3.one, "ValveSpoke");
            }

            Vector3 hubAt = Vector3.up * (ribY + 0.05f);
            CharacterPart.Add(_wheel, CharacterMeshes.Sphere(12), s_hull, hubAt, Quaternion.identity,
                Vector3.one * (WheelRadius * 0.22f), "ValveHub");

            // The phase-2 alarm beacon — dark until SludgequeenBoss.IsPhaseTwo, then it flashes.
            _alarmLens = CharacterPart.AddLens(_wheel, CharacterMeshes.Sphere(10),
                hubAt + Vector3.up * (WheelRadius * 0.16f), Quaternion.identity, Vector3.one * (WheelRadius * 0.14f));
            _alarmLens.gameObject.name = "PhaseTwoAlarm";
            ApplyLensColor(_alarmLens, Color.black);
        }

        /// <summary>The single violet orb — "the first hint of the Curator's light", faint. Bulging
        /// up off the drum's own front-top edge rather than sitting on the vertical flank: the same
        /// lesson <see cref="BigBermudaRig"/>'s own ocular core is built on — "a lens on a vertical
        /// face is seen edge-on and vanishes" under the fixed top-down camera, so the one thing meant
        /// to read as a quiet hint of something watching has to sit where that camera can actually see
        /// it.</summary>
        private void BuildEye()
        {
            _eye = CharacterPart.AddLens(transform, CharacterMeshes.Sphere(14),
                new Vector3(0f, DrumHeight * 0.94f, DrumRadius * 0.62f), Quaternion.identity, Vector3.one * 0.4f);
            _eye.gameObject.name = "CuratorEye";
            ApplyLensColor(_eye, CuratorViolet * CuratorEyeIntensity);
        }

        /// <summary>The flood's own rising sludge plane and its foam leading edge — an ENVIRONMENT
        /// overlay covering <see cref="SludgequeenBoss.FloodRect"/>'s world extent, built the same
        /// primitive-cube-plus-<see cref="SludgeFlow"/> way <c>MapRuntime.BuildSludge</c> already
        /// builds ground sludge (the "not Unity primitives" rule is about the CHARACTER, not the
        /// ground it stands on). Deliberately unparented: FloodRect is a world-space rect, and this
        /// rig's own transform follows the boss (see Follow) — parenting the flood under it would
        /// drag the arena-sized plane around by the boss's own position.
        /// </summary>
        private void BuildFlood()
        {
            var floodGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floodGo.name = "SludgequeenFlood";
            Strip(floodGo);
            floodGo.transform.SetParent(null);
            _floodPlane = floodGo.transform;

            Material floodMat = MaterialLibrary.Tinted(SurfaceKind.Prop, FloodColor);
            var floodRenderer = floodGo.GetComponent<MeshRenderer>();
            if (floodMat != null) floodRenderer.sharedMaterial = floodMat;
            floodRenderer.shadowCastingMode = ShadowCastingMode.Off;
            floodRenderer.receiveShadows = false;

            Flood = floodGo.AddComponent<SludgeFlow>();
            Flood.Configure(floodMat, FloodScrollSpeed);

            var foamGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foamGo.name = "SludgequeenFloodFoam";
            Strip(foamGo);
            foamGo.transform.SetParent(null);
            _foamEdge = foamGo.transform;

            var foamRenderer = foamGo.GetComponent<MeshRenderer>();
            foamRenderer.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            foamRenderer.shadowCastingMode = ShadowCastingMode.Off;
            foamRenderer.receiveShadows = false;
            ApplyLensColor(foamRenderer, Color.white * 0.8f);
        }

        /// <summary>Resizes the flood plane and its foam edge to <see cref="SludgequeenBoss.FloodRect"/>'s
        /// current extent, and shows them only while the fight is actually on
        /// (<see cref="SludgequeenBoss.Engaged"/>) — the flood only ever damages during that same
        /// window (<c>SludgequeenBoss.TickFight</c>).</summary>
        private void RefreshFlood()
        {
            if (_boss == null || _floodPlane == null) return;

            Rect r = _boss.FloodRect;
            float w = Mathf.Max(0.01f, r.width);
            float d = Mathf.Max(0.01f, r.height);

            _floodPlane.position = new Vector3(r.center.x, FloodThickness * 0.5f, r.center.y);
            _floodPlane.localScale = new Vector3(w, FloodThickness, d);

            // The leading (northmost) edge of the flood — the boundary a rising line actually reads
            // from, brightest right where dry ground turns to sludge.
            _foamEdge.position = new Vector3(r.center.x, FloodThickness + FoamHeight * 0.5f, r.yMax);
            _foamEdge.localScale = new Vector3(w, FoamHeight, FoamDepth);

            bool visible = _boss.Engaged && !_dying;
            if (_floodPlane.gameObject.activeSelf != visible) _floodPlane.gameObject.SetActive(visible);
            if (_foamEdge.gameObject.activeSelf != visible) _foamEdge.gameObject.SetActive(visible);
        }

        /// <summary>DestroyImmediate, not Destroy: this rig is built inside EditMode tests too, not
        /// only at runtime, and Destroy() logs an error there — same reasoning as
        /// <see cref="BigBermudaRig.Strip"/>.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
        }

        /// <summary>Same axis-aligned-in-another-frame rebuild as <see cref="BigBermudaRig.RenderedBoundsRelativeTo"/>
        /// (MV-613) — what <see cref="SludgequeenBoss.FitColliderTo"/> needs.</summary>
        private Bounds RenderedBoundsRelativeTo(Transform reference)
        {
            var renderers = GetComponentsInChildren<MeshRenderer>();
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
            Follow();

            if (_dying) { TickDeath(); return; }

            TickWheel();
            TickHatchGlow();
            TickAlarm();
            RefreshFlood();
        }

        private void Follow()
        {
            Vector3 p = _boss.transform.position;
            var at = new Vector3(p.x, 0f, p.z);
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }

        private void TickWheel()
        {
            if (!_boss.Engaged || _wheel == null) return;
            float spinSpeed = _boss.IsVenting ? VentingSpinSpeed : IdleSpinSpeed;
            _wheelSpinDeg += spinSpeed * Time.deltaTime;
            _wheel.localRotation = Quaternion.Euler(0f, _wheelSpinDeg, 0f);
        }

        /// <summary>The brood tell (MV-696 §2's hatch-open window): the two intake hatches glow hot
        /// green while <see cref="SludgequeenBoss.IsVenting"/>, dim rust otherwise — eased, same
        /// "Lerp toward a target at a fixed response rate" idiom every other rig's tell uses.</summary>
        private void TickHatchGlow()
        {
            float target = _boss.IsVenting ? 1f : 0f;
            _ventGlow = Mathf.Lerp(_ventGlow, target, 1f - Mathf.Exp(-8f * Time.deltaTime));
            Color c = Color.Lerp(RustColor * 0.25f, DripColor * 1.6f, _ventGlow);
            for (int i = 0; i < _hatchGlows.Length; i++) ApplyLensColor(_hatchGlows[i], c);
        }

        /// <summary>The phase-2 alarm: dark until <see cref="SludgequeenBoss.IsPhaseTwo"/>, then it
        /// flashes for the rest of the fight — "it got worse" reads at a glance, same language
        /// <see cref="BigBermudaRig"/>'s own enraged-red eye uses.</summary>
        private void TickAlarm()
        {
            if (_alarmLens == null) return;
            float pulse = _boss.IsPhaseTwo ? 0.5f + 0.5f * Mathf.Sin(Time.time * 9f) : 0f;
            ApplyLensColor(_alarmLens, AlarmColor * pulse);
        }

        /// <summary>
        /// The drain-away on death (MV-696's own "the 3 s drain-out itself is a rig-only visual, left
        /// to the art pass"). UNSCALED, same reason as <see cref="BigBermudaRig.TickDeath"/>: the run
        /// ends the frame the boss dies and ResultScreen sets timeScale = 0 that same frame.
        /// </summary>
        private void TickDeath()
        {
            const float duration = 3f;   // the ticket's own "3 s drain-out"

            _dieTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_dieTimer / duration);

            // The flood drains away with her — the visual half of the boss's own OnDeath, which
            // already drops her from FloodSpeedMultiplierAt/TickFloodDamage the instant she dies.
            float shrink = 1f - t;
            SetFloodShrink(shrink, shrink > 0.01f);

            ApplyLensColor(_eye, Color.Lerp(CuratorViolet * CuratorEyeIntensity, Color.black, t));
            for (int i = 0; i < _hatchGlows.Length; i++) ApplyLensColor(_hatchGlows[i], Color.black);
            for (int i = 0; i < _drips.Length; i++) ApplyLensColor(_drips[i], Color.Lerp(DripColor * 0.8f, Color.black, t));
            if (_alarmLens != null) ApplyLensColor(_alarmLens, Color.black);

            transform.position += Vector3.down * (t * t * 1.2f);   // sinks into the drained floor

            if (t >= 1f) gameObject.SetActive(false);
        }

        private void SetFloodShrink(float shrink, bool visible)
        {
            if (_floodPlane == null) return;
            Rect r = _boss != null ? _boss.FloodRect : new Rect();
            float w = Mathf.Max(0.01f, r.width) * Mathf.Max(0f, shrink);
            float d = Mathf.Max(0.01f, r.height) * Mathf.Max(0f, shrink);
            _floodPlane.localScale = new Vector3(w, FloodThickness, d);
            _floodPlane.gameObject.SetActive(visible);
            if (_foamEdge != null) _foamEdge.gameObject.SetActive(false);
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
            // Same MV-625 guard BigBermudaRig.OnDefeated uses: HudSignals.BossDefeated carries no
            // identity, and every rig built so far hears it — without this check, one boss dying would
            // drain and hide every OTHER still-alive boss's rig in a multi-boss scene.
            if (_boss == null || !_boss.IsDead) return;
            if (_dying) return;
            _dying = true;
            _dieTimer = 0f;
        }

        private void OnDestroy()
        {
            // The flood plane/foam are unparented (see BuildFlood), so destroying this rig does not
            // take them with it automatically — clean them up explicitly, same DestroyImmediate
            // reasoning as Strip (this runs inside EditMode tests too).
            if (_floodPlane != null) DestroyImmediate(_floodPlane.gameObject);
            if (_foamEdge != null) DestroyImmediate(_foamEdge.gameObject);
        }
    }
}
