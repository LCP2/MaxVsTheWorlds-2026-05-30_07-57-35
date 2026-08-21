using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Pickups;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Dresses the walk-over pickups with their real art (YT-134), and drives their ground ring
    /// (MV-429, formerly a floating aura — YT-145).
    ///
    /// YT-131/133 drop the pickups as greybox stand-ins — a cyan sphere for a power cell and, for a
    /// PART, a single cube. YT-134/145 swapped each greybox for a distinct <see cref="WeaponPartArt"/>
    /// prop; YT-180 reversed that for four of the five parts so they stayed boxes; WV-237 retired the
    /// box entirely in favour of ~10 randomised machine-internals designs. MV-180 then reverted WV-237
    /// back to the plain box for a playtest call that no longer holds: MV-305 reverses MV-180 again — a
    /// PART pickup wears one of <see cref="WeaponPartArt.MachineInternalsKeys"/>'s irregular designs
    /// (gear, coil, circuit block, ...), rerolled at random on each fresh drop, the same swap-in idiom
    /// the power cell always used. The power cell keeps its own always-the-same swapped prop.
    ///
    /// A director, not an edit to <c>Pickup</c>, for the same reason the boss and the robots are dressed
    /// by directors (BigBermudaRig, RobotRigDirector): the pickup's greybox is pure cosmetic — no
    /// active-tap indicator or collider to preserve — so the art stream can replace it without reaching
    /// into gameplay. The cell's pickup is POOLED and reused as-is (its kind never changes), so the
    /// once-built check below is all that's needed to keep it from rebuilding every frame. A pooled PART
    /// pickup, though, gets reused for a fresh drop with a fresh random design each time — <see
    /// cref="_partWasActive"/>/<see cref="_partArtKey"/> track that reroll across the deactivate/
    /// reactivate cycle <c>PickupDirector</c> pools it through.
    ///
    /// MV-308: a shed's ability grant (<see cref="PickupKind.Device"/>) had no branch here at all, so it
    /// fell through wearing its plain greybox cube forever. <see cref="WeaponPartArt.Keys.HydroDevice"/>
    /// was already built and documented as this exact ground pickup's "shimmers like a cell" look (WV-236)
    /// but never wired up after WV-229 generalised the shed drop from a single Hydro grant to any of the
    /// five <see cref="MaxWorlds.Weapons.AbilityKind"/> values — it's a single shared "ability device"
    /// look for every grant (the ticket's nice-to-have of one prop per ability needs a catalog entry per
    /// ability that doesn't exist yet), swapped in and radiated the same swap-in-once idiom as the cell.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PickupArtDirector : MonoBehaviour
    {
        private const string ArtPrefix = "PartArt:";   // child name carries the key it was built for
        private const float SpinDegreesPerSecond = 90f;

        // MV-305: per-pickup bookkeeping so a pooled Part pickup rerolls its machine-internals design on
        // every fresh drop rather than wearing whatever it first got forever. Keyed by reference — pooled
        // Pickups are reused, never destroyed, so entries live for the pickup's whole lifetime.
        //
        // MV-527: used to be repopulated by polling an active/inactive transition every frame for every
        // Pickup in the scene. Pickup.Registered fires exactly once per placement (fresh drop or pooled
        // reuse) — the same transition, as an event instead of a diff — so the reroll now happens once,
        // in OnPickupRegistered, and Update just reads what's already here.
        private readonly Dictionary<Pickup, string> _partArtKey = new Dictionary<Pickup, string>();

        // MV-527: one reusable block instead of `new MaterialPropertyBlock()` per glisten/core pulse per
        // pickup per frame — same idiom as RobotRig's _eyeMpb. Fine to share across every renderer this
        // director touches: each use is get-mutate-set, synchronous, single-threaded, and never holds a
        // reference across two different renderers at once. Built in Awake, not a field initializer —
        // MaterialPropertyBlock's constructor calls into native code Unity only allows from Awake/Start,
        // and a field initializer runs earlier than that, as part of the object's construction.
        private MaterialPropertyBlock _mpb;

        private void Awake() => _mpb = new MaterialPropertyBlock();

        /// <summary>The collectible language colour: shared with the HUD part-ready chip so the tell
        /// matches the pickup it points at (YT-147). MV-429 retired the on-ground aura this used to also
        /// paint (an additive sphere 2.3-2.7x wider than the prop it was meant to advertise, and centred
        /// on the pickup's hover point rather than its mass — see <see cref="DressGroundRing"/> for what
        /// replaced it) but the HUD still reads this constant, so it stays.</summary>
        public static readonly Color CollectibleGlow = new Color(1f, 0.52f, 0.12f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // --- the ground ring (MV-429) -------------------------------------------------------------
        //
        // Replaces the old CollectibleGlow aura: a flat GroundRing per pickup, tracking the pickup's XZ
        // but pinned to the ground plane (unlike the aura, it must NOT ride the float/bob — a ring that
        // bounced with the prop would read as levitating scenery, not a ground mark). Sits below the
        // danger telegraph (GroundRing.GroundLift = 0.03) and the always-on actor anchors
        // (GroundAnchorTuning.RingLift = 0.020) in the ground-mark stacking order, so a telegraph can
        // never be hidden by a pickup's own "grab me" tell.
        private const string RingName = "GroundRing";
        private const string RingOuterName = "GroundRingOuter";
        private const string RingInnerName = "GroundRingInner";
        private const float RingLift = 0.016f;

        // The "grab me" beat the old aura pulsed with (same Mathf.Sin cadence), now driving the ring's
        // alpha instead of the aura's scale/brightness — a breathing radius would read as a telegraph,
        // so only alpha moves.
        private const float RingPulseSpeed = 3.4f;
        private const float RingPulseMin = 0.55f;
        private const float RingPulseRange = 0.45f;

        private const float PowerCellRingRadius = 0.50f;
        private const float PartRingRadius = 0.46f;
        private const float DeviceRingOuterRadius = 0.68f;
        private const float DeviceRingInnerRadius = 0.44f;

        private const float PowerCellRingAlpha = 0.85f;
        private const float PartRingAlpha = 0.70f;
        private const float DeviceRingOuterAlpha = 0.90f;
        private const float DeviceRingInnerAlpha = 0.50f;

        // MV-429 wore this as its own literal ahead of MV-431's colour pass on the prop itself; now that
        // WeaponPartArt.ModuleGlow exists, read it back so the ring and the prop's core can never drift
        // apart onto two different reds.
        private static readonly Color DeviceRingColor = WeaponPartArt.ModuleGlow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<PickupArtDirector>() != null) return;
            if (FindFirstObjectByType<InstallGate>() != null) return;
            new GameObject("PickupArtInstallGate").AddComponent<InstallGate>();
        }

        /// <summary>
        /// MV-313: this used to gate on the real actor by calling <c>FindFirstObjectByType&lt;PickupDirector&gt;()</c>
        /// right here, inside this class's own AfterSceneLoad callback — but <c>PickupDirector</c>
        /// installs itself through the exact same idiom, its own [RuntimeInitializeOnLoadMethod(AfterSceneLoad)],
        /// and Unity does not guarantee which of two different classes' AfterSceneLoad callbacks runs
        /// first. In the live/WebGL (IL2CPP) build this class's callback was losing that race, so the
        /// PickupDirector check always saw nothing and PickupArtDirector never installed — every pickup
        /// wore its raw greybox forever. The isolated PlayMode tests never caught it because they build
        /// the director by hand (<c>InstallDirector</c>), skipping this gate entirely.
        ///
        /// <c>Start()</c> only runs once every AfterSceneLoad callback for this frame has already run —
        /// including <c>PickupDirector</c>'s own — so checking here instead is independent of dispatch
        /// order, native boot or <see cref="MaxWorlds.Core.SceneInstallers"/>'s own Replay re-install.
        /// A frame-working object that installs into every shared PlayMode test scene would flake
        /// timing-sensitive tests (YT-129/130) exactly as the old gate's comment warned, so on a miss it
        /// removes itself rather than sticking around to check again.
        /// </summary>
        private sealed class InstallGate : MonoBehaviour
        {
            private void Start()
            {
                if (FindFirstObjectByType<PickupDirector>() != null)
                {
                    gameObject.name = "PickupArt";
                    gameObject.AddComponent<PickupArtDirector>();
                }
                else if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
                    DestroyImmediate(gameObject);
                }
            }
        }

        private void OnEnable() => Pickup.Registered += OnPickupRegistered;
        private void OnDisable() => Pickup.Registered -= OnPickupRegistered;

        /// <summary>MV-527: the reroll that used to happen inline in <c>Update</c>'s Supercell branch,
        /// diffed every frame off an active/inactive dictionary — now driven by the placement event
        /// itself, once. A non-Supercell kind never rerolls (PowerCell/Device always wear the same fixed
        /// key), so this is a no-op for them.</summary>
        private void OnPickupRegistered(Pickup pickup)
        {
            if (pickup.Kind != PickupKind.Supercell) return;
            RollNewPartKey(pickup);
        }

        /// <summary>Picks a fresh machine-internals design, remembers it, and clears out whatever design
        /// this (possibly pooled) pickup wore last time.</summary>
        private string RollNewPartKey(Pickup pickup)
        {
            string key = WeaponPartArt.MachineInternalsKeys[Random.Range(0, WeaponPartArt.MachineInternalsKeys.Length)];
            _partArtKey[pickup] = key;
            DestroyStaleArt(pickup.transform, ArtPrefix + key);
            return key;
        }

        private void Update()
        {
            // MV-527: Pickup.Active — every currently-placed pickup, self-registered on enable/disable —
            // instead of a per-frame FindObjectsByType<Pickup>(Include) scan of the whole scene. A pooled,
            // not-yet-dropped pickup sits inactive and out of this list, so it also stops being spun,
            // pulsed and re-Find()'d for nothing every frame while it isn't even on the ground.
            foreach (var pickup in Pickup.Active)
            {
                // The power cell always wears the same swapped-in prop; a PART wears one of the
                // machine-internals designs, rerolled per drop in the branch below (MV-305).
                if (pickup.Kind == PickupKind.PowerCell)
                {
                    string want = ArtPrefix + WeaponPartArt.Keys.PowerCell;
                    Transform art = FindArt(pickup.transform, want);

                    if (art == null)
                    {
                        art = Build(pickup, want);
                        HideGreybox(pickup.transform);
                    }

                    // Spin it here rather than lean on the pickup's own spin: the pickup spins its greybox
                    // child, which we hid, so the art needs its own turn. Unscaled so it keeps turning while
                    // the upgrade screen has the game paused with a cell still on the ground.
                    if (art != null)
                    {
                        art.Rotate(0f, SpinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
                        // The GLISTEN/SHIMMER (YT-167, extended WV-236): flicker whichever specular dots
                        // WeaponPartArt built onto this prop — a missing index is a harmless no-op
                        // (PulseGlisten below bails if it can't find the child). Combined with the spin
                        // above, this is what sells "shiny" — a highlight that visibly travels the
                        // surface and catches the light, not just a halo around it.
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "0", 0f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "1", 1.7f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "2", 3.1f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "3", 4.6f);
                        // The gentle RADIATE (MV-304): the "Core" charge band WeaponPartArt built is
                        // otherwise a static light — breathing it slowly sells "energy source", not
                        // "lamp". Deliberately calmer and slower than the ground ring's pulse (the
                        // ticket's own "far gentler than a radiant star") so the two don't compete —
                        // the cell's own charge is a quiet pulse above the "grab me" ring below it.
                        PulseCellCore(art);
                    }
                }
                else if (pickup.Kind == PickupKind.Supercell)
                {
                    // MV-305: every dropped Supercell wears one of the machine-internals designs, rerolled
                    // per drop — MV-527: by OnPickupRegistered when it was placed, not here. Defensive
                    // fallback if this pickup was somehow never seen registering (should not happen on the
                    // live path — Pickup.Registered always fires before this frame's Update).
                    if (!_partArtKey.TryGetValue(pickup, out string key)) key = RollNewPartKey(pickup);
                    string want = ArtPrefix + key;
                    Transform art = FindArt(pickup.transform, want);

                    if (art == null)
                    {
                        art = Build(pickup, want);
                        HideGreybox(pickup.transform);
                    }

                    if (art != null)
                    {
                        art.Rotate(0f, SpinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
                        // Each machine-internals design carries one or two glint dots (WeaponPartArt);
                        // a missing index is a harmless no-op, so one fixed loop covers every design
                        // without the director needing to know which one this pickup rolled.
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "0", 0f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "1", 1.7f);
                    }
                }
                else if (pickup.Kind == PickupKind.Device)
                {
                    // MV-308: the shed's ability grant wears the same always-the-same swapped prop the
                    // power cell does (its kind, like the cell's, never changes once built).
                    string want = ArtPrefix + WeaponPartArt.Keys.HydroDevice;
                    Transform art = FindArt(pickup.transform, want);

                    if (art == null)
                    {
                        art = Build(pickup, want);
                        HideGreybox(pickup.transform);
                    }

                    if (art != null)
                    {
                        art.Rotate(0f, SpinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
                        // The prop's three built-in glisten dots (WeaponPartArt.BuildHydroDevice) —
                        // shimmer it the same "diamonds catching light" way the cell and parts get.
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "0", 0f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "1", 1.7f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "2", 3.1f);
                        // MV-308 AC: "the glowing radiance the power cells have" — the same gentle
                        // MV-304 Core breathe, not just the shared "grab me" ring below it.
                        PulseCellCore(art);
                    }
                }

                DressGroundRing(pickup.transform, pickup.Kind);
            }
        }

        /// <summary>Removes any previously-built PartArt: child that isn't <paramref name="keep"/> — the
        /// leftover from this pooled pickup's last drop, now that it's rerolled a different design.</summary>
        private static void DestroyStaleArt(Transform pickup, string keep)
        {
            for (int i = pickup.childCount - 1; i >= 0; i--)
            {
                var c = pickup.GetChild(i);
                if (c.name.StartsWith(ArtPrefix) && c.name != keep) Destroy(c.gameObject);
            }
        }

        /// <summary>Ensures and pulses this pickup's ground ring(s) — one for a cell or part, two
        /// (outer + inner) for a device. Built once per name and reused, the same idiom the old aura
        /// used. Unscaled time so it keeps pulsing while a pickup sits on the ground under the paused
        /// upgrade screen, matching the art's spin.</summary>
        private static void DressGroundRing(Transform pickup, PickupKind kind)
        {
            float t = Mathf.Sin(Time.unscaledTime * RingPulseSpeed) * 0.5f + 0.5f;   // 0..1
            float pulse = RingPulseMin + RingPulseRange * t;

            if (kind == PickupKind.Device)
            {
                ShowRing(pickup, RingOuterName, DeviceRingOuterRadius, DeviceRingColor, DeviceRingOuterAlpha * pulse);
                ShowRing(pickup, RingInnerName, DeviceRingInnerRadius, DeviceRingColor, DeviceRingInnerAlpha * pulse);
                return;
            }

            bool cell = kind == PickupKind.PowerCell;
            float radius = cell ? PowerCellRingRadius : PartRingRadius;
            Color color = cell ? WeaponPartArt.CellCyan : WeaponPartArt.Chrome;
            float alpha = cell ? PowerCellRingAlpha : PartRingAlpha;
            ShowRing(pickup, RingName, radius, color, alpha * pulse);
        }

        /// <summary>Builds (once) or reuses the named <see cref="GroundRing"/> child and places it at the
        /// pickup's XZ, pinned to the ground plane — <b>not</b> parented via local position, since
        /// <see cref="GroundRing.Show"/> writes an absolute world position every call, which is what
        /// keeps the ring from inheriting the pickup's float/bob.</summary>
        private static void ShowRing(Transform pickup, string name, float radius, Color color, float alpha)
        {
            var existing = pickup.Find(name);
            GroundRing ring = existing != null ? existing.GetComponent<GroundRing>() : null;
            if (ring == null)
            {
                ring = GroundRing.Create(name);
                ring.transform.SetParent(pickup, worldPositionStays: false);
            }
            ring.Lift = RingLift;

            Vector3 groundPos = pickup.position;
            groundPos.y = 0f;   // the lawn plane (GroundAnchorTuning) — the ring never reads the bob
            ring.Show(groundPos, radius, new Color(color.r, color.g, color.b, alpha));
        }

        // MV-304: the cell's own gentle radiance — slower and lower-amplitude than the ground ring's
        // pulse, so it reads as a quiet inner charge rather than competing with the "grab me" tell.
        private const string CellCoreName = "Core";
        private const float CellPulseSpeed = 1.1f;
        private const float CellPulseMin = 0.75f;
        private const float CellPulseRange = 0.5f;

        /// <summary>Breathes the power cell's "Core" charge band (built by <see cref="WeaponPartArt.BuildPowerCell"/>)
        /// between a dim and a bright cyan so it reads as radiating energy rather than a fixed light.
        /// A no-op for any prop without a "Core" child (the Hydro device's own core glow is untouched —
        /// it isn't reached from the PowerCell branch that calls this).</summary>
        private void PulseCellCore(Transform art)
        {
            var core = art.Find(CellCoreName);
            if (core == null || !core.TryGetComponent<MeshRenderer>(out var r)) return;

            float t = Mathf.Sin(Time.unscaledTime * CellPulseSpeed) * 0.5f + 0.5f;   // 0..1
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, WeaponPartArt.CellCyan * (CellPulseMin + CellPulseRange * t));
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Flickers one of a prop's specular glint dots (YT-167, WV-236) in a brief spike-and-fade,
        /// not the aura's slow breathing sine — a sparkle is light catching a facet for an instant, not a
        /// beacon glowing steadily. <paramref name="phase"/> offsets each dot's cycle so, together with the
        /// prop's own spin, its glints twinkle independently rather than flashing in lockstep.</summary>
        private void PulseGlisten(Transform art, string childName, float phase)
        {
            var glisten = art.Find(childName);
            if (glisten == null || !glisten.TryGetComponent<MeshRenderer>(out var r)) return;

            // Raising a sine to a high power narrows it from a smooth wave into a brief spike separated
            // by dark gaps — the shape of a glint, not a lamp. Additive, so the peak is pushed past 1 for
            // a genuinely hot flash rather than just a brighter version of the resting dot.
            float wave = Mathf.Sin(Time.unscaledTime * 2.1f + phase) * 0.5f + 0.5f;
            float spike = Mathf.Pow(wave, 10f);
            float brightness = 0.15f + 2.4f * spike;

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, WeaponPartArt.GlistenColor * brightness);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>The child wearing exactly <paramref name="wantName"/>, or null.</summary>
        private static Transform FindArt(Transform pickup, string wantName)
        {
            for (int i = 0; i < pickup.childCount; i++)
            {
                var c = pickup.GetChild(i);
                if (c.name == wantName) return c;
            }
            return null;
        }

        private static Transform Build(Pickup pickup, string wantName)
        {
            string key = wantName.Substring(ArtPrefix.Length);
            var art = WeaponPartArt.Build(key, pickup.transform);
            if (art == null) return null;
            art.name = wantName;
            // The props are authored base-at-zero and ~0.45 m tall; drop them so they hover centred on
            // the pickup point rather than floating above it.
            art.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            // The shed device reads "a chunk bigger" than the cell (WV-236) — scaled up on top of its
            // authored geometry rather than re-tuned part-by-part, same idiom as every other size tweak
            // in this catalog living as one named constant.
            if (key == WeaponPartArt.Keys.HydroDevice)
                art.transform.localScale = Vector3.one * WeaponPartArt.HydroDeviceGroundScale;
            else if (key == WeaponPartArt.Keys.PowerCell)
                art.transform.localScale = Vector3.one * WeaponPartArt.PowerCellGroundScale;
            else
                // MV-326: every other key reaching here is one of the machine-internals designs a Part
                // pickup wears — give it its own ground scale so it reads unmistakably larger than the
                // power cell, not just differently shaped/coloured.
                art.transform.localScale = Vector3.one * WeaponPartArt.PartGroundScale;
            return art.transform;
        }

        /// <summary>Turn off the greybox stand-in the pickup built, leaving its transform (the pickup
        /// bobs the whole object; only the drawn cube/sphere has to go).</summary>
        private static void HideGreybox(Transform pickup)
        {
            var visual = pickup.Find("Visual");
            if (visual != null && visual.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = false;
        }
    }
}
