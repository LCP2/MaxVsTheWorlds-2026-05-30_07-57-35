using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>MV-1229: drives the drop-pod launch tube's two porthole discs from ocean blue through sky
    /// to a star field as Max walks segment B of the Reef -> Mothership corridor. Position-driven, not a
    /// timer -- Max keeps full control the whole corridor (MV-1123's own standing rule: the game never
    /// takes control of him in a transition), so there is no scripted duration to animate against; the
    /// colour is simply wherever he's standing between the tube's own two ends, however long that takes.</summary>
    [MaxWorlds.Core.PerfSection("intro")]
    public sealed class PodLaunchPorthole : MonoBehaviour
    {
        private static readonly Color OceanBlue = new Color(0.102f, 0.282f, 0.482f);
        private static readonly Color SkyBlue = new Color(0.561f, 0.776f, 0.925f);
        private static readonly Color StarField = new Color(0.035f, 0.043f, 0.090f);

        private Vector2 _doorMouth;
        private Wall _wall;
        private float _tubeStart;
        private float _tubeEnd;
        private Renderer[] _portholes;
        private Transform _player;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>MV-527: <paramref name="player"/> is resolved ONCE by the caller (who already holds
        /// it building the rest of the corridor) rather than re-found here every frame.</summary>
        public void Configure(Vector2 doorMouth, Wall wall, float tubeStart, float tubeEnd, Renderer[] portholes, Transform player)
        {
            _doorMouth = doorMouth;
            _wall = wall;
            _tubeStart = tubeStart;
            _tubeEnd = tubeEnd;
            _portholes = portholes;
            _player = player;
            _mpb = new MaterialPropertyBlock();
            Apply(0f);
        }

        /// <summary>The porthole colour at progress <paramref name="t"/> in [0,1] -- a pure function so a
        /// future test could assert it without a live frame loop, even though this ticket's own AC
        /// doesn't ask for one.</summary>
        public static Color ColorForProgress(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? Color.Lerp(OceanBlue, SkyBlue, t / 0.5f) : Color.Lerp(SkyBlue, StarField, (t - 0.5f) / 0.5f);
        }

        private void Update()
        {
            if (_player == null) return;
            float along = OutwardAlong(_player.position, _doorMouth, _wall);
            float t = Mathf.InverseLerp(_tubeStart, _tubeEnd, along);
            Apply(t);
        }

        private void Apply(float t)
        {
            if (_portholes == null || _mpb == null) return;
            Color c = ColorForProgress(t);
            foreach (Renderer r in _portholes)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(EmissionColorId, c * 1.2f);
                r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Mirrors <c>WorldJoinSequence</c>'s own private <c>AlongDistance</c> (duplicated rather
        /// than exposed -- the same call <c>WorldJoinDressing</c> already makes against its own
        /// OutwardDir/AcrossDir pair).</summary>
        private static float OutwardAlong(Vector3 pos, Vector2 doorMouth, Wall wall)
        {
            Vector3 dir = wall switch
            {
                Wall.N => Vector3.forward,
                Wall.S => Vector3.back,
                Wall.E => Vector3.right,
                Wall.W => Vector3.left,
                _ => Vector3.forward,
            };
            bool travelAlongZ = dir.x == 0f;
            float raw = travelAlongZ ? pos.z - doorMouth.y : pos.x - doorMouth.x;
            return raw * (travelAlongZ ? dir.z : dir.x);
        }
    }
}
