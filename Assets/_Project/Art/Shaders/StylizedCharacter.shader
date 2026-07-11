// Stylized character shader (YT-57) — outline, rim light, dissolve.
//
// Hand-written ShaderLab rather than Shader Graph: a .shader file is code. It diffs, it reviews,
// and it lives in the repo like everything else — a Shader Graph asset is a binary-ish blob that
// only opens in the editor, which is exactly the hand-authoring the code-driven rule exists to
// avoid.
//
// It keeps _BaseColor and _EmissionColor, because gameplay tints these renderers through a
// MaterialPropertyBlock (hit flashes, wind-up tells, factory damage state). Break those property
// names and every tell in the game goes silent.
Shader "MaxWorlds/StylizedCharacter"
{
    Properties
    {
        _BaseColor      ("Base Color", Color) = (1,1,1,1)
        _EmissionColor  ("Emission Color", Color) = (0,0,0,0)

        [Header(Rim)]
        _RimColor       ("Rim Color", Color) = (1, 0.86, 0.62, 1)
        _RimPower       ("Rim Power", Range(0.5, 8)) = 3.4
        _RimStrength    ("Rim Strength", Range(0, 3)) = 0.55

        [Header(Outline)]
        _OutlineColor   ("Outline Color", Color) = (0.05, 0.05, 0.06, 1)
        // Screen-space width (fraction of clip space), NOT metres. An object-space extrusion looks
        // right on a test capsule up close and then vanishes at the real camera distance, because a
        // 2cm shell is sub-pixel from up there. This keeps the line a constant thickness on screen
        // whatever the camera does.
        _OutlineWidth   ("Outline Width (screen)", Range(0, 0.02)) = 0.009

        [Header(Dissolve)]
        _Dissolve       ("Dissolve", Range(0, 1)) = 0
        _EdgeWidth      ("Dissolve Edge Width", Range(0.001, 0.3)) = 0.09
        _EdgeColor      ("Dissolve Edge Color", Color) = (1, 0.62, 0.18, 1)
        _NoiseScale     ("Dissolve Noise Scale", Range(1, 30)) = 9
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EmissionColor;
            float4 _RimColor;
            float  _RimPower;
            float  _RimStrength;
            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _Dissolve;
            float  _EdgeWidth;
            float4 _EdgeColor;
            float  _NoiseScale;
        CBUFFER_END

        // Cheap value noise. The dissolve only needs a blotchy mask, and a procedural one means no
        // texture to author, import, or forget to include in the build.
        float hash31(float3 p)
        {
            p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float noise31(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float n000 = hash31(i + float3(0,0,0));
            float n100 = hash31(i + float3(1,0,0));
            float n010 = hash31(i + float3(0,1,0));
            float n110 = hash31(i + float3(1,1,0));
            float n001 = hash31(i + float3(0,0,1));
            float n101 = hash31(i + float3(1,0,1));
            float n011 = hash31(i + float3(0,1,1));
            float n111 = hash31(i + float3(1,1,1));

            float nx00 = lerp(n000, n100, f.x);
            float nx10 = lerp(n010, n110, f.x);
            float nx01 = lerp(n001, n101, f.x);
            float nx11 = lerp(n011, n111, f.x);

            return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
        }
        ENDHLSL

        // --- Outline: inverted hull. Push the shell out along its normals and draw only the back
        // faces, so what's left is a rim of colour around the silhouette. It costs one extra pass
        // and no depth texture, which is why it survives on a mobile/WebGL target.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionWS = positionWS;
                float4 positionCS = TransformWorldToHClip(positionWS);

                // Push the silhouette outward in SCREEN space: project the normal into view space,
                // take its 2D direction, and offset in clip space scaled by w. Multiplying by w is
                // what cancels the perspective divide, so the line holds the same pixel width near
                // and far.
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float2 dir = normalize(normalVS.xy + 1e-6);
                positionCS.xy += dir * _OutlineWidth * positionCS.w;

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                // The outline dissolves with the body, or a dead enemy leaves a floating shell.
                float n = noise31(IN.positionWS * _NoiseScale);
                clip(n - _Dissolve);
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex LitVert
            #pragma fragment LitFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
            };

            Varyings LitVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 LitFrag(Varyings IN) : SV_Target
            {
                float noise = noise31(IN.positionWS * _NoiseScale);
                clip(noise - _Dissolve);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndotl = saturate(dot(N, mainLight.direction));
                float shadow = mainLight.shadowAttenuation;

                // Deliberately simple, banded-ish lambert plus ambient: stylised, not photoreal.
                float3 ambient = SampleSH(N);
                float3 lit = _BaseColor.rgb * (ambient + mainLight.color * ndotl * shadow);

                // Rim light — the readability workhorse. At a fixed top-down angle a character's
                // silhouette is most of what you can see, so lighting the edge is what separates it
                // from the ground.
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower);
                lit += _RimColor.rgb * rim * _RimStrength;

                lit += _EmissionColor.rgb;

                // The dissolve's leading edge glows, so a death reads as burning away rather than
                // as the model quietly developing holes.
                float edge = 1.0 - saturate((noise - _Dissolve) / max(_EdgeWidth, 1e-4));
                edge = saturate(edge) * step(0.0001, _Dissolve);
                lit = lerp(lit, _EdgeColor.rgb, edge);

                return half4(lit, 1);
            }
            ENDHLSL
        }

        // Shadow casting, so these characters still drop the shadows the YT-49 lighting relies on
        // for depth.
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                // A dissolving body must stop casting the parts of itself that have burned away.
                float n = noise31(IN.positionWS * _NoiseScale);
                clip(n - _Dissolve);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
