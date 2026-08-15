// Force Field shield shader (MV-391) — a translucent bubble with a genuine, view-dependent
// glowing rim, not a texture trick.
//
// Reuses StylizedCharacter.shader's exact Fresnel term (pow(1 - saturate(dot(N,V)), power) *
// strength) — the same "loud, tight rim" that already reads at the fixed ~72° camera on every
// character in the yard — but as its own transparent, unlit, alpha-blended pass instead of an
// additive term on top of a lit opaque body. A shield has no albedo to light: it is a thin,
// mostly-empty film that should show almost nothing at its centre (so Max stays visible inside
// it) and brighten only where the surface turns edge-on to the camera, which is what actually
// makes a sphere read as "a bubble" rather than "a coloured disc" from any angle the fixed rig
// can see it from.
Shader "MaxWorlds/ForceFieldShield"
{
    Properties
    {
        // Fill: alpha is the "subtle, mostly-transparent" half of the DECISION. Gameplay drives
        // this (and RimColor) through a MaterialPropertyBlock as the absorb budget depletes —
        // see ForceFieldBubble.SetFraction — so the whole ready-to-empty colour shift lives here,
        // not hardcoded in the shader.
        _BaseColor   ("Fill Color", Color) = (1, 1, 1, 0.14)
        _RimColor    ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower    ("Rim Power", Range(0.5, 8)) = 2.4
        _RimStrength ("Rim Strength", Range(0, 6)) = 2.6
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
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Same Fresnel term as StylizedCharacter's rim — see that shader for why this
                // exact curve reads as an edge rather than a wash at this camera's fixed pitch.
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;

                float3 color = _BaseColor.rgb + _RimColor.rgb * rim;
                // The fill alone stays subtle (low, near-constant alpha) so Max reads through the
                // centre of the bubble; the rim ADDS coverage on top of that, never replaces it,
                // so the edge brightens without the fill ever needing to be raised to compensate.
                float alpha = saturate(_BaseColor.a + rim);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
