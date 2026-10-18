using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// LIGHT MISSILE's aim preview (MV-1212, BUILD MODE: INDICATIVE, panel 3 the spec) -- drawn while the
    /// aim stick is held with LIGHT MISSILE active: one dashed launch line per ball in the current COUNT,
    /// each the ball's own real straight launch leg (<see cref="LightMissileBall.LaunchLegMetres"/>),
    /// fanned the SAME way <see cref="MaxWorlds.Combat.LightMissile.FireVolley"/> itself fans them, plus a
    /// lock ring on every robot the volley WOULD lock -- chosen by calling
    /// <see cref="LightMissileBall.SelectInitialTarget"/>, the SAME target-selection code the balls
    /// themselves use (MV-1197's own "never a second approximation" rule, restated on this ticket), and
    /// sharing one claimed-target set across the volley preview, the same way
    /// <see cref="MaxWorlds.Combat.LightMissile.FireVolley"/> does for the real balls.
    ///
    /// Every element here is a <see cref="LineRenderer"/>, never a <see cref="MeshRenderer"/> -- same
    /// reasoning <see cref="RicochetAimPreviewVfx"/>'s own doc comment gives (a MeshRenderer sitting under
    /// an IDamageable gets reskinned by CharacterSkinDirector's one-time sweep).
    ///
    /// Deliberately does not read any <c>MaxWorlds.Combat.LightMissile</c> constant directly -- the fan
    /// angle is passed into <see cref="Refresh"/> instead, the same "caller passes its own tuning in"
    /// shape <see cref="RicochetAimPreviewVfx.Refresh"/> already uses, so this file never needs to
    /// reference the Combat assembly-folder back.
    /// </summary>
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class LightMissileAimPreviewVfx : MonoBehaviour
    {
        /// <summary>A small fixed pool, big enough for every COUNT level the board ever reaches -- same
        /// shape <see cref="LightMissileBall"/>'s own MaxSpikeSlots pool uses. Rebuilt never; only
        /// shown/hidden per <see cref="Refresh"/> call.</summary>
        private const int MaxLines = 8;

        private const float LineWidth = 0.04f;
        private const float RingWidth = 0.05f;
        private const float RingPaddingMetres = 0.6f;
        private const float RingHeight = 0.05f;
        private const int RingSegments = 16;

        /// <summary>The ticket's own "dashed launch lines": roughly this many glow dots per line,
        /// tiling the same radial <see cref="VfxMaterials.Glow"/> texture along its length the way
        /// <see cref="LightMissileBall"/>'s own sparkle trail already tiles it -- a string of dots
        /// reads as dashed at this line's width and the fixed top-down camera distance.</summary>
        private const float DashesPerLine = 5f;

        /// <summary>Pale violet, #DCAAFF family -- the ticket's own authored dashed-line colour.</summary>
        private static readonly Color LineColor = new Color(0xDC / 255f, 0xAA / 255f, 1f, 0.7f);

        /// <summary>Violet, #E678FF family -- the ticket's own authored lock-ring colour.</summary>
        private static readonly Color RingColor = new Color(0xE6 / 255f, 0x78 / 255f, 1f, 0.9f);

        private LineRenderer[] _lines;
        private LineRenderer[] _rings;
        private bool _built;

        private readonly List<RobotEnemy> _lockedTargets = new List<RobotEnemy>(MaxLines);

        /// <summary>The robots the last <see cref="Refresh"/> call found the volley would lock, in ball
        /// order -- what an EditMode test reads to compare against an independently-driven call to
        /// <see cref="LightMissileBall.SelectInitialTarget"/> (MV-465 Tier 2: resolved, read off this
        /// instance after it did the work, not re-derived blind).</summary>
        public IReadOnlyList<RobotEnemy> LockedTargets => _lockedTargets;

        /// <summary>How many launch lines the last <see cref="Refresh"/> call actually drew.</summary>
        public int ActiveLineCount { get; private set; }

        /// <summary>Self-attach onto <paramref name="owner"/> -- same shape
        /// <see cref="RicochetAimPreviewVfx.Attach"/> uses. Deliberately does NOT build the lines/rings
        /// yet (see <see cref="Refresh"/>) -- most of a run is never spent holding LIGHT MISSILE's aim
        /// stick, so most players never pay for this at all.</summary>
        public static LightMissileAimPreviewVfx Attach(Transform owner)
        {
            var go = new GameObject("LightMissileAimPreview");
            go.transform.SetParent(owner, false);
            return go.AddComponent<LightMissileAimPreviewVfx>();
        }

        private void Build()
        {
            Material mat = VfxMaterials.Additive(VfxMaterials.Glow());
            _lines = new LineRenderer[MaxLines];
            _rings = new LineRenderer[MaxLines];
            for (int i = 0; i < MaxLines; i++)
            {
                // Dashed, per the ticket and the design image's own panel 3 -- an explicit PER-INSTANCE
                // clone of the shared glow material (never the cached one every other additive effect
                // also uses, and never Renderer.material's own lazy-instantiate, which logs an
                // edit-mode "leak" error that fails an EditMode test) tiled along the line's length,
                // same tiling idiom LightMissileBall's own sparkle trail uses.
                Material dashedMat = new Material(mat)
                {
                    mainTextureScale = new Vector2(DashesPerLine / LightMissileBall.LaunchLegMetres, 1f),
                };
                _lines[i] = NewLineRenderer("Line" + i, dashedMat, LineWidth, LineColor, worldSpace: true,
                    loop: false, positionCount: 2);
                _lines[i].textureMode = LineTextureMode.Tile;

                _rings[i] = NewLineRenderer("Ring" + i, mat, RingWidth, RingColor, worldSpace: false,
                    loop: true, positionCount: RingSegments);
                _lines[i].gameObject.SetActive(false);
                _rings[i].gameObject.SetActive(false);
            }
        }

        private LineRenderer NewLineRenderer(string name, Material mat, float width, Color color,
            bool worldSpace, bool loop, int positionCount)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.useWorldSpace = worldSpace;
            lr.loop = loop;
            lr.positionCount = positionCount;
            lr.widthMultiplier = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = mat;
            lr.startColor = color;
            lr.endColor = color;
            return lr;
        }

        /// <summary>A flat ring, local-space -- same local-space-ring idiom
        /// <see cref="RicochetAimPreviewVfx"/>'s own NewRing uses, so dropping it on a world point is just
        /// setting the GameObject's own <see cref="Transform.position"/>.</summary>
        private static void SizeRing(LineRenderer ring, float diameter)
        {
            float radius = diameter * 0.5f;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * (360f / RingSegments) * Mathf.Deg2Rad;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, RingHeight, Mathf.Sin(a) * radius));
            }
        }

        /// <summary>Recompute and show (or hide) the whole preview for this frame -- called every
        /// <see cref="MaxWorlds.Combat.LightMissile.Tick"/>, not a separate Update, so visibility tracks
        /// the exact same frame the weapon itself decides to aim or not. <paramref name="forward"/> must
        /// already be the flattened, normalised heading the volley itself fans from; <paramref name="fanDegrees"/>
        /// and <paramref name="count"/> are the caller's own current COUNT and fan-angle tuning, same shape
        /// <see cref="RicochetAimPreviewVfx.Refresh"/> takes its tuning as explicit parameters rather than
        /// reading a sibling Combat constant directly.</summary>
        public void Refresh(bool visible, Vector3 origin, Vector3 forward, int count, float fanDegrees,
            int areaIndex)
        {
            if (!visible) { if (_built) HideAll(); return; }
            if (!_built) { Build(); _built = true; }

            count = Mathf.Clamp(count, 0, MaxLines);
            ActiveLineCount = count;
            _lockedTargets.Clear();

            // Rule 1 (same as FireVolley): one claimed-target set shared across the whole volley preview,
            // so the preview can never show a robot locked twice when FireVolley itself wouldn't.
            var claimed = new HashSet<IDamageable>();

            for (int i = 0; i < MaxLines; i++)
            {
                bool active = i < count;
                _lines[i].gameObject.SetActive(active);
                _rings[i].gameObject.SetActive(false);
                if (!active) continue;

                float angleOffset = (i - (count - 1) / 2f) * fanDegrees;
                Vector3 dir = Quaternion.Euler(0f, angleOffset, 0f) * forward;
                Vector3 legEnd = origin + dir * LightMissileBall.LaunchLegMetres;
                _lines[i].SetPosition(0, origin);
                _lines[i].SetPosition(1, legEnd);

                // SAME selection code the ball itself calls once its own launch leg completes -- never a
                // second approximation (MV-1197's rule, restated on this ticket).
                IDamageable picked = LightMissileBall.SelectInitialTarget(legEnd, dir, areaIndex,
                    LightMissileBall.TargetRangeMetres, claimed);
                if (!(picked is RobotEnemy robot)) continue;
                claimed.Add(picked);
                _lockedTargets.Add(robot);

                float diameter = EnemyArchetype.Of(robot.Kind).ColliderRadius * 2f + RingPaddingMetres;
                SizeRing(_rings[i], diameter);
                _rings[i].transform.position = robot.transform.position;
                _rings[i].gameObject.SetActive(true);
            }
        }

        /// <summary>The world-space point a line's own endpoint (0 = origin, 1 = launch-leg end) was last
        /// set to -- read back off the real <see cref="LineRenderer"/> after <see cref="Refresh"/>, not
        /// re-derived, so a test measures the resolved geometry (MV-465 Tier 2).</summary>
        public Vector3 LinePoint(int ballIndex, int pointIndex) => _lines[ballIndex].GetPosition(pointIndex);

        private void HideAll()
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i].gameObject.SetActive(false);
                _rings[i].gameObject.SetActive(false);
            }
            ActiveLineCount = 0;
            _lockedTargets.Clear();
        }
    }
}
