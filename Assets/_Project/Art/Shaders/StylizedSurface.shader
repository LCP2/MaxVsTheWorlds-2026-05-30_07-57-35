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

        _Smoothness     ("Smoothness", Range(0, 1)) = 0.08
        _Metallic       ("Metallic", Range(0, 1)) = 0

        [Header(Wind YT78)]
        // METRES of sway at full height. Zero for everything that isn't a plant — a fence that
        // breathes is a bug, and the yard's walls wear this same shader.
        _WindStrength   ("Wind Strength (m)", Range(0, 0.5)) = 0
        _WindSpeed      ("Wind Speed", Range(0, 4)) = 1.1
        _WindHeight     ("Wind Full-Bend Height (m)", Range(0.2, 8)) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // Shared by every pass, and it HAS to be shared: the wind moves the vertex, so the shadow
        // pass and the depth pass must move it the same way or a bush parts company with its own
        // shadow. That was the whole reason this shader stopped inheriting those passes from the
        // fallback — see the note at the bottom.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EmissionColor;
            float  _DetailScale;
            float  _NormalStrength;
            float  _Smoothness;
            float  _Metallic;
            float  _WindStrength;
            float  _WindSpeed;
            float  _WindHeight;
        CBUFFER_END

        /// Wind (YT-78). Bends a plant; leaves everything else exactly where it stands.
        ///
        /// A VERTEX shader, and it has to be one. The ambience layer used to sway props by rotating
        /// their transforms (YT-56), and that had been doing nothing at all: every kit prop is marked
        /// static and BackyardDressing static-batches them, which bakes their vertices into world
        /// space and leaves the transform with nothing to push. The vertices are the only thing left
        /// that can move.
        ///
        /// Batching is also why the phase is hashed out of WORLD POSITION rather than taken from the
        /// object's transform: after the combine, every prop shares one identity object-to-world
        /// matrix, so where a plant stands is the only identity it has left.
        float3 Wind(float3 posWS)
        {
            if (_WindStrength <= 0.0) return posWS;   // uniform branch — most of the yard isn't a plant

            // ~1.4 m cells: one bush, one phase. Neighbours bend out of step, so a hedge reads as many
            // plants in one wind rather than as one object wobbling.
            float2 cell = floor(posWS.xz * 0.7);
            float phase = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453) * 6.2831;

            float t = _Time.y * _WindSpeed;

            // A gust, not a metronome. A pure sine at one frequency reads as machinery.
            float gust = 0.65 + 0.35 * sin(t * 0.31 + phase * 0.5);

            // Taller bends further, and the base stays put — a tuft of grass pivots at the soil.
            // Height above the LAWN (y = 0): after batching, object space IS world space, so this is
            // the only height there is, and it happens to be exactly the one worth having.
            float h = saturate(posWS.y / _WindHeight);
            float amp = _WindStrength * lerp(0.15, 1.0, h) * gust;

            posWS.x += sin(t + phase) * amp;
            posWS.z += cos(t * 0.83 + phase * 1.3) * amp * 0.6;
            return posWS;
        }
        ENDHLSL

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
                float3 positionWS : TEXCOORD0;   // where the vertex IS — lighting, shadows, fog
                float3 texWS      : TEXCOORD1;   // where it RESTS — the triplanar's material coordinate
                float3 normalWS   : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };

            Varyings SurfaceVert(Attributes IN)
            {
                Varyings OUT;

                // The RESTING position is the material coordinate, and that is not a subtlety — this
                // shader maps its texture from world position, so if the grain were sampled at the
                // BENT position it would slide across the leaves every time the plant moved. Sampling
                // where the vertex rests glues the texture to the plant, which is what a texture is
                // supposed to do.
                OUT.texWS = TransformObjectToWorld(IN.positionOS.xyz);

                OUT.positionWS = Wind(OUT.texWS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 SurfaceFrag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 wp = IN.texWS * _DetailScale;   // where the surface RESTS — see SurfaceVert

                // Triplanar weights, sharpened toward the dominant axis.
                //
                // n^4 by multiplication rather than pow(). This runs on every lit pixel of every
                // surface in the yard on the MOBILE tier (WebGL is what ships), so it is worth not
                // spending three pow() calls a pixel on it; a fixed exponent is also honest, because
                // nothing was ever going to tune this per material.
                //
                // It is NOT here because the shader was too slow. It wasn't: WebGL builds of the
                // commit before this ticket and of this one, run back to back in the same browser,
                // both hold ~57 fps. This is a cheaper way to get the same pixels, nothing more.
                float3 w = abs(N);
                w = w * w;
                w = w * w;
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

                // The relief is taken from the DOMINANT plane only — one fetch, not three.
                //
                // The albedo has to blend across all three or a rock cross-fades badly at its corners;
                // a slope does not. It is a small perturbation of a normal that is mostly the geometric
                // one, so where two planes are close to equal weight the two answers are close to equal
                // too, and nobody has ever seen the difference. Two fetches a pixel saved, over the
                // fence line, the shed and every prop in the yard, for output that is pixel-identical.
                //
                // Unpacked by hand rather than through UnpackNormalScale: these maps are generated at
                // runtime as plain linear RGB (StylizedTextures.NormalFor), not DXT5nm-swizzled, so the
                // helper would read the wrong channels on whichever platform took the other branch.
                // Same reasoning as StylizedGround.
                bool xDom = w.x >= w.y && w.x >= w.z;
                bool yDom = !xDom && w.y >= w.z;
                float2 nuv = xDom ? uvX : (yDom ? uvY : uvZ);

                float2 t = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, nuv).rg * 2.0 - 1.0;

                // That plane's 2D slope, rotated back into the world axes it was projected along, then
                // added to the geometric normal. Far more stable than reconstructing a tangent frame
                // and sign-correcting it, and for a stylised surface the difference is invisible — this
                // only has to make the light break, not carry a bump-mapped brick wall.
                float3 slope = xDom ? float3(0, t.y, t.x)
                             : (yDom ? float3(t.x, 0, t.y)
                                     : float3(t.x, t.y, 0));

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

        // The shadow the plant casts, bending with it (YT-78).
        //
        // This pass exists ONLY because of the wind. Without it the shader inherits ShadowCaster from
        // the fallback below — which knows nothing about the wind, so the bush swayed and its shadow
        // stayed nailed to the lawn. At a sway of a centimetre or two that is invisible and not worth
        // a pass; at a sway anyone can actually SEE, which is what the ticket asks for, a plant
        // sliding out of its own shadow is the first thing you notice. The wind had to be big enough
        // to read, so the shadow had to move with it.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;

                float3 positionWS = Wind(TransformObjectToWorld(IN.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Same reason, for the depth buffer: the SSAO added in YT-76 reads depth, and a bush whose
        // depth silhouette doesn't move would drag a smear of contact shading around with it.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionCS = TransformWorldToHClip(Wind(TransformObjectToWorld(IN.positionOS.xyz)));
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    // Still the safety net: if this SubShader is ever unsupported, a fence that falls back to plain
    // Lit is a look regression, and a magenta fence is a broken build (YT-58). It also still supplies
    // DepthNormals, which nothing in this project reads — the SSAO is configured off DEPTH, not
    // normals (Stage76RenderScaffold) — so it is left inherited rather than hand-written for a
    // consumer that doesn't exist.
    Fallback "Universal Render Pipeline/Lit"
}
