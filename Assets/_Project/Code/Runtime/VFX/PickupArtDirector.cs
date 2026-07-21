using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Dresses the walk-over pickups with their real art (YT-134).
    ///
    /// YT-131/133 drop the pickups as greybox stand-ins — a cyan sphere for a power cell and, for a
    /// PART, a single gold cube that is the SAME for all five. So on the lawn the beam nozzle and the
    /// Hydro device are indistinguishable, which is the one thing the five parts must never be. This
    /// swaps each greybox for the matching <see cref="WeaponPartArt"/> prop the moment it appears.
    ///
    /// A director, not an edit to <c>Pickup</c>, for the same reason the boss and the robots are dressed
    /// by directors (BigBermudaRig, RobotRigDirector): the pickup's greybox is pure cosmetic — no
    /// active-tap indicator or collider to preserve — so the art stream can replace it without reaching
    /// into gameplay. Pickups are POOLED and re-placed with a different part, so this re-checks every
    /// frame and rebuilds when a pooled cube comes back as a different component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PickupArtDirector : MonoBehaviour
    {
        private const string ArtPrefix = "PartArt:";   // child name carries the key it was built for
        private const float SpinDegreesPerSecond = 90f;

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
                string want = ArtPrefix + KeyFor(pickup);
                Transform art = FindArt(pickup.transform, want);

                if (art == null)
                {
                    // No child wears the right key. Clear any that wear the WRONG one (a pooled cube that
                    // came back as a different part) and build the right prop. Keying "build only when the
                    // right one is absent" is what keeps a deferred Destroy() — which lingers a frame —
                    // from making us stack a fresh prop every frame until the old one finally goes.
                    ClearWrongArt(pickup.transform, want);
                    art = Build(pickup, want);
                    HideGreybox(pickup.transform);
                }

                // Spin it here rather than lean on the pickup's own spin: the pickup spins its greybox
                // child, which we hid, so the art needs its own turn. Unscaled so it keeps turning while
                // the upgrade screen has the game paused with a part still on the ground.
                if (art != null) art.Rotate(0f, SpinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
            }
        }

        /// <summary>Which prop this pickup should wear. A power cell is a power cell; a part maps to one
        /// of the five by its <see cref="PartKind"/>.</summary>
        private static string KeyFor(Pickup pickup)
        {
            if (pickup.Kind == PickupKind.PowerCell) return WeaponPartArt.Keys.PowerCell;
            switch (pickup.Part)
            {
                case PartKind.BeamNozzle: return WeaponPartArt.Keys.BeamNozzle;
                case PartKind.PowerNozzle: return WeaponPartArt.Keys.PowerNozzle;
                case PartKind.AugmentationHarness: return WeaponPartArt.Keys.AugmentationHarness;
                case PartKind.AccelerationEngine: return WeaponPartArt.Keys.AccelerationEngine;
                case PartKind.Hydro: return WeaponPartArt.Keys.HydroDevice;
                default: return WeaponPartArt.Keys.PowerCell;
            }
        }

        /// <summary>The child wearing exactly <paramref name="wantName"/>, or null. Ignores any
        /// wrong-keyed child that is mid-Destroy from a part change.</summary>
        private static Transform FindArt(Transform pickup, string wantName)
        {
            for (int i = 0; i < pickup.childCount; i++)
            {
                var c = pickup.GetChild(i);
                if (c.name == wantName) return c;
            }
            return null;
        }

        /// <summary>Destroy any art child that is NOT the one we want — a pooled pickup that came back
        /// as a different part.</summary>
        private static void ClearWrongArt(Transform pickup, string wantName)
        {
            for (int i = pickup.childCount - 1; i >= 0; i--)
            {
                var c = pickup.GetChild(i);
                if (c.name.StartsWith(ArtPrefix) && c.name != wantName) Destroy(c.gameObject);
            }
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
