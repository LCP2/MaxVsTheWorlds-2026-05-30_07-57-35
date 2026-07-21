using UnityEngine;
using MaxWorlds.Hose;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Dresses the garden taps with their real art (YT-134). YT-129/130 stand each tap up as a
    /// greybox — a post, a spout, and a glowing connection bulb — and place a whole network of them
    /// (YT-130), so every one of them is a bare two-cylinder stub the player is meant to read as a
    /// "plug in here" landmark.
    ///
    /// This swaps the post + spout for the <see cref="GardenTapArt"/> standpipe. It deliberately KEEPS
    /// the tap's own <c>TapIndicator</c> bulb — that one is functional (it glows cyan when Max is
    /// plugged into this tap, and gameplay tints it per state), so unlike the post and spout it is not
    /// ours to replace. It sits just above the new valve wheel, reading as the connection light.
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
                // TapIndicator is left alone — it is the connection light gameplay drives.
            }
        }

        private static void HideGreybox(Transform tap, string childName)
        {
            var child = tap.Find(childName);
            if (child != null && child.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = false;
        }
    }
}
