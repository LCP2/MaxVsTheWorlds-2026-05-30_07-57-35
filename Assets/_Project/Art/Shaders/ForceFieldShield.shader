// Force Field shield shader (MV-391) — a translucent BLUE/CYAN energy-shield dome with a
// hexagonal/faceted panel pattern and a genuine, view-dependent glowing rim, matching the
// SG2/SG3 reference look from Lee's 16 Aug DECISION (supersedes the earlier plain-white ring).
//
// Reuses StylizedCharacter.shader's exact Fresnel term (pow(1 - saturate(dot(N,V)), power) *
// strength) — the same "loud, tight rim" that already reads at the fixed ~72° camera on every
// character in the yard — but as its own transparent, unlit, alpha-blended pass instead of an
// additive term on top of a lit opaque body. A shield has no albedo to light: it is a thin,
// mostly-empty film that should show almost nothing at its centre (so Max stays visible inside
// it) and brighten only where the surface turns edge-on to the camera or crosses a panel seam.
//
// The 16 Aug DECISION's specific complaint was that even a correctly-transparent Fresnel sphere
// still just reads as "a circle around Max" from the fixed top-down camera, because a sphere seen
// from near-overhead silhouettes as a ring with almost no surface detail in between. The
// hex-panel pattern below is what breaks that: a triplanar-blended hex lattice sampled directly
// off the sphere's own local surface (object space, not UV — the primitive sphere's poles would
// otherwise pinch a UV-based grid), with panel seams boosted the same way the rim is and panel
// interiors left close to the plain fill. Seen from any angle — including nearly straight down —
// the seams trace visible facet lines across the dome, which is what makes it read as a faceted
// 3D shell rather than a flat tinted disc.
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
        _RimPower       ("Rim Power", Range(0.5, 8)) = 2.4
        _RimStrength    ("Rim Strength", Range(0, 6)) = 2.6

        // Hex facet panelling (MV-391 16 Aug DECISION) — panel count across the dome, seam
        // thickness in hex-cell units, and how much brighter a seam glows versus the rim.
        _PanelScale     ("Panel Scale", Range(2, 20)) = 7
        _PanelSeamWidth ("Panel Seam Width", Range(0.02, 0.5)) = 0.1
        _PanelSeamBoost ("Panel Seam Boost", Range(0, 4)) = 1.3

        // "Reactive/alive" cue (AC6) — subtle, or Max becomes hard to see inside his own shield.
        _PulseSpeed     ("Pulse Speed", Range(0, 4)) = 1.2
        _PulseStrength  ("Pulse Strength", Range(0, 1)) = 0.15
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
                float  _PanelScale;
                float  _PanelSeamWidth;
                float  _PanelSeamBoost;
                float  _PulseSpeed;
                float  _PulseStrength;
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

            // ---- Hex lattice (pointy-top, Red Blob Games axial convention, cell size = 1) ----

            float2 AxialToPixel(float2 axial)
            {
                float x = 1.7320508 * axial.x + 0.8660254 * axial.y; // sqrt(3)*q + sqrt(3)/2*r
                float y = 1.5 * axial.y;
                return float2(x, y);
            }

            float2 PixelToAxial(float2 p)
            {
                float q = 0.5773503 * p.x - 0.3333333 * p.y; // sqrt(3)/3 * x - 1/3 * y
                float r = 0.6666667 * p.y;                    // 2/3 * y
                return float2(q, r);
            }

            float2 AxialRound(float2 axial)
            {
                float3 cube = float3(axial.x, -axial.x - axial.y, axial.y);
                float3 rc = round(cube);
                float3 diff = abs(rc - cube);
                if (diff.x > diff.y && diff.x > diff.z) rc.x = -rc.y - rc.z;
                else if (diff.y > diff.z)               rc.y = -rc.x - rc.z;
                // else rc.z is already the most-correct component to keep.
                return float2(rc.x, rc.z);
            }

            // 0 deep in a panel interior .. 1 right on a cell seam. Compares the distance to the
            // nearest hex-lattice point against the second-nearest across the surrounding 3x3
            // neighbourhood — the gap between the two shrinks to zero exactly on a Voronoi/hex
            // cell boundary, which traces the true seam regardless of the lattice's orientation
            // (no separately-oriented hexagon SDF to keep in sync with the axial math above).
            float HexSeamFactor(float2 p, float seamWidth)
            {
                float2 nearestAxial = AxialRound(PixelToAxial(p));
                float minD = 1e5;
                float secondD = 1e5;
                [unroll]
                for (int dq = -1; dq <= 1; dq++)
                {
                    [unroll]
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        float2 candidate = nearestAxial + float2(dq, dr);
                        float d = distance(p, AxialToPixel(candidate));
                        if (d < minD) { secondD = minD; minD = d; }
                        else if (d < secondD) { secondD = d; }
                    }
                }
                float gap = secondD - minD;
                return 1.0 - smoothstep(0.0, seamWidth, gap);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Same Fresnel term as StylizedCharacter's rim — see that shader for why this
                // exact curve reads as an edge rather than a wash at this camera's fixed pitch.
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;

                // Triplanar-blend the hex pattern off the sphere's own local surface (object
                // space) so it wraps the whole dome with no pole-pinch, the way a UV-based grid
                // would have on this primitive sphere.
                float3 nOS = normalize(IN.positionOS);
                float3 blendW = abs(nOS) / max(abs(nOS.x) + abs(nOS.y) + abs(nOS.z), 1e-5);
                float3 pOS = IN.positionOS * _PanelScale;

                float seamXY = HexSeamFactor(pOS.xy, _PanelSeamWidth);
                float seamYZ = HexSeamFactor(pOS.yz, _PanelSeamWidth);
                float seamXZ = HexSeamFactor(pOS.xz, _PanelSeamWidth);
                float seam = seamXY * blendW.z + seamYZ * blendW.x + seamXZ * blendW.y;

                // Reactive/alive shimmer (AC6) — modulates the panel glow only, kept small enough
                // that it never dims the fill/rim enough to hide Max inside the bubble.
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseStrength;
                float panelGlow = seam * _PanelSeamBoost * pulse;

                float3 color = _BaseColor.rgb + _RimColor.rgb * (rim + panelGlow);
                // The fill alone stays subtle (low, near-constant alpha) so Max reads through the
                // centre of the bubble; the rim and panel seams ADD coverage on top of that, never
                // replace it, so the edges/seams brighten without the fill needing to be raised.
                float alpha = saturate(_BaseColor.a + rim + panelGlow * 0.6);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
