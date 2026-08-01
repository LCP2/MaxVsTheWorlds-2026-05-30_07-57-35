using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Pickups;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Dresses the walk-over pickups with their real art (YT-134), and drives the shared collectible
    /// glow every pickup wears on the ground (YT-145).
    ///
    /// YT-131/133 drop the pickups as greybox stand-ins — a cyan sphere for a power cell and, for a
    /// PART, a single cube that is the SAME for all five. YT-134/145 swapped each greybox for a
    /// distinct <see cref="WeaponPartArt"/> prop; YT-180 reversed that for four of the five parts so
    /// they stayed boxes. WV-237 retires that box direction now that a part is a purely cosmetic
    /// universal token (WV-228): every dropped part wears one of ~10 machine-internals designs
    /// (<see cref="WeaponPartArt.MachineInternalsKeys"/>), picked at random each time it's placed, so
    /// repeated drops read as varied rather than a carpet of identical boxes. The power cell keeps its
    /// own always-the-same swapped prop.
    ///
    /// A director, not an edit to <c>Pickup</c>, for the same reason the boss and the robots are dressed
    /// by directors (BigBermudaRig, RobotRigDirector): the pickup's greybox is pure cosmetic — no
    /// active-tap indicator or collider to preserve — so the art stream can replace it without reaching
    /// into gameplay. The cell's pickup is POOLED and reused as-is (its kind never changes), so the
    /// once-built check below is all that's needed to keep it from rebuilding every frame. A pooled
    /// PART pickup, though, gets reused for a fresh drop with a fresh random design each time (WV-237)
    /// — <see cref="_partWasActive"/>/<see cref="_partArtKey"/> track that reroll across the
    /// deactivate/reactivate cycle <c>PickupDirector</c> pools it through.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PickupArtDirector : MonoBehaviour
    {
        private const string ArtPrefix = "PartArt:";   // child name carries the key it was built for
        private const float SpinDegreesPerSecond = 90f;

        // WV-237: per-pickup bookkeeping so a pooled Part pickup rerolls its machine-internals design
        // on every fresh drop rather than wearing whatever it first got forever. Keyed by reference —
        // pooled Pickups are reused, never destroyed, so entries live for the pickup's whole lifetime.
        private readonly Dictionary<Pickup, bool> _partWasActive = new Dictionary<Pickup, bool>();
        private readonly Dictionary<Pickup, string> _partArtKey = new Dictionary<Pickup, string>();

        // The collectible glow (YT-145): a soft additive bloom aura on every dropped pickup + power cell,
        // with a subtle pulse, so they read as "grab me" from across the yard. One shared colour for the
        // whole pickup language — the HUD part-ready icon (YT-147) is told to match it.
        //
        // ORANGE, not green: the lawn is green (BiomePalette turf/grass), so a green glow on it has almost
        // no hue contrast — and readability is the craft bible's first tie-breaker. Orange is the
        // complement of the grass, so it pops hardest; the pulse keeps it distinct from the game's STATIC
        // hazard-orange (factory/telegraph), and it stays clear of the forbidden yellow/brown. The aura
        // rides the floating pickup (it is NOT a ground ring) so it never reads as a danger telegraph.
        private const string GlowName = "CollectibleGlow";
        /// <summary>The collectible language colour: the on-ground pickup aura, and the shared source
        /// the HUD part-ready chip reads so the tell matches the pickup it points at (YT-147). Retune
        /// this one value and both the ground glow and the HUD chip move together — they can't drift.</summary>
        public static readonly Color CollectibleGlow = new Color(1f, 0.52f, 0.12f);
        private static readonly Color GlowColor = CollectibleGlow;
        private const float GlowBaseScale = 0.72f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<PickupArtDirector>() != null) return;
            // Gate on the real actor. A frame-working AfterSceneLoad director that installs into every
            // shared PlayMode test scene flakes timing-sensitive tests (YT-129/130); only the game runs
            // a PickupDirector, so its absence means there is nothing here for us to dress.
            if (FindFirstObjectByType<PickupDirector>() == null) return;
            new GameObject("PickupArt").AddComponent<PickupArtDirector>();
        }

        private void Update()
        {
            foreach (var pickup in FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // The power cell always wears the same swapped-in prop. Every PART now wears one of
                // the machine-internals designs too (WV-237) — which one is rerolled per drop below.
                string want = null;
                if (pickup.Kind == PickupKind.PowerCell)
                {
                    want = ArtPrefix + WeaponPartArt.Keys.PowerCell;
                }
                else if (pickup.Kind == PickupKind.Part)
                {
                    want = ArtPrefix + RollPartArtKey(pickup);
                }

                if (want != null)
                {
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
                        // WeaponPartArt built onto this prop — the cell wears four, the Hydro device three;
                        // a missing index is a harmless no-op (PulseGlisten below bails if it can't find the
                        // child), so one loop drives both without the director needing to know the count.
                        // Combined with the spin above, this is what sells "shiny" over the plain aura
                        // below — a highlight that visibly travels the surface and catches the light, not
                        // just a halo sitting around the whole prop.
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "0", 0f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "1", 1.7f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "2", 3.1f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "3", 4.6f);
                    }
                }

                PulseGlow(EnsureGlow(pickup.transform));
            }
        }

        /// <summary>The pickup's collectible aura, built once and reused. A sibling of the art (not a
        /// PartArt: child), so it survives a pooled cell rebuilding its prop instead of getting torn
        /// down with it.</summary>
        private static Transform EnsureGlow(Transform pickup)
        {
            var existing = pickup.Find(GlowName);
            if (existing != null) return existing;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = GlowName;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);                 // the Pickup's own trigger owns walk-over
            go.transform.SetParent(pickup, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;     // centred on the prop mass; rides the float/bob
            go.transform.localScale = Vector3.one * GlowBaseScale;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>Breathe the aura — a gentle scale + brightness pulse so it reads as an active beacon,
        /// not a painted-on disc. Unscaled time so it keeps pulsing while a part sits on the ground under
        /// the paused upgrade screen, matching the art's spin.</summary>
        private static void PulseGlow(Transform glow)
        {
            if (glow == null) return;

            float t = Mathf.Sin(Time.unscaledTime * 3.4f) * 0.5f + 0.5f;   // 0..1
            glow.localScale = Vector3.one * (GlowBaseScale * (0.9f + 0.16f * t));

            if (glow.TryGetComponent<MeshRenderer>(out var r))
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, GlowColor * (0.6f + 0.4f * t));   // additive: dimmer..full
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>Flickers one of a prop's specular glint dots (YT-167, WV-236) in a brief spike-and-fade,
        /// not the aura's slow breathing sine — a sparkle is light catching a facet for an instant, not a
        /// beacon glowing steadily. <paramref name="phase"/> offsets each dot's cycle so, together with the
        /// prop's own spin, its glints twinkle independently rather than flashing in lockstep.</summary>
        private static void PulseGlisten(Transform art, string childName, float phase)
        {
            var glisten = art.Find(childName);
            if (glisten == null || !glisten.TryGetComponent<MeshRenderer>(out var r)) return;

            // Raising a sine to a high power narrows it from a smooth wave into a brief spike separated
            // by dark gaps — the shape of a glint, not a lamp. Additive, so the peak is pushed past 1 for
            // a genuinely hot flash rather than just a brighter version of the resting dot.
            float wave = Mathf.Sin(Time.unscaledTime * 2.1f + phase) * 0.5f + 0.5f;
            float spike = Mathf.Pow(wave, 10f);
            float brightness = 0.15f + 2.4f * spike;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, WeaponPartArt.GlistenColor * brightness);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>The machine-internals key this PART pickup should wear right now (WV-237). Rerolled
        /// only on a fresh drop — detected as an inactive→active transition, the same edge
        /// <c>PickupDirector</c> crosses when it pops a pooled pickup and calls <c>Place</c> — so the
        /// design a player sees stays put while it sits on the ground and only varies drop to drop.</summary>
        private string RollPartArtKey(Pickup pickup)
        {
            bool activeNow = pickup.gameObject.activeSelf;
            bool wasActive = _partWasActive.TryGetValue(pickup, out bool prev) && prev;
            _partWasActive[pickup] = activeNow;

            if (!_partArtKey.TryGetValue(pickup, out string key) || (activeNow && !wasActive))
            {
                key = WeaponPartArt.MachineInternalsKeys[Random.Range(0, WeaponPartArt.MachineInternalsKeys.Length)];
                _partArtKey[pickup] = key;
                DestroyStaleArt(pickup.transform, ArtPrefix + key);
            }
            return key;
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
