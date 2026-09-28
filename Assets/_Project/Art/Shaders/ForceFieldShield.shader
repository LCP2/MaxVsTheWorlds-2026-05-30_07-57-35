// Force Field shield shader (MV-391) — a translucent BLUE/CYAN energy-shield dome with a
// genuine, view-dependent glowing rim and a travelling shimmer band that sweeps the sphere's
// local Y axis, matching Lee's TestFlight review (MV-990: the shield should be "a simple blue
// sphere with a pulse that rises from bottom to top").
//
// Reuses StylizedCharacter.shader's exact Fresnel term (pow(1 - saturate(dot(N,V)), power) *
// strength) — the same "loud, tight rim" that already reads at the fixed ~72° camera on every
// character in the yard — but as its own transparent, unlit, alpha-blended pass instead of an
// additive term on top of a lit opaque body. A shield has no albedo to light: it is a thin,
// mostly-empty film that should show almost nothing at its centre (so Max stays visible inside
// it) and brighten only where the surface turns edge-on to the camera or the shimmer band sweeps
// past.
//
// MV-990 (Lee, TestFlight, 2026-09-28): removed the hex-panel lattice this shader used to draw
// (MV-391/MV-455) — it obscured the rising pulse Lee actually wants. The shimmer band
// (`_ShimmerBandSpeed`/`_ShimmerBandWidth`), driven off `_Time.y` against the sphere's own local
// Y axis, is that pulse and is unchanged; only the lattice (`HexSeamFactor`, the triplanar `seam*`
// samples, `panelGlow`, and the `pulse` term that only modulated it) and its five properties
// (`_PanelScale`, `_PanelSeamWidth`, `_PanelSeamBoost`, `_PulseSpeed`, `_PulseStrength`) are gone.
//
// MV-455 (Lee, 0.8.3 build review) history, kept for the parts still relevant to the fill/rim
// maths below: Cull Off draws both hemispheres of the sphere at one screen pixel, so with
// Blend SrcAlpha OneMinusSrcAlpha every fragment's alpha would be composited TWICE if emitted
// directly: the visible coverage would be 1-(1-a)^2, not a. Fixed by emitting the exact
// per-fragment alpha `e` that makes the double-blend land back on the INTENDED single-pass value
// `A`:
//     1 - (1-e)^2 = A   =>   e = 1 - sqrt(1-A)
// (for small A this is ~A/2, i.e. "divide the alpha", but the exact inverse is used below so it
// holds at the rim too, where A can approach 1). See `alpha` in Frag(). `color` is likewise
// `saturate()`d before rim+shimmer are added, so it can never blow past `_RimColor` into flat
// white.
//
// All of the above are dev-mode Settings-panel sliders (SettingsPanel's Feel tab, MV-455) so Lee
// dials the final numbers by eye rather than this ticket guessing them — same always-present,
// ungated pattern as the existing camera-zoom knob (YT-120: the panel is compiled into every
// build, no #if, no build-time define; see DevTuning/SettingsPanel for why that replaced the old
// dev-only overlay).
Shader "MaxWorlds/ForceFieldShield"
{
    Properties
    {
        // Fill: alpha is the "subtle, mostly-transparent" half of the DECISION. Gameplay drives
        // this (and RimColor) through a MaterialPropertyBlock as the absorb budget depletes —
        // see ForceFieldBubble.SetFraction — so the whole ready-to-empty colour shift lives here,
        // not hardcoded in the shader. Defaults below are the steady-state blue/cyan look.
        _BaseColor      ("Fill Color", Color) = (0.22, 0.5, 1.0, 0.16)
        _RimColor       ("Rim Color", Color) = (0.55, 0.9, 1.0, 1)
        // MV-658 bakes Lee's 2026-09-02 tuning pass as the new compiled-in default (was 2.4).
        _RimPower       ("Rim Power", Range(0.5, 8)) = 8
        // MV-583 bakes Lee's 26 Aug 2026 tuning session (SG1) as the new compiled-in defaults, kept
        // 1:1 with SettingsPanel's own Add(...) defaults so a fresh run matches the panel with no
        // dev override needed — see SettingsPanel.cs's Feel-tab Force Field knobs for the readings
        // each one bakes. _RimStrength: SG1 read 0% (slider minimum).
        _RimStrength    ("Rim Strength", Range(0, 6)) = 0

        // The shimmer itself (MV-455): a soft highlight band that sweeps the dome's surface along
        // its local Y axis over time, looping. Speed is full sweeps/second, Width is the band's
        // extent as a fraction of the axis (0..1). MV-583: Speed's SG1 reading (99%, i.e.
        // unchanged) is left alone here as the Level-1 baseline — see
        // AbilityTuning.ForceFieldShimmerBandSpeed, which now derives the LIVE value from Force
        // Field's level instead of this fixed compiled-in default. Width: SG1 read 27% ->
        // PosToValue(0.02, 1, 0.18, 0.27) = 0.0632 (was 0.18).
        _ShimmerBandSpeed ("Shimmer Band Speed", Range(0, 2)) = 0.35
        _ShimmerBandWidth ("Shimmer Band Width", Range(0.02, 1)) = 0.0632

        // Hard ceiling on the BODY alpha (fill + shimmer band, before the rim adds its own coverage
        // on top) — MV-455 AC: "no more than ~0.35 composited at any fragment away from the rim".
        // Applied pre-compensation, i.e. this is the true composited value Max is seen through.
        // MV-583: SG1 read 50% (half the old default) -> PosToValue(0, 1, 0.35, 0.5) = 0.175.
        _AlphaCeiling   ("Alpha Ceiling (body)", Range(0, 1)) = 0.175
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Shield"
            Tags { "LightMode" = "UniversalForward" }

            // Both faces: the far side of the bubble is exactly what makes the near-silhouette
            // read as edge-on from inside the sweep of the fixed camera, and a shield has no
            // "inside" that should ever be hidden from itself.
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float  _RimPower;
                float  _RimStrength;
                float  _ShimmerBandSpeed;
                float  _ShimmerBandWidth;
                float  _AlphaCeiling;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Same Fresnel term as StylizedCharacter's rim — see that shader for why this
                // exact curve reads as an edge rather than a wash at this camera's fixed pitch.
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;

                // The shimmer (MV-455, AC3): a soft highlight band that sweeps along the sphere's
                // own local Y axis over time and loops, so the eye reads motion travelling across
                // the film rather than the whole dome brightening at once.
                float3 nOS = normalize(IN.positionOS);
                float axisN = saturate(nOS.y * 0.5 + 0.5);
                float bandPhase = frac(_Time.y * _ShimmerBandSpeed);
                float bandDist = abs(axisN - bandPhase);
                bandDist = min(bandDist, 1.0 - bandDist); // wrap so the sweep loops seamlessly
                float shimmerBand = 1.0 - smoothstep(0.0, max(_ShimmerBandWidth, 1e-4), bandDist);

                // Glow term shared by colour and alpha, clamped once (AC2) so the shimmer can
                // never blow `color` past `_RimColor` into flat white.
                float glow = saturate(rim + shimmerBand * 0.5);
                float3 color = _BaseColor.rgb + _RimColor.rgb * glow;

                // Body alpha (fill + shimmer, everything except the rim) is hard-ceilinged (AC5)
                // so the dome's interior stays "well below opaque" regardless of how the shimmer
                // sliders are dialled; the rim is exempt and still adds its own coverage on top,
                // same "rim ADDS, never replaces the fill" contract as before.
                float bodyAlpha = min(_BaseColor.a + shimmerBand * 0.3, _AlphaCeiling);
                float singlePassAlpha = saturate(bodyAlpha + rim);

                // Undo the Cull-Off double-composite (see file header maths): emit the alpha that,
                // blended twice (near + far hemisphere), lands back on `singlePassAlpha`.
                float alpha = 1.0 - sqrt(saturate(1.0 - singlePassAlpha));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
