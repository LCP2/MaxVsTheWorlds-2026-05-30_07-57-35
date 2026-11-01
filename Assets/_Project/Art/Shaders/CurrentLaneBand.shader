// A Reef current lane's own translucent band + chevron quad (MV-1335). Procedural, not textured:
// an earlier pass of this ticket baked the band/chevron pattern into a runtime Texture2D
// (StylizedTextures) and sampled it here via SAMPLE_TEXTURE2D/_BaseMap_ST, the same convention
// MapCurrentLaneVisual already scrolls MaxWorlds.Arena.SludgeFlow's own material in. That compiled,
// ran, and reported a correctly-bound, correctly-named Material from script, but every GPU sample
// of that texture read back flat black in the Standalone build (isolated by swapping in
// Texture2D.whiteTexture, which DID sample correctly through the exact same shader/material code
// path -- so the fault is specific to this project's runtime-generated procedural textures reaching
// THIS shader, not to texture sampling or to transparency in general: ForceFieldShield.shader is a
// transparent custom shader with no texture at all, and StylizedGround.shader samples a
// StylizedTextures-authored texture but only from an OPAQUE pass). Rather than chase that mismatch
// further, this follows the one construction already proven to work for a "built and scrolled purely
// at runtime" MaxWorlds shader in THIS project: SludgeChannelFlow.shader's own chevron math,
// entirely procedural in the fragment stage, driven by the mesh's own metre-scale UV0 (u along the
// flow axis, v across it, both centred on the tile -- same convention, so ReefCurrentLaneKit reads
// directly in the same units ChevronPeriodMetres is already authored in, no extra tiling-scale
// property needed).
//
// Blend is hardcoded (no keyword toggle) for the same reason SludgeChannelFlow.shader's own header
// already documents: a Material built purely at runtime never uses a stock shader's keyword-gated
// variant, so URP's build-time stripper can drop a keyword combination no on-disk Material asset
// requests, and a runtime-built Material then silently draws the wrong (or no) variant.
//
// Cull Off rather than Back: ReefCurrentLaneKit.BuildLane builds the quad directly from the lane's
// own flow/across axis vectors, not through a Transform rotation, so getting the triangle winding
// provably right for every authored direction without an interactive editor preview is exactly the
// kind of thing worth trading a little overdraw on a handful of thin ground quads to simply not risk
// (SludgeChannelFlow.shader's own header documents the identical reasoning).
Shader "MaxWorlds/CurrentLaneBand"
{
    Properties
    {
        // MV-1335's own first real (post-fix) capture of a16_cur1 at Mobile tier measured mean hue
        // 216.2 deg (target 180-215) and a luminance delta of 19.1 against the floor (target >=20) --
        // both a touch off because the Reef's own circuit-glow floor colour bleeds blue/violet into the
        // alpha-blended composite, pulling the measured hue up past the band's own baked ~204 deg.
        // Shifted both colours a few degrees more cyan (baked hue ~190) and a little more opaque so the
        // floor contributes less to the blend.
        _BandColor    ("Band Color", Color) = (0.102, 0.725, 0.850, 0.40)
        _ChevronColor ("Chevron Color", Color) = (0.146, 0.833, 0.970, 0.60)
        _Span         ("Span Width (m)", Float) = 4
        _ChevronPeriod ("Chevron Period (m)", Float) = 2.5
        _ScrollOffset ("Scroll Offset Along Flow (m)", Float) = 0
        _PulseBoost   ("Pulse Alpha Boost (0..1+)", Float) = 0
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
            Name "CurrentLaneBand"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BandColor;
                half4 _ChevronColor;
                float _Span;
                float _ChevronPeriod;
                float _ScrollOffset;
                float _PulseBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float along = IN.uv.x + _ScrollOffset;   // metres along the flow axis, tile-local
                float across = IN.uv.y;                  // metres across the flow axis, tile-local
                float halfSpan = max(_Span * 0.5, 1e-4);

                // Apex leads downstream, legs trail and splay across the lane's own width -- same
                // "apex must sit downstream of its own open ends" shape SludgeChannelFlow.shader's own
                // chevron uses, one arrow per _ChevronPeriod.
                float legU = saturate(abs(across) / halfSpan);          // 0 at centre, 1 at the edges
                float alongNorm = frac(along / _ChevronPeriod);         // 0..1 within one period
                float apexNorm = 0.75 - legU * 0.5;
                float chevronMask = 1.0 - smoothstep(0.03, 0.05, abs(alongNorm - apexNorm));

                half4 col = lerp(_BandColor, _ChevronColor, chevronMask);

                // Pre-flip pulse (MV-1335 change item 2): MapCurrentLaneVisual drives _PulseBoost up
                // to ~1 for the final 1.5s before a reversal, brightening the band so the flip reads
                // as telegraphed rather than instant.
                col.a = saturate(col.a + _PulseBoost);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
