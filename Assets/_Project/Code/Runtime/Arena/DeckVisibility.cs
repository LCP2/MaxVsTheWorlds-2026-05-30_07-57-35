using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>MV-692 readability rule (craft bible: readability &gt; richness): while Max stands more
    /// than <see cref="FeetBelowThreshold"/> under a deck that covers his XZ position, that deck's grate
    /// fades to <see cref="UnderneathAlpha"/> and its kerb rails hide, so a walkway over his own head
    /// never blocks him seeing himself — restored the moment he leaves. Driven purely from Max's own
    /// position; no camera change, per the ticket's own scope.</summary>
    [DisallowMultipleComponent]
    public sealed class DeckVisibility : MonoBehaviour
    {
        private const float FadeSeconds = 0.15f;
        private const float UnderneathAlpha = 0.35f;
        private const float FeetBelowThreshold = 0.5f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer _grate;
        private GameObject[] _rails;
        private Rect _footprint;
        private float _topY;
        private Transform _player;
        private float _alpha = 1f;
        private MaterialPropertyBlock _mpb;

        public void Configure(Renderer grate, GameObject[] rails, Rect footprint, float deckTopY)
        {
            _grate = grate;
            _rails = rails;
            _footprint = footprint;
            _topY = deckTopY;
        }

        private void Awake() => _mpb = new MaterialPropertyBlock();

        private void Update()
        {
            if (_player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _player = p.transform;
            }

            _alpha = Mathf.MoveTowards(_alpha, TargetAlphaFor(_player.position), Time.deltaTime / FadeSeconds);
            ApplyAlpha();
        }

        /// <summary>The alpha a body at <paramref name="position"/> should fade this deck toward — the
        /// resolved target <see cref="Update"/> smooths toward every frame, exposed directly (MV-711) so
        /// an EditMode test can assert it without ticking real time through the fade.</summary>
        public float TargetAlphaFor(Vector3 position) =>
            _footprint.Contains(new Vector2(position.x, position.z)) && (_topY - position.y) > FeetBelowThreshold
                ? UnderneathAlpha : 1f;

        private void ApplyAlpha()
        {
            if (_grate != null)
            {
                _grate.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, _alpha));
                _grate.SetPropertyBlock(_mpb);
            }

            bool showRails = _alpha > 0.99f;
            if (_rails == null) return;
            foreach (GameObject rail in _rails)
                if (rail != null && rail.activeSelf != showRails) rail.SetActive(showRails);
        }
    }
}
