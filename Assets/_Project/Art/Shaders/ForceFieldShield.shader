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
//
// MV-455 (Lee, 0.8.3 build review): the shipped result did not honour any of the above — the
// lattice read as a solid dome, not a shimmer. Four compounding bugs, all fixed in this pass:
//
//   1. Cull Off draws both hemispheres of the sphere at one screen pixel, so with
//      Blend SrcAlpha OneMinusSrcAlpha every fragment's alpha was composited TWICE: the visible
//      coverage was 1-(1-a)^2, not a. Fixed by emitting the exact per-fragment alpha `e` that
//      makes the double-blend land back on the INTENDED single-pass value `A`:
//          1 - (1-e)^2 = A   =>   e = 1 - sqrt(1-A)
//      (for small A this is ~A/2, i.e. "divide the alpha", but the exact inverse is used below so
//      it holds at the rim too, where A can approach 1). See `alpha` in Frag().
//   2. `_PanelSeamBoost` (was 1.3) let the hex seams alone add ~0.78 of alpha across the WHOLE
//      dome — a lattice, not an edge. Re-tuned to a hint-level default (see Properties) and the
//      body alpha (fill + seam glow) is now hard-clamped to `_AlphaCeiling` — the rim is exempt
//      and still adds its own coverage on top, per the "rim/seams ADD, never replace" contract.
//   3. `color` was an unclamped additive sum that blew past `_RimColor` into flat white wherever
//      rim+seam exceeded 1. Fixed with a `saturate()` on the glow term before it's added.
//   4. The old `pulse` term brightened the WHOLE lattice uniformly — a brightness breath, not a
//      shimmer. A travelling highlight band (`_ShimmerBandSpeed`/`_ShimmerBandWidth`), driven off
//      `_Time.y` against the sphere's own local Y axis, now carries the "alive" cue instead — the
//      eye reads motion sweeping the film rather than the whole dome breathing. `pulse` stays as a
//      much subtler secondary modulator on the seam glow only (Lee's slider, not the shimmer).
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
        _RimPower       ("Rim Power", Range(0.5, 8)) = 2.4
        _RimStrength    ("Rim Strength", Range(0, 6)) = 2.6

        // Hex facet panelling (MV-391 16 Aug DECISION) — panel count across the dome, seam
        // thickness in hex-cell units, and how much brighter a seam glows versus the rim.
        // _PanelSeamBoost re-tuned down from 1.3 for MV-455 — at 1.3 the seams alone solidified
        // the whole dome (see the file header maths); this is provisional, Lee's to dial further.
        _PanelScale     ("Panel Scale", Range(2, 20)) = 7
        _PanelSeamWidth ("Panel Seam Width", Range(0.02, 0.5)) = 0.1
        _PanelSeamBoost ("Panel Seam Boost", Range(0, 4)) = 0.35

        // Secondary "reactive/alive" cue — a much subtler modulator on the seam glow only, kept
        // small so it never dominates the travelling shimmer band below (MV-455 provisional).
        _PulseSpeed     ("Pulse Speed", Range(0, 4)) = 1.2
        _PulseStrength  ("Pulse Strength", Range(0, 1)) = 0.08

        // The shimmer itself (MV-455): a soft highlight band that sweeps the dome's surface along
        // its local Y axis over time, looping. Speed is full sweeps/second, Width is the band's
        // extent as a fraction of the axis (0..1). Provisional defaults, Lee's to dial.
        _ShimmerBandSpeed ("Shimmer Band Speed", Range(0, 2)) = 0.35
        _ShimmerBandWidth ("Shimmer Band Width", Range(0.02, 1)) = 0.18

        // Hard ceiling on the BODY alpha (fill + seam glow, before the rim adds its own coverage
        // on top) — MV-455 AC: "no more than ~0.35 composited at any fragment away from the rim".
        // Applied pre-compensation, i.e. this is the true composited value Max is seen through.
        _AlphaCeiling   ("Alpha Ceiling (body)", Range(0, 1)) = 0.35
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

                // Secondary brightness breath (kept small — see Properties) on the seam glow only.
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseStrength;
                float panelGlow = seam * _PanelSeamBoost * pulse;

                // The shimmer (MV-455, AC3): a soft highlight band that sweeps along the sphere's
                // own local Y axis over time and loops, so the eye reads motion travelling across
                // the film rather than the whole dome brightening at once. `nOS` (the normalized
                // object-space position, already computed above for the triplanar hex blend) is a
                // stable per-fragment surface coordinate independent of `_PanelScale`.
                float axisN = saturate(nOS.y * 0.5 + 0.5);
                float bandPhase = frac(_Time.y * _ShimmerBandSpeed);
                float bandDist = abs(axisN - bandPhase);
                bandDist = min(bandDist, 1.0 - bandDist); // wrap so the sweep loops seamlessly
                float shimmerBand = 1.0 - smoothstep(0.0, max(_ShimmerBandWidth, 1e-4), bandDist);

                // Glow term shared by colour and alpha, clamped once (AC2) so seams/shimmer can
                // never blow `color` past `_RimColor` into flat white.
                float glow = saturate(rim + panelGlow + shimmerBand * 0.5);
                float3 color = _BaseColor.rgb + _RimColor.rgb * glow;

                // Body alpha (fill + seam + shimmer, everything except the rim) is hard-ceilinged
                // (AC5) so the dome's interior stays "well below opaque" regardless of how the
                // seam/shimmer sliders are dialled; the rim is exempt and still adds its own
                // coverage on top, same "rim ADDS, never replaces the fill" contract as before.
                float bodyAlpha = min(_BaseColor.a + panelGlow * 0.6 + shimmerBand * 0.3, _AlphaCeiling);
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
