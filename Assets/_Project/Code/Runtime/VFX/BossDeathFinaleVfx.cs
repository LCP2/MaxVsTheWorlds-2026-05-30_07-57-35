using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1190: the two throwaway effects the boss death finale (1.6-2.2s) needs that nothing else in
    /// the codebase already provides — ten body chunks flung outward, and a full-screen white flash.
    /// Both are one-shot: built on call, they tear themselves down once they're done, no scene-wide
    /// installer. Called once per boss death (at most) from <see cref="BossDeathSequence"/>'s finale
    /// callback, so there is no per-frame budget concern here the way there is for ambience.
    /// </summary>
    public static class BossDeathFinaleVfx
    {
        private const int ChunkCount = 10;
        private const float FlightSeconds = 0.45f;
        private const float HopHeight = 2.2f;
        private const float ScatterMin = 2.5f;
        private const float ScatterMax = 5.5f;
        private const float FadeSeconds = 1.5f;   // "fade out within 1.5s of landing" (ticket)

        private const float FlashFadeInSeconds = 0.12f;
        private const float FlashFadeOutSeconds = 0.25f;

        /// <summary>Destroy() logs an error outside Play mode (an EditMode test drives this sequence
        /// directly, with no running game) — same guard <c>SludgequeenRig.TickFootfallRings</c> and
        /// <c>GroundRing.Create</c>'s own collider cleanup already use.</summary>
        private static void DestroySafely(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Ten chunks in the boss's own body colours, flung outward from <paramref name="centre"/>
        /// on <see cref="BossVictoryPayoff.Arc"/>'s own hop maths (MV-1190's "reuse BossVictoryPayoff's
        /// arc maths") and faded out within 1.5s of landing.</summary>
        public static void SpawnChunks(Vector3 centre, Color[] bodyColors)
        {
            if (bodyColors == null || bodyColors.Length == 0) bodyColors = new[] { Color.gray };

            var root = new GameObject("BossDeathChunks");
            var driver = root.AddComponent<BossDeathChunksDriver>();
            driver.Begin(centre, bodyColors);
        }

        /// <summary>A one-shot full-screen white flash: 0.12s up, 0.25s fade, then gone.</summary>
        public static void Flash()
        {
            var go = new GameObject("BossDeathFlash", typeof(Canvas), typeof(CanvasGroup));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;   // above every HUD screen and the splash (300)

            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var imgGo = new GameObject("Flash", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(go.transform, worldPositionStays: false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = imgGo.GetComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;

            go.AddComponent<BossDeathFlashDriver>().Begin(group);
        }

        private const float ChunkFlightSeconds = FlightSeconds;

        private struct Chunk
        {
            public Transform T;
            public Vector3 From;
            public Vector3 To;
            public float T01;
            public bool Landed;
            public float LandedAt;
            public Vector3 Scale;
        }

        /// <summary>Drives the ten flung chunks: an <see cref="BossVictoryPayoff.Arc"/> flight to a
        /// scattered landing spot, then a scale-fade over <see cref="FadeSeconds"/>, then self-destroy.
        /// Unscaled time — same reason every other boss-defeat director uses it.</summary>
        [MaxWorlds.Core.PerfSection("bosses")]
        private sealed class BossDeathChunksDriver : MonoBehaviour
        {
            private readonly List<Chunk> _chunks = new List<Chunk>(ChunkCount);
            private Material[] _mats;

            public void Begin(Vector3 centre, Color[] bodyColors)
            {
                gameObject.AddComponent<KeepsOwnMaterial>();

                _mats = new Material[bodyColors.Length];
                for (int i = 0; i < bodyColors.Length; i++)
                    _mats[i] = MaterialLibrary.Tinted(SurfaceKind.Metal, bodyColors[i]);

                for (int i = 0; i < ChunkCount; i++)
                {
                    float a = i * 137.5f * Mathf.Deg2Rad;
                    float radius = Mathf.Lerp(ScatterMin, ScatterMax, Frac(i * 0.618f));
                    Vector3 to = new Vector3(centre.x + Mathf.Cos(a) * radius, 0f, centre.z + Mathf.Sin(a) * radius);

                    float s = 0.22f + Frac(i * 0.911f) * 0.3f;
                    var scale = new Vector3(s, s * (0.6f + Frac(i * 0.51f) * 0.6f), s);

                    var go = GameObject.CreatePrimitive((i % 3 == 0) ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                    go.name = "DeathChunk";
                    var col = go.GetComponent<Collider>();
                    if (col != null) DestroySafely(col);
                    go.transform.SetParent(transform, worldPositionStays: false);
                    go.transform.position = centre;
                    go.transform.rotation = Quaternion.Euler(i * 41f, i * 73f, i * 17f);
                    go.transform.localScale = scale;

                    var r = go.GetComponent<MeshRenderer>();
                    r.sharedMaterial = _mats[i % _mats.Length];
                    r.shadowCastingMode = ShadowCastingMode.Off;

                    _chunks.Add(new Chunk { T = go.transform, From = centre, To = to, Scale = scale });
                }
            }

            private void Update()
            {
                float dt = Time.unscaledDeltaTime;
                bool allDone = true;

                for (int i = 0; i < _chunks.Count; i++)
                {
                    Chunk c = _chunks[i];
                    if (c.T == null) continue;

                    if (!c.Landed)
                    {
                        allDone = false;
                        c.T01 += dt / ChunkFlightSeconds;
                        if (c.T01 >= 1f)
                        {
                            c.T01 = 1f;
                            c.Landed = true;
                            c.LandedAt = 0f;
                            c.T.position = c.To;
                        }
                        else
                        {
                            c.T.position = BossVictoryPayoff.Arc(c.From, c.To, c.T01, HopHeight);
                        }
                    }
                    else
                    {
                        c.LandedAt += dt;
                        float fade = Mathf.Clamp01(1f - c.LandedAt / FadeSeconds);
                        if (fade > 0f) allDone = false;
                        c.T.localScale = c.Scale * fade;
                    }

                    _chunks[i] = c;
                }

                if (allDone) DestroySafely(gameObject);
            }

            private void OnDestroy()
            {
                foreach (var c in _chunks) if (c.T != null) DestroySafely(c.T.gameObject);
                foreach (var m in _mats ?? System.Array.Empty<Material>()) if (m != null) DestroySafely(m);
            }

            private static float Frac(float x) => x - Mathf.Floor(x);
        }

        [MaxWorlds.Core.PerfSection("bosses")]
        private sealed class BossDeathFlashDriver : MonoBehaviour
        {
            private CanvasGroup _group;
            private float _elapsed;
            private bool _fadingOut;

            public void Begin(CanvasGroup group) => _group = group;

            private void Update()
            {
                if (_group == null) { DestroySafely(gameObject); return; }

                _elapsed += Time.unscaledDeltaTime;

                if (!_fadingOut)
                {
                    _group.alpha = Mathf.Clamp01(_elapsed / FlashFadeInSeconds);
                    if (_elapsed >= FlashFadeInSeconds) { _fadingOut = true; _elapsed = 0f; _group.alpha = 1f; }
                    return;
                }

                _group.alpha = 1f - Mathf.Clamp01(_elapsed / FlashFadeOutSeconds);
                if (_elapsed >= FlashFadeOutSeconds) DestroySafely(gameObject);
            }
        }
    }
}
