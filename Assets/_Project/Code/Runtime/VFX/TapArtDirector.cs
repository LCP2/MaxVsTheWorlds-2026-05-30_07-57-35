using UnityEngine;
using MaxWorlds.Hose;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Dresses the garden taps with their real art (YT-134). YT-129/130 stand each tap up as a
    /// greybox — a bare post-and-spout stub. WV-233 detached the hose from taps entirely (Max carries
    /// it freely, self-supplied by power cells), so WV-239 retires the "plug in here" read along with
    /// it: taps are pure passive backyard set-dressing now, nothing on them implies a connection point.
    ///
    /// This swaps the post + spout for the <see cref="GardenTapArt"/> standpipe — a plumbing prop, not
    /// an interactive landmark.
    ///
    /// A director, not an edit to <c>Tap</c>, matching how the boss and robots are dressed — and gated
    /// on the game's <see cref="HoseDirector"/> so it never installs into a shared PlayMode test scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TapArtDirector : MonoBehaviour
    {
        private const string ArtName = "TapArt";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<TapArtDirector>() != null) return;
            // Only the game runs a HoseDirector (it creates the taps); its absence means there is no
            // tap network here to dress, which keeps this out of unrelated test scenes (YT-129/130).
            if (FindFirstObjectByType<HoseDirector>() == null) return;
            new GameObject("TapArt").AddComponent<TapArtDirector>();
        }

        private void Update()
        {
            // Tap.All is the registry every tap self-adds to — cheaper and more direct than a scene
            // scan, and it is empty until the hose network builds, so this no-ops until there's work.
            var taps = Tap.All;
            for (int i = 0; i < taps.Count; i++)
            {
                var tap = taps[i];
                if (tap == null || tap.transform.Find(ArtName) != null) continue;   // already dressed

                GardenTapArt.Build(tap.transform).name = ArtName;
                HideGreybox(tap.transform, "TapPost");
                HideGreybox(tap.transform, "TapSpout");
            }
        }

        private static void HideGreybox(Transform tap, string childName)
        {
            var child = tap.Find(childName);
            if (child != null && child.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = false;
        }
    }
}
