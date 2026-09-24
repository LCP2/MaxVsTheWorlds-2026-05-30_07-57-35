// World 2's sludge channel flow dressing (MV-938) — replaces StormdrainKit.DressSludgeTile's old
// per-piece build (ten scrolling band Transforms, twelve chevron-leg pairs, nine foam clumps — 43
// separately animated renderers per tile, ticked every frame by the now-removed SludgeFlowRig) with
// ONE static quad per tile and this shader reproducing the same pattern entirely on the GPU: same
// colours, same band/chevron/foam counts, same two scroll speeds (bands+chevrons fast, foam slow),
// animated by _Time.y in the fragment stage so there is no per-frame CPU cost and nothing left for a
// distance gate to gate.
//
// The mesh's own UV0 carries the tile-local coordinate in METRES (not 0..1) — u along the channel's
// flow axis, v across it, both centred on the tile — so this shader's `along`/`across` read directly
// in the same units StormdrainKit's own SludgeBandLengthMin/Max etc. are authored in, with no extra
// tiling-scale property needed to convert between them.
//
// Cull Off rather than Back: the quad is built directly from the flow/across axis vectors
// (StormdrainKit.BuildSludgeFlowSurface), not through a Transform rotation, so getting the triangle
// winding provably right for every flow direction without an interactive editor preview to check it
// against is exactly the kind of thing worth trading a little overdraw on a handful of thin ground
// quads to simply not risk.
Shader "MaxWorlds/SludgeChannelFlow"
{
    Properties
    {
        _BaseColor    ("Base Color", Color) = (0.200, 0.300, 0.115, 1)
        _BandColor    ("Band Color", Color) = (0.300, 0.430, 0.150, 1)
        _ChevronColor ("Chevron Color", Color) = (0.390, 0.540, 0.190, 1)
        _FoamColor    ("Foam Color", Color) = (0.520, 0.720, 0.250, 1)

        _Run          ("Run Length (m)", Float) = 20
        _Span         ("Span Width (m)", Float) = 4
        _BandCount    ("Band Count", Float) = 10
        _ChevronPairs ("Chevron Pairs", Float) = 12
        _FoamCount    ("Foam Count", Float) = 9

        _FastSpeed    ("Fast Scroll Speed - bands/chevrons (m/s)", Float) = 0.35
        _SlowSpeed    ("Slow Scroll Speed - foam (m/s)", Float) = 0.12
        _FastPhase    ("Fast Phase Offset (m)", Float) = 0
        _SlowPhase    ("Slow Phase Offset (m)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "SludgeFlow"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BandColor;
                float4 _ChevronColor;
                float4 _FoamColor;
                float  _Run;
                float  _Span;
                float  _BandCount;
                float  _ChevronPairs;
                float  _FoamCount;
                float  _FastSpeed;
                float  _SlowSpeed;
                float  _FastPhase;
                float  _SlowPhase;
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

            // Same cheap hash idiom as StylizedGround.shader's hash21 (metre-scale use only, so it
            // can be this crude without the lattice showing) — used here to jitter each foam clump's
            // own position/size along the bank, the same per-clump variation BuildFoamClump's own
            // per-instance seed used to give before this ticket.
            float hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float along = IN.uv.x;   // metres along the flow axis, tile-local (-run/2 .. run/2)
                float across = IN.uv.y;  // metres across the flow axis, tile-local (-span/2 .. span/2)
                float halfSpan = _Span * 0.5;

                // ---- bands: alternating tone, _BandCount cells across the tile's own run ----
                float bandPeriod = _Run / max(_BandCount, 1.0);
                float bandCoord = (along + _Time.y * _FastSpeed + _FastPhase) / bandPeriod;
                float bandParity = abs(fmod(floor(bandCoord), 2.0));
                float3 color = lerp(_BaseColor.rgb, _BandColor.rgb, step(0.5, bandParity));

                // ---- chevrons: apex leads downstream, legs splay across the channel width (MV-797:
                // the apex must sit DOWNSTREAM of its own open ends along the flow axis) ----
                float chevronPeriod = _Run / max(_ChevronPairs, 1.0);
                float chevronLean = 1.35;
                float chevronCoord = (along + _Time.y * _FastSpeed + _FastPhase + abs(across) * chevronLean) / chevronPeriod;
                float chevronDist = abs(frac(chevronCoord) - 0.5) * 2.0;
                float chevronMask = 1.0 - smoothstep(0.0, 0.14, chevronDist);
                color = lerp(color, _ChevronColor.rgb, chevronMask);

                // ---- foam: clumps collecting along both banks, scrolling slower than the flow (the
                // differential is what sells the sludge as fluid rather than a conveyor) ----
                float foamPeriod = _Run / max(_FoamCount, 1.0);
                float foamAlong = along + _Time.y * _SlowSpeed + _SlowPhase;
                float foamCellIndex = floor(foamAlong / foamPeriod);
                float2 foamCell = float2(foamCellIndex, sign(across) + 0.001);
                float foamJitter = hash21(foamCell);
                float foamCenterAlong = (foamCellIndex + 0.3 + foamJitter * 0.4) * foamPeriod;
                float foamCenterAcross = halfSpan * lerp(0.55, 0.92, hash21(foamCell + 7.31));
                float foamRadius = foamPeriod * lerp(0.18, 0.34, hash21(foamCell + 2.71));
                float2 foamDelta = float2(foamAlong - foamCenterAlong, abs(across) - foamCenterAcross);
                float foamDist = length(foamDelta);
                float foamMask = 1.0 - smoothstep(foamRadius * 0.6, foamRadius, foamDist);
                color = lerp(color, _FoamColor.rgb, saturate(foamMask));

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
