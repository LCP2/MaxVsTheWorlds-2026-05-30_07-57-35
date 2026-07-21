using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Hose
{
    /// <summary>
    /// The leash (YT-129, weapon epic YT-127). Max's hose is bound to a <see cref="Tap"/>, and he can
    /// only range a hard maximum distance from it. Past that radius he is pinned to the tether circle
    /// — not slowed, not warned, stopped — which is what makes a tap a place he has to think about
    /// rather than scenery. It also draws the hose itself, a line from the tap's spout to his hands.
    ///
    /// The clamp runs in <see cref="LateUpdate"/>, after <see cref="MaxWorlds.Player.PlayerController"/>
    /// has already moved him for the frame, so it corrects the final position rather than fighting the
    /// move mid-step. The max length reads through <see cref="DevTuning"/> every frame, so the Settings
    /// panel's "Hose tether" slider takes effect live with no push.
    ///
    /// Tap-SWITCHING (detach, re-plug into a nearer tap) is YT-130 — here <see cref="SetTap"/> just
    /// binds the one starting tap. The switcher will call the same method.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoseTether : MonoBehaviour
    {
        /// <summary>Hard max tether length in metres, before any dev override. Generous enough to
        /// work the opening lawn from the patio tap, short enough that the leash is felt.</summary>
        public const float AuthoredLength = 20f;

        /// <summary>Effective max length this frame — the Settings slider (YT-129) may be overriding it.</summary>
        public static float Length => DevTuning.Or(DevTuning.HoseTetherLength, AuthoredLength);

        private const int HoseSegments = 12;
        private static readonly Color HoseGreen = new Color(0.24f, 0.55f, 0.30f); // garden-hose green

        private Tap _tap;
        private CharacterController _cc;
        private LineRenderer _hose;

        /// <summary>The tap Max is currently plugged into (null until the director wires it).</summary>
        public Tap Tap => _tap;

        /// <summary>Plug the hose into a tap. YT-130's tap-switcher re-calls this to re-anchor the leash.</summary>
        public void SetTap(Tap tap) => _tap = tap;

        /// <summary>
        /// Pure leash clamp: where Max is allowed to stand, given his position, the tap, and the max
        /// length. Planar — it never touches his Y (he stays on the ground he was on). Inside the
        /// radius he is returned unchanged; past it he is pinned to the point on the tether circle in
        /// his own direction, so he slides along the leash rather than snapping to a fixed spot.
        /// </summary>
        public static Vector3 Clamp(Vector3 max, Vector3 tap, float length)
        {
            float dx = max.x - tap.x;
            float dz = max.z - tap.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d <= length || d < 1e-5f) return max;
            float k = length / d;
            return new Vector3(tap.x + dx * k, max.y, tap.z + dz * k);
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            BuildHose();
        }

        private void LateUpdate()
        {
            if (_tap == null) return;

            Vector3 clamped = Clamp(transform.position, _tap.transform.position, Length);
            if (clamped != transform.position) MoveTo(clamped);

            DrawHose();
        }

        /// <summary>Reposition Max honouring the CharacterController, which caches its own position and
        /// would otherwise undo a raw <c>transform.position</c> write — the same disable/enable dance
        /// <c>MapRuntime.Adopt</c> uses to teleport him at spawn.</summary>
        private void MoveTo(Vector3 pos)
        {
            if (_cc != null)
            {
                bool was = _cc.enabled;
                _cc.enabled = false;
                transform.position = pos;
                _cc.enabled = was;
            }
            else
            {
                transform.position = pos;
            }
        }

        private void BuildHose()
        {
            var go = new GameObject("Hose");
            // Parented to Max so it dies with him (no leaked unparented renderer), and marked so the
            // surface sweep and the character-skin director both leave its material alone (it is not
            // scenery and not part of Max's body).
            go.transform.SetParent(transform, worldPositionStays: false);
            go.AddComponent<KeepsOwnMaterial>();

            _hose = go.AddComponent<LineRenderer>();
            _hose.material = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            _hose.useWorldSpace = true;                 // positions are set in world space each frame
            _hose.widthMultiplier = 0.14f;
            _hose.numCapVertices = 4;
            _hose.numCornerVertices = 2;
            _hose.textureMode = LineTextureMode.Stretch;
            _hose.startColor = HoseGreen;
            _hose.endColor = HoseGreen;
            _hose.positionCount = HoseSegments + 1;
            _hose.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _hose.receiveShadows = false;
        }

        /// <summary>Draw the hose as a gently sagging line from the tap spout to Max's hands, so it
        /// reads as a slack rubber hose rather than a taut wire.</summary>
        private void DrawHose()
        {
            if (_hose == null) return;

            Vector3 a = _tap.NozzlePosition;
            Vector3 b = transform.position + Vector3.up * 0.6f + transform.forward * 0.4f;
            float sag = Mathf.Min(0.4f, Vector3.Distance(a, b) * 0.06f);

            for (int i = 0; i <= HoseSegments; i++)
            {
                float t = i / (float)HoseSegments;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * (1f - (2f * t - 1f) * (2f * t - 1f)); // parabola: 0 at the ends, max mid-span
                _hose.SetPosition(i, p);
            }
        }
    }
}
