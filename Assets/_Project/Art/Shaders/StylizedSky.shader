Shader "MaxWorlds/StylizedSky"
{
    // The Backyard's sky (YT-76).
    //
    // A note on what this is actually for. The camera is pinned at a ~72° top-down pitch with a 40°
    // FOV, so the horizon sits about 30° above the top of the frame: THE PLAYER NEVER SEES THE SKY.
    // What they see, in every gap the yard's geometry doesn't cover, is the sky dome BELOW the
    // horizon — which in a default Unity scene is a flat grey nothing, and reads as the arena
    // floating in a void. So the half of this shader that earns its keep is the bottom half: a warm
    // ground haze that the fog colour matches, so the yard fades into distance instead of ending.
    //
    // The top half is still built properly — sun glow, cloud bands, a real gradient — because it is
    // nearly free (the sky is drawn once, behind everything, and only where nothing else covers it),
    // and because the day the camera pulls back or a cutscene tilts up, a grey void is a bug and
    // this is not.
    //
    // Everything is driven from BackyardLook, so the sky is a tunable and not a painted asset.

    Properties
    {
        _Zenith        ("Zenith", Color) = (0.30, 0.50, 0.80, 1)
        _Horizon       ("Horizon", Color) = (0.75, 0.82, 0.88, 1)
        _GroundHaze    ("Ground haze", Color) = (0.55, 0.58, 0.55, 1)
        _SunColor      ("Sun", Color) = (1, 0.92, 0.75, 1)
        _CloudColor    ("Cloud", Color) = (1, 0.98, 0.95, 1)

        _SunDir        ("Sun direction", Vector) = (0.4, 0.6, -0.7, 0)
        _SunSize       ("Sun size", Range(0.9, 0.9999)) = 0.995
        _SunGlow       ("Sun glow", Range(1, 256)) = 40
        _SunIntensity  ("Sun intensity", Range(0, 3)) = 1.1

        _CloudAmount   ("Cloud amount", Range(0, 1)) = 0.35
        _CloudScale    ("Cloud scale", Range(0.2, 8)) = 1.6
        _HorizonSoft   ("Horizon softness", Range(0.02, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS      : TEXCOORD0;
            };

            half4 _Zenith, _Horizon, _GroundHaze, _SunColor, _CloudColor;
            float4 _SunDir;
            half _SunSize, _SunGlow, _SunIntensity;
            half _CloudAmount, _CloudScale, _HorizonSoft;

            // Value noise. Two octaves is all a cloud band needs at this distance, and the sky is
            // the one surface in the game that must never cost anything: it is behind everything.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise21(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dirWS = v.positionOS.xyz;   // the skybox mesh IS the direction
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dirWS);

                // --- the dome ---------------------------------------------------------------
                // Up: horizon → zenith, biased so the gradient does its work near the horizon
                // rather than spreading evenly over a sky nobody looks at.
                float up = saturate(d.y);
                float3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(up, 0.55));

                // Down: horizon → haze. This is the half the player actually sees, in the gaps
                // past the yard, and it is deliberately close to the fog colour so a distant fence
                // dissolves into it instead of ending against a wall of sky.
                float down = saturate(-d.y);
                float3 ground = lerp(_Horizon.rgb, _GroundHaze.rgb, pow(down, 0.5));

                float3 col = lerp(ground, sky, smoothstep(-_HorizonSoft, _HorizonSoft, d.y));

                // --- the sun ----------------------------------------------------------------
                float3 sunDir = normalize(_SunDir.xyz);
                float cosA = dot(d, sunDir);

                // Glow first, then the disc on top of it: a disc with no glow is a sticker.
                float glow = pow(saturate(cosA), _SunGlow) * _SunIntensity;
                float disc = smoothstep(_SunSize, _SunSize + 0.002, cosA) * _SunIntensity;
                col += _SunColor.rgb * (glow * 0.55 + disc);

                // --- cloud bands ------------------------------------------------------------
                // Projected on the dome and stretched along X, so they read as bands rather than
                // as a noise field, and faded out at the horizon where the projection stretches
                // into mush.
                float2 uv = d.xz / max(d.y + 0.28, 0.12) * _CloudScale;
                float n = noise21(uv * float2(0.6, 1.15));
                n = n * 0.65 + noise21(uv * 2.3 + 11.7) * 0.35;

                float cloud = smoothstep(0.58, 0.86, n) * _CloudAmount * smoothstep(0.0, 0.35, d.y);
                col = lerp(col, _CloudColor.rgb, saturate(cloud));

                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    // The one thing a sky must never do is fail to draw. If this shader is unsupported anywhere,
    // Unity falls back to a flat colour rather than to the magenta error shader.
    Fallback "Skybox/Procedural"
}
