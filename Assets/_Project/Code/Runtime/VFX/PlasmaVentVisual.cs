using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1240: a plasma vent's own look — reads <see cref="MapVentDamageRunner.CyclePhase"/> (never
    /// ticks a clock of its own, so the look and the damage can never drift apart) and switches the
    /// grille between idle/glow/fire tones, showing the flame column only while it is actually live.
    ///
    /// Reads state, never writes it — same "no gameplay logic" contract
    /// <see cref="TelegraphVfx"/>'s own doc comment keeps.
    /// </summary>
    [MaxWorlds.Core.PerfSection("vents")]
    public sealed class PlasmaVentVisual : MonoBehaviour
    {
        /// <summary>#FF8C2E family — "the best in-repo match" <see cref="MaxWorlds.Rendering.ShipKit.IsReservedColour"/>'s
        /// own doc comment named, now the MV-1240 ticket's own confirmed value: orange is reserved for
        /// vents in this world. Idle tone — the grille between cycles.</summary>
        public static readonly Color IdleOrange = new Color(0.55f, 0.30f, 0.10f);

        /// <summary>The 1.0s glow tell (ticket table) — brighter than <see cref="IdleOrange"/>, still
        /// clearly a warning rather than the fire itself.</summary>
        public static readonly Color GlowOrange = new Color(1f, 0.55f, 0.18f);

        /// <summary>The flame column's own colour — hot white-orange, so it reads as fire rather than as
        /// another painted surface.</summary>
        public static readonly Color ColumnColor = new Color(1f, 0.78f, 0.45f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer _grilleRenderer;
        private MaterialPropertyBlock _mpb;
        private Transform _column;

        /// <summary>Wires this visual to the grille renderer it retints and the flame column it shows —
        /// called once, right after <see cref="MaxWorlds.Arena.MapRuntime"/> builds both. Retints through
        /// a <see cref="MaterialPropertyBlock"/> (same idiom as <see cref="GroundRing"/>), never
        /// <c>Renderer.material</c> — that call forks a brand-new per-renderer material instance every
        /// time it is read outside Play mode and logs an "Instantiating material... will leak materials
        /// into the scene" error, which fails every EditMode test that builds a map carrying a vent
        /// (MV1233MothershipLayoutTests caught this).</summary>
        public void Configure(Renderer grille, Transform column)
        {
            _grilleRenderer = grille;
            _mpb = new MaterialPropertyBlock();
            _column = column;
            if (_column != null) _column.gameObject.SetActive(false);
        }

        private void Update()
        {
            float phase = MapVentDamageRunner.CyclePhase;
            bool firing = phase >= MapVentDamage.GlowDuration &&
                          phase < MapVentDamage.GlowDuration + MapVentDamage.ColumnDuration;
            bool glowing = !firing && phase < MapVentDamage.GlowDuration;

            SetTone(firing ? ColumnColor : glowing ? GlowOrange : IdleOrange);
            if (_column != null) _column.gameObject.SetActive(firing);
        }

        private void SetTone(Color c)
        {
            if (_grilleRenderer == null) return;
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            _grilleRenderer.SetPropertyBlock(_mpb);
        }
    }
}
