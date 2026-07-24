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
    /// distinct <see cref="WeaponPartArt"/> prop; YT-180 reversed that for the five parts — Lee wants
    /// them to stay as boxes, just glowing and one consistent (non-brown) colour, per <c>Pickup</c>'s
    /// own <c>PartColor</c>. Only the power cell still gets its wired-in prop swap here.
    ///
    /// A director, not an edit to <c>Pickup</c>, for the same reason the boss and the robots are dressed
    /// by directors (BigBermudaRig, RobotRigDirector): the pickup's greybox is pure cosmetic — no
    /// active-tap indicator or collider to preserve — so the art stream can replace it without reaching
    /// into gameplay. The cell's pickup is POOLED and reused as-is (its kind never changes), so the
    /// once-built check below is all that's needed to keep it from rebuilding every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PickupArtDirector : MonoBehaviour
    {
        private const string ArtPrefix = "PartArt:";   // child name carries the key it was built for
        private const float SpinDegreesPerSecond = 90f;

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
                // Only the power cell wears a swapped-in prop. The five parts keep their own greybox
                // cube (YT-180) — Pickup already paints it the shared non-brown PartColor and spins/bobs
                // it itself, so there is nothing more to dress here beyond the glow below.
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
                        // The GLISTEN (YT-167): flicker the two specular dots WeaponPartArt built onto the
                        // casing. Combined with the spin above, this is what sells "shiny" over the plain
                        // aura below — a highlight that visibly travels the surface and catches the light,
                        // not just a halo sitting around the whole prop.
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "0", 0f);
                        PulseGlisten(art, WeaponPartArt.GlistenPrefix + "1", 1.7f);
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

        /// <summary>Flickers one of the power cell's specular glint dots (YT-167) in a brief spike-and-fade,
        /// not the aura's slow breathing sine — a sparkle is light catching a facet for an instant, not a
        /// beacon glowing steadily. <paramref name="phase"/> offsets each dot's cycle so, together with the
        /// prop's own spin, the two glints twinkle independently rather than flashing in lockstep.</summary>
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
