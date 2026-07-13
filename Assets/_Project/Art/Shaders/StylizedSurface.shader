// Stylized surface shader (YT-77) — wood, stone, dirt and painted metal that read as materials
// rather than as coloured blocks.
//
// Why this exists at all, when URP/Lit was already lighting these surfaces correctly: URP/Lit reads
// its textures through the MESH'S UVs, and in this yard mesh UVs are unusable for detail.
//
//  1. The set dressing is a Kenney kit. Its FBXs are UV-mapped to swatches in a palette atlas — a
//     whole fence plank samples a few pixels of flat colour. A tiling wood grain fed through those
//     UVs would land as one uniform tone per face: exactly the flat look this ticket is about.
//
//  2. The props are SCALED at runtime — fence panels stretch to the wall height, hedges fill their
//     cover block, the greybox walls are one cube each. UV-space detail scales with the object, so
//     the same plank texture would come out fine on a post and smeared across a wall.
//
// So detail is projected from WORLD POSITION instead, triplanar. Grain is then a fixed physical
// size — a centimetre of timber is a centimetre of timber on every prop in the yard, whatever the
// mesh's UVs say and whatever scale it was instantiated at. StylizedGround (YT-69) made the same
// call for the same reason; this is that idea applied to everything standing on the lawn.
//
// Lighting goes through UniversalFragmentPBR rather than a hand-rolled lambert like the ground's.
// That is deliberate and load-bearing: the sun sits at 40° and these are VERTICAL surfaces, which
// catch almost nothing from it. What actually lights the shadow side of a fence is the FILL light —
// an additional directional light — plus ambient. A shader that only read GetMainLight (as the
// ground's does, correctly, for a floor) would render every fence panel facing away from the sun as
// a black silhouette, which is the exact regression YT-76 raised the fill to cure.
Shader "MaxWorlds/StylizedSurface"
{
    Properties
    {
        // The tone. Gameplay also drives this through a MaterialPropertyBlock on damageable bodies
        // (the Mower Hutch's hazard-orange, its damage tint, hit flashes) — which is why a surface
        // meant for a damageable object carries its wear in a GREYSCALE albedo and leaves the colour
        // to this. Multiplying a coloured albedo by a coloured tint would fight gameplay for the hue.
        _BaseColor      ("Base Color", Color) = (1,1,1,1)

        // Kept for the same reason StylizedCharacter keeps it: gameplay drives the hit flash and the
        // wind-up tell through a MaterialPropertyBlock on _EmissionColor. The Mower Hutch wears this
        // shader (it is a machine, not a character), so dropping the property would silence every
        // tell the factory has.
        _EmissionColor  ("Emission Color", Color) = (0,0,0,0)

        _BaseMap        ("Albedo", 2D) = "white" {}
        _BumpMap        ("Normal", 2D) = "bump" {}

        // Tiles per METRE of world, not per UV. See the header.
        _DetailScale    ("Detail Scale (tiles/m)", Range(0.05, 8)) = 1.6
        _NormalStrength ("Normal Strength", Range(0, 3)) = 1.0

        // How hard the triplanar projection snaps to the dominant axis. The kit is chunky low-poly
        // and mostly axis-aligned, so a high exponent keeps planks crisp on a flat face instead of
        // cross-fading three projections into mush; it still blends on a tree trunk or a rock.
        _Sharpness      ("Triplanar Sharpness", Range(1, 16)) = 6

        _Smoothness     ("Smoothness", Range(0, 1)) = 0.08
        _Metallic       ("Metallic", Range(0, 1)) = 0
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex SurfaceVert
            #pragma fragment SurfaceFrag

            // The fill and rim lights are ADDITIONAL directional lights — without these keywords the
            // shadow side of every vertical surface in the yard goes black. See the header.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // The mobile tier is what WebGL ships (YT-76), and its SSAO is what puts the fence posts
            // back in contact with the lawn. Without this keyword the pass runs but never lands here.
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float  _DetailScale;
                float  _NormalStrength;
                float  _Sharpness;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);   SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
            };

            Varyings SurfaceVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 SurfaceFrag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 wp = IN.positionWS * _DetailScale;

                // Triplanar weights, sharpened toward the dominant axis.
                float3 w = pow(abs(N), _Sharpness);
                w /= max(1e-4, w.x + w.y + w.z);

                // The three projections. A texture authored with its grain running along V therefore
                // runs VERTICALLY on any vertical face — which is what a fence paling and a shed
                // plank actually do — and lies flat along an axis on a horizontal one.
                float2 uvX = wp.zy;
                float2 uvY = wp.xz;
                float2 uvZ = wp.xy;

                float3 albedo =
                      SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * w.y
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;

                albedo *= _BaseColor.rgb;

                // Unpacked by hand, not through UnpackNormalScale: these maps are generated at runtime
                // as plain linear RGB (StylizedTextures.NormalFor), not DXT5nm-swizzled, so the helper
                // would read the wrong channels on whichever platform took the other branch. Same
                // reasoning as StylizedGround.
                float2 tx = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX).rg * 2.0 - 1.0;
                float2 ty = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY).rg * 2.0 - 1.0;
                float2 tz = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ).rg * 2.0 - 1.0;

                // Each plane's 2D slope, rotated back into the world axes it was projected along, then
                // added to the geometric normal. Cheaper and far more stable than reconstructing three
                // tangent frames and sign-correcting them, and for a stylised surface the difference
                // is invisible — this only ever has to make the light break, not carry a bump-mapped
                // brick wall.
                float3 slope = float3(0, tx.y, tx.x) * w.x
                             + float3(ty.x, 0, ty.y) * w.y
                             + float3(tz.x, tz.y, 0) * w.z;

                float3 normalWS = normalize(N + slope * _NormalStrength);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.emission = _EmissionColor.rgb;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
    }

    // Supplies ShadowCaster/DepthOnly/DepthNormals, and is the safety net if this SubShader is ever
    // unsupported — a fence that falls back to plain Lit is a look regression, a magenta fence is a
    // broken build (YT-58).
    Fallback "Universal Render Pipeline/Lit"
}
