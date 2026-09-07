using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// A walk-over collectible dropped by a robot (YT-131) — a power cell or a part. Greybox stand-in:
    /// a small hovering, spinning shape (a sphere for a cell, a cube for a part) so it reads on the
    /// lawn at the ~72° camera. It carries no collection logic itself; the <see cref="PickupDirector"/>
    /// pools it and does the walk-over check, so there is one Max lookup and one pool, not one per drop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Pickup : MonoBehaviour
    {
        private static readonly Color CellColor = new Color(0.31f, 0.86f, 0.98f); // cyan power cell

        // One shared, non-brown colour for every part box (YT-180): the old gold (0.98, 0.72, 0.22) is
        // exactly the warm mid-value WeaponPartArt's Chrome fix (YT-146) already found reads as a muddy
        // brown once the sunlit-albedo ceiling scales it down in shade — a near-neutral chrome can't.
        private static readonly Color PartColor = new Color(0.80f, 0.83f, 0.88f); // chrome part

        /// <summary>MV-698: World 1's finale drop — a brighter, more saturated cyan than the ordinary
        /// power cell's, so the two never read as the same tier of pickup even sharing the same hue
        /// family.</summary>
        private static readonly Color WeaponCoreColor = new Color(0.35f, 0.95f, 1f);

        /// <summary>MV-698: the Weapon Core's own greybox is bigger than an ordinary part (0.6 m
        /// diameter) and carries a 2 m ground glow — a rare, one-per-run drop reading unmistakably
        /// different from the routine cell/part/device trio.</summary>
        private const float WeaponCoreDiameter = 0.6f;
        private const float WeaponCoreRingRadius = 1f;   // 2 m across

        /// <summary>How high the collectible hovers over the ground.</summary>
        private const float FloatHeight = 0.6f;

        public PickupKind Kind { get; private set; }

        /// <summary>For a part pickup (YT-133), which of the five it is — set by the director from the
        /// unique drop table when it's placed. Meaningless for a power cell.</summary>
        public PartKind Part { get; set; }

        /// <summary>For a device pickup (WV-229), the ability it grants on collection — set by the
        /// director from <see cref="MaxWorlds.Weapons.WeaponSystemState.Unacquired"/> when it's placed.
        /// Meaningless for any other kind.</summary>
        public AbilityKind Ability { get; set; }

        private Transform _spin;
        private float _baseY = FloatHeight;
        private GroundRing _glowRing;   // MV-698: only the Weapon Core carries one of its own

        // MV-527: PickupArtDirector used to find every currently-placed pickup with a per-frame
        // FindObjectsByType<Pickup>(Include) scan — same idiom RobotEnemy._active already replaces for
        // enemies. A pickup only needs animating (spin/glisten/ring pulse) while it's actually placed on
        // the ground, so this registry IS exactly the set PickupArtDirector's Update should walk.
        private static readonly List<Pickup> _active = new List<Pickup>(16);

        /// <summary>Every currently-PLACED pickup — walked by <c>PickupArtDirector</c> instead of a
        /// scene-wide scan (MV-527).</summary>
        public static IReadOnlyList<Pickup> Active => _active;

        /// <summary>Fires exactly once per placement — a fresh drop or a pooled reuse, both always via
        /// <see cref="Place"/> (see below for why it is raised there and not from <see cref="OnEnable"/>).
        /// <c>PickupArtDirector</c> listens for this instead of polling an active/inactive transition
        /// itself every frame (MV-527) — same null-safe static-event idiom as
        /// <c>DropSignals</c>/<c>HudSignals</c>.</summary>
        public static event System.Action<Pickup> Registered;

        /// <summary>Test isolation only (mirrors <c>RobotEnemy.ResetRegistry</c>) — a fixture that
        /// builds and destroys pickups without going through <see cref="OnDisable"/> would otherwise
        /// leak stale entries into the next test.</summary>
        public static void ResetRegistry() => _active.Clear();

        private void OnEnable() => _active.Add(this);

        private void OnDisable() => _active.Remove(this);

        /// <summary>Build a pooled pickup of the given kind (its visual never changes, so the director
        /// pools per kind and reuses it as-is).</summary>
        public static Pickup Create(PickupKind kind)
        {
            var go = new GameObject($"Pickup ({kind})");
            var p = go.AddComponent<Pickup>();
            p.Kind = kind;
            p.BuildVisual();
            return p;
        }

        private void BuildVisual()
        {
            // KeepsOwnMaterial on the root so the surface sweep leaves the tinted pickup alone (it is
            // not scenery). The child renderer gets a real URP material via MaterialLibrary — a raw
            // runtime primitive would draw magenta in a player build.
            gameObject.AddComponent<KeepsOwnMaterial>();

            if (Kind == PickupKind.WeaponCore)
            {
                BuildWeaponCoreVisual();
                return;
            }

            bool cell = Kind == PickupKind.PowerCell;
            var prim = GameObject.CreatePrimitive(cell ? PrimitiveType.Sphere : PrimitiveType.Cube);
            prim.name = "Visual";
            // walk-over is a distance check, not physics. Destroy() is illegal outside Play mode
            // (MV-439: EditMode tests now build a real Pickup via PickupDirector.SpawnDrop), so this
            // must switch to DestroyImmediate there — same idiom as everywhere else in the project
            // that builds objects reachable from both a live run and an EditMode test.
            var collider = prim.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
            prim.transform.SetParent(transform, worldPositionStays: false);
            prim.transform.localScale = Vector3.one * (cell ? 0.32f : 0.5f);
            prim.transform.localPosition = Vector3.zero;

            var mr = prim.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, cell ? CellColor : PartColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _spin = prim.transform;
        }

        /// <summary>MV-698: World 1's finale drop wears its own greybox rather than the ordinary
        /// sphere/cube pair — a larger cyan-cored sphere plus a 2 m ground glow ring
        /// (<see cref="WeaponCoreRingRadius"/>) it carries for itself (no other kind gets one from
        /// <c>Pickup</c> directly; <c>PickupArtDirector</c> dresses everyone else's). A native primitive,
        /// not a hand-authored mesh — this is a once-per-run drop with no screenshot harness watching
        /// it, so reliability beats a fancier silhouette.</summary>
        private void BuildWeaponCoreVisual()
        {
            var prim = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            prim.name = "Visual";
            var collider = prim.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
            prim.transform.SetParent(transform, worldPositionStays: false);
            prim.transform.localScale = Vector3.one * WeaponCoreDiameter;
            prim.transform.localPosition = Vector3.zero;

            var mr = prim.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponCoreColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _spin = prim.transform;

            _glowRing = GroundRing.Create("WeaponCoreGlow");
            _glowRing.transform.SetParent(transform, worldPositionStays: false);
        }

        /// <summary>Drop the pickup at a ground position and switch it on (the director calls this).
        /// <see cref="Registered"/> fires from here, not from <see cref="OnEnable"/> (MV-685): a fresh
        /// <see cref="Create"/> calls <c>AddComponent&lt;Pickup&gt;()</c> on a GameObject that starts
        /// active, and Unity fires a freshly-added component's <c>OnEnable</c> synchronously, inside that
        /// call — before <c>Create</c> has assigned <see cref="Kind"/> or built the "Visual" greybox
        /// child. Raising <see cref="Registered"/> from <c>OnEnable</c> back then meant
        /// <c>PickupArtDirector</c> saw a pickup with no kind and no greybox to hide yet, so the greybox
        /// was built moments later and never got hidden, and (for every kind but the enum's zero value,
        /// PowerCell) the wrong art got built too. <c>Place</c> is always called after both are ready —
        /// for a fresh drop, at the end of the same <c>Create</c>-then-<c>Place</c> call the director's
        /// own doc comment already promised; for a pooled reuse, <see cref="Kind"/> never changes and the
        /// art/greybox were already built the first time — so raising it here instead is unconditionally
        /// safe and fixes both without depending on Unity's OnEnable timing at all.</summary>
        public void Place(Vector3 groundPos)
        {
            transform.position = new Vector3(groundPos.x, _baseY, groundPos.z);
            gameObject.SetActive(true);
            // MV-698: the glow ring is a flat ground quad, not a child riding the pickup's own
            // transform (see GroundRing's own doc comment on why — it must not inherit the bob) — so it
            // needs its own explicit re-place on every placement, fresh drop or pooled reuse alike.
            if (_glowRing != null)
                _glowRing.Show(new Vector3(groundPos.x, 0f, groundPos.z), WeaponCoreRingRadius, WeaponCoreColor);
            Registered?.Invoke(this);
        }

        private void Update()
        {
            // A slow spin + gentle bob is the whole reason a small greybox reads as "pick me up" from
            // above rather than as a bit of dropped debris.
            if (_spin != null) _spin.Rotate(0f, 140f * Time.deltaTime, 0f, Space.Self);
            Vector3 pos = transform.position;
            pos.y = _baseY + Mathf.Sin(Time.unscaledTime * 3f) * 0.12f;
            transform.position = pos;
        }
    }
}
