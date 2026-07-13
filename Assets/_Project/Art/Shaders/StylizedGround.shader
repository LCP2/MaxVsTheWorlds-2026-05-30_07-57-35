// Stylized ground shader (YT-69) — grass that reads as a surface, not as a fill.
//
// The floor was a correct green and still looked artificial. Two reasons, both structural:
//
//  1. It was ALBEDO ONLY. A painted mottle on a perfectly flat plane gives the key light nothing
//     to catch, so every pixel of the lawn shades identically and the eye reads "coloured fill",
//     not "ground". Relief is what makes a surface look like a surface, so the grass now carries a
//     normal map and the light actually breaks across it.
//
//  2. It was ONE TILE, REPEATED. Detail alone isn't enough — a yard that is the same everywhere is
//     still a slab. The variation across the yard (lush patches vs dry, sun vs shade) is generated
//     procedurally here in WORLD space, at a wavelength of metres, so it never repeats and never
//     lines up with the texture tiling.
//
// Why world-space UVs rather than the mesh's: the arena mesh gets rebuilt and reshaped by gameplay
// (YT-68 widened the lawn), which silently rescales its UVs and would rescale the grass with them.
// Deriving UVs from world XZ means the grass is a fixed physical size no matter what shape the yard
// becomes — the tech-art look can't be broken by a gameplay change to the mesh.
//
// The high frequencies live in the TEXTURES (which mip, and so don't shimmer at distance) and the
// low frequencies are procedural (too coarse to alias). That split is deliberate: procedural detail
// fine enough to read as blades would crawl and sparkle across the far half of a top-down screen.
Shader "MaxWorlds/StylizedGround"
{
    Properties
    {
        // The biome tint (YT-50). White by default — the palette is already on-model.
        _BaseColor       ("Base Color (biome tint)", Color) = (1,1,1,1)

        _BaseMap         ("Grass Albedo", 2D) = "white" {}
        _BumpMap         ("Grass Normal", 2D) = "bump" {}

        // Texture repeats per METRE of world. Not per-UV: see the header.
        _DetailScale     ("Detail Scale (tiles/m)", Range(0.05, 3)) = 0.55
        _NormalStrength  ("Normal Strength", Range(0, 2)) = 0.85

        [Header(Macro variation across the yard)]
        _MacroScale      ("Macro Scale (cycles/m)", Range(0.005, 0.5)) = 0.06
        _MacroStrength   ("Dry Patch Strength", Range(0, 1)) = 0.35
        _LushShade       ("Lush Patch Shade", Range(0.5, 1)) = 0.78
        _DryColor        ("Dry Grass Color", Color) = (0.36, 0.50, 0.20, 1)

        [Header(Clumping)]
        _ClumpScale      ("Clump Scale (cycles/m)", Range(0.1, 3)) = 0.65
        _ClumpDepth      ("Clump Depth", Range(0, 0.4)) = 0.12

        [Header(Wind YT78)]
        // METRES the blades lean at the peak of a gust. The lawn is a plane and cannot sway; this
        // leans the grass by moving where the blade texture is sampled. See GroundFrag.
        _WindStrength    ("Wind Blade Lean (m)", Range(0, 0.4)) = 0.06
        _WindSpeed       ("Wind Speed", Range(0, 4)) = 0.9
        _WindShimmer     ("Wind Shimmer", Range(0, 0.15)) = 0.045
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
            #pragma vertex GroundVert
            #pragma fragment GroundFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _DetailScale;
                float  _NormalStrength;
                float  _MacroScale;
                float  _MacroStrength;
                float  _LushShade;
                float4 _DryColor;
                float  _ClumpScale;
                float  _ClumpDepth;
                float  _WindStrength;
                float  _WindSpeed;
                float  _WindShimmer;
            CBUFFER_END

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);   SAMPLER(sampler_BumpMap);

            // Cheap 2D value noise. Only ever used at metre-scale wavelengths (the macro pass), so
            // it can be this crude without showing its lattice.
            float hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
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

            // Two octaves is all the macro pass needs: one for the big sweeps, one to stop them
            // looking like circles.
            float macroFbm(float2 p)
            {
                return noise21(p) * 0.667 + noise21(p * 2.17 + 19.3) * 0.333;
            }

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

            Varyings GroundVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 GroundFrag(Varyings IN) : SV_Target
            {
                // Rotated ~31° off the world axes. The grass texture has a directional grain, and a
                // grain running dead parallel to X/Z lines up with the arena's own edges and reads as
                // a grid — the eye finds an axis-aligned repeat far more readily than a skew one.
                const float2 rot = float2(0.857, 0.515);   // (cos, sin) of ~31 degrees
                float2 wp = IN.positionWS.xz;

                // --- wind across the lawn (YT-78) ---
                //
                // The lawn cannot sway. It is a flat plane with almost no vertices in it, so there is
                // nothing for a vertex shader to bend — and it is also, at a camera thirty metres up,
                // most of what is on the screen. Swaying the bushes alone left the yard technically
                // breathing and visibly dead: the plants that CAN bend cover a tenth of the frame.
                //
                // So the wind bends the BLADES instead. The grass here is a texture with a directional
                // grain, and shifting where that texture is sampled leans every blade in the yard a
                // few centimetres. Rolling that shift across the lawn as a long, slow gust — metres
                // wide, moving — is a wind crossing a field, and it costs two sines.
                //
                // The clump and macro passes below deliberately do NOT get this offset. They are the
                // lawn's own history — wear, shade, dry patches — and history does not blow about.
                const float2 windDir = float2(0.86, 0.51);
                float gustT = _Time.y * _WindSpeed;
                float gust = sin(dot(wp, windDir) * 0.42 - gustT) * 0.62
                           + sin(dot(wp, windDir) * 0.13 - gustT * 0.55 + 1.7) * 0.38;

                float2 bent = wp + windDir * gust * _WindStrength;
                float2 uv = float2(bent.x * rot.x - bent.y * rot.y,
                                   bent.x * rot.y + bent.y * rot.x) * _DetailScale;

                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;

                // Grass changes colour as it leans — a blade turning its edge to the sun is darker
                // than one turning its face. Tiny: this is the difference between a lawn that is
                // moving and a lawn with a shadow sliding over it.
                albedo *= 1.0 + gust * _WindShimmer;

                // Unpacked by hand rather than through UnpackNormalScale: that helper branches on
                // UNITY_NO_DXT5nm and expects a DXT5nm-swizzled map. This normal map is built at
                // runtime as plain linear RGB (StylizedTextures.GroundNormal), so the helper would
                // read the wrong channels on whichever platform took the other branch.
                float3 nTS = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv).rgb * 2.0 - 1.0;
                nTS.xy *= _NormalStrength;
                nTS = normalize(nTS);

                // The ground is horizontal, so its tangent frame can be built from world axes rather
                // than mesh tangents — which a CreatePrimitive'd arena box may not carry. The axis
                // guard matters: the far half of the floor is a flattened CUBE (BackyardPath's
                // "Arena Floor"), and on its vertical side faces a fixed cross-product axis would be
                // parallel to the normal and hand back a NaN.
                float3 N = normalize(IN.normalWS);
                float3 axis = abs(N.z) < 0.99 ? float3(0, 0, 1) : float3(1, 0, 0);
                float3 T = normalize(cross(N, axis));
                float3 B = cross(N, T);
                float3 normalWS = normalize(nTS.x * T + nTS.y * B + nTS.z * N);

                // Clump pass: the metre-ish scale the old code baked into the texture, moved out here
                // into world space. This is the structure the eye actually reads as "grass grows in
                // tufts" — and it is exactly the scale that betrays a tiled texture, so generating it
                // procedurally rather than tiling it is what stops the lawn looking like wallpaper.
                // Far too coarse to alias, unlike the blade detail, which is why that stays a texture.
                float clump = noise21(wp * _ClumpScale);
                albedo *= lerp(1.0 - _ClumpDepth, 1.0 + _ClumpDepth, clump);

                // Macro pass: metre-scale drift across the yard. Lush, shaded turf at one end of the
                // range; dry, sun-bleached turf at the other. This is what stops the lawn reading as
                // one uniform slab, and it is deliberately independent of the texture tiling so the
                // two never beat against each other.
                float macro = saturate(macroFbm(wp * _MacroScale));
                float3 lush = albedo * _LushShade;
                float3 dry = lerp(albedo, _DryColor.rgb, _MacroStrength);
                albedo = lerp(lush, dry, macro);

                albedo *= _BaseColor.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 ambient = SampleSH(normalWS);

                // Matte lambert + ambient, matching the character shader (YT-57). Grass is not shiny;
                // a specular term here would read as wet plastic.
                float3 lit = albedo * (ambient + mainLight.color * ndotl * mainLight.shadowAttenuation);

                return half4(lit, 1);
            }
            ENDHLSL
        }
    }

    // Supplies ShadowCaster/DepthOnly/DepthNormals, and is the safety net if this SubShader is ever
    // unsupported — a ground that falls back to plain Lit is a look regression, a magenta ground is
    // a broken build (YT-58).
    Fallback "Universal Render Pipeline/Lit"
}
