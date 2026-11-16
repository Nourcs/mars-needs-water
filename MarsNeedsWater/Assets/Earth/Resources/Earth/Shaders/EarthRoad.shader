// Roads, rail lines, pavements and crossings of the mini Earth (meshes made by Core/RoadLook.cs).
// Everything is drawn from numbers on each vertex, no textures: asphalt with grain, patches, repairs, cracks and
// worn wheel tracks; painted lines by the local rules; paving, slabs, gravel, dirt with ruts, steps; ballast,
// sleepers and rails. All of it is driven by noise in world position and per-road seeds, so nothing repeats.
//   uv0  x: metres across from the centre line (left +), y: metres along
//   uv1  x: half-width of the carriageway, y: metres clear of the nearest junction, z: kind, w: line flags
//   uv2  x: lanes, y: lane width, z: random seed 0..1, w: stop line side (+1 left, -1 right, 2 both, 0 none)
Shader "MarsNeedsWater/EarthRoad"
{
    Properties
    {
        _DetailFar ("Fine detail fades out by (m)", Float) = 120
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _DetailFar;
        CBUFFER_END

        float4 _GroundOffset;          // set by EarthGround.Shift: keeps world noise in place when the world re-centres

        // ==== ROAD SURFACE BEGIN (shared with the preview tool, tools/world/roads; keep it plain) ====
        #define K_ASPHALT 0
        #define K_PAVERS 1
        #define K_CONCRETE 2
        #define K_DIRT 3
        #define K_GRAVEL 4
        #define K_STEPS 5
        #define K_CYCLE 6
        #define K_RAIL 7
        #define K_ZEBRA 8
        #define K_SIDEWALK 9
        #define K_KERB 10

        #define F_EDGE 1
        #define F_CDASH 2
        #define F_CSOLID 4
        #define F_CDOUBLE 8
        #define F_YELLOW 16
        #define F_ONEWAY 32
        #define F_YLEFT 64
        #define F_RUTS 128
        #define F_LONGDASH 256
        #define F_LANES 512
        #define F_RED 1024

        uint RHash(uint2 v)
        {
            uint h = v.x * 0x8da6b343u ^ v.y * 0xd8163841u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return h;
        }

        float RRand(float2 c, float s)
        {
            int2 ic = int2(floor(c));
            return float(RHash(uint2(ic + int2(32768, 32768)) + uint2(uint(s * 7919.0), uint(s * 104729.0))) & 0xffffu) / 65535.0;
        }

        float RNoise(float2 p, float s)
        {
            float2 i = floor(p);
            float2 f = p - i;
            f = f * f * (3.0 - 2.0 * f);
            float a = RRand(i, s), b = RRand(i + float2(1.0, 0.0), s), c = RRand(i + float2(0.0, 1.0), s), d = RRand(i + float2(1.0, 1.0), s);
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        float RFbm(float2 p, float s)
        {
            return RNoise(p, s) * 0.5 + RNoise(p * 2.03 + float2(17.1, 5.3), s + 1.0) * 0.3 + RNoise(p * 4.1 + float2(3.7, 11.9), s + 2.0) * 0.2;
        }

        // coverage of a band |x - c| < hw, smoothed over a pixel; thin lines fade instead of flickering
        float RBand(float x, float c, float hw, float aa)
        {
            return saturate((hw - abs(x - c)) / aa + 0.5) * saturate(hw * 2.0 / aa);
        }

        // dashes along v: 'on' metres painted every 'period' metres
        float RDash(float v, float period, float on, float aa)
        {
            float t = v / period;
            float f = t - floor(t);
            float e = on / period * 0.5;
            float d = abs(f - e);
            return saturate((e - d) * period / aa + 0.5);
        }

        float3 RLin(float3 c)
        {
        #if defined(UNITY_COLORSPACE_GAMMA)
            return c;
        #else
            return c * c * (c * 0.2848 + 0.7152);       // close to the sRGB curve
        #endif
        }

        // What the surface is at one spot. clipv < 0: nothing here (gaps between zebra stripes).
        void RoadSurface(float2 sv, float hw, float dj, float kindF, float flagsF, float lanes, float laneW, float seed, float stopSide,
                         float2 world, float detail, out float3 albedo, out float smoothness, out float2 bump, out float clipv)
        {
            int kind = int(kindF + 0.5);
            int flags = int(flagsF + 0.5);
            float s = sv.x, v = sv.y;
            float aaS = max(fwidth(s), 1e-4), aaV = max(fwidth(v), 1e-4);
            float2 aaW = max(fwidth(world), float2(1e-4, 1e-4));
            float fine = detail * saturate(0.05 / max(aaW.x, aaW.y));       // grain only where pixels are small enough to show it
            clipv = 1.0;
            bump = float2(0.0, 0.0);
            smoothness = 0.12;

            // ---- base surfaces
            float grain = RNoise(world * 23.0, 1.0) - 0.5;
            float grain2 = RNoise(world * 61.0, 2.0) - 0.5;
            float mottle = RFbm(world * 1.3, 3.0) - 0.5;
            float big = RFbm(world * 0.06, 4.0) - 0.5;
            float3 col;

            if (kind == K_ASPHALT || kind == K_CYCLE || kind == K_ZEBRA)
            {
                // each road has its own age: older is paler and greyer
                float age = RRand(float2(seed * 977.0, 3.0), 5.0);
                float tone = lerp(0.20, 0.33, age) + big * 0.06 + mottle * 0.035 + (grain * 0.05 + grain2 * 0.04) * fine;
                col = float3(tone, tone, tone * 1.04);
                if (kind == K_CYCLE && (flags & F_RED) != 0) col = float3(tone * 1.9, tone * 0.85, tone * 0.78);
                // repairs: fresher, darker patches here and there, with ragged edges and a sealed seam
                float cellV = floor(v / 9.0);
                float pr = RRand(float2(cellV, seed * 311.0), 6.0);
                if (pr < 0.09 && kind != K_ZEBRA)
                {
                    float a0 = RRand(float2(cellV, seed * 311.0), 7.0), a1 = RRand(float2(cellV, seed * 311.0), 8.0);
                    float lo = lerp(-hw, hw * 0.2, a0), hi = min(hw, lo + lerp(0.3, 1.4, a1) * hw);
                    float vv = v / 9.0 - cellV;
                    float wob = (RNoise(world * 3.0, 21.0) - 0.5) * 0.12;
                    float dS = min(s - lo, hi - s) + wob, dV = (lerp(0.12, 0.4, a0) - abs(vv - 0.5)) * 9.0 + wob;
                    float d = min(dS, dV);
                    float inside = saturate(d / max(aaS, 0.01) + 0.5);
                    float seam = saturate(1.0 - abs(d) / max(aaS * 1.5, 0.012)) * detail;
                    col = lerp(col, col * lerp(0.74, 0.88, a1), inside);
                    col *= 1.0 - seam * 0.3;
                }
                // wheel tracks: a little darker and smoother in each lane, and an oil line down its middle
                if (lanes > 0.5 && laneW > 0.01)
                {
                    float lp = (s / laneW) + lanes * 0.5;
                    float lf = lp - floor(lp);
                    float tracks = smoothstep(0.16, 0.0, abs(lf - 0.25)) + smoothstep(0.16, 0.0, abs(lf - 0.75));
                    float oil = smoothstep(0.09, 0.0, abs(lf - 0.5)) * RNoise(float2(v * 0.7, lp * 5.0), 9.0);
                    col *= 1.0 - tracks * 0.035 - oil * 0.09;
                    smoothness += tracks * 0.08;
                }
                // grime and loose grit along the edges
                float edgeD = hw - abs(s);
                col *= lerp(0.82, 1.0, saturate(edgeD / 0.25));
                // cracks in old surfaces: thin wandering lines, only in some stretches
                float cn = RFbm(world * 0.9, seed * 13.0 + 20.0) - 0.5;
                float cw = max(fwidth(cn), 1e-4);
                float crack = saturate(1.0 - abs(cn) / (cw * 1.2)) * saturate(0.012 / max(aaW.x, 0.004));
                crack *= saturate((age - 0.4) * 3.0) * smoothstep(0.62, 0.7, RNoise(world * 0.12, 10.0));
                col *= 1.0 - crack * 0.35 * detail;
                bump = float2(RNoise(world * 23.0 + 0.3, 1.0) - grain - 0.5, RNoise(world * 23.0 + float2(0.0, 0.3), 1.0) - grain - 0.5) * 0.6 * fine;
            }
            else if (kind == K_PAVERS)
            {
                // setts in a running bond
                float2 st = float2(s * 7.0, v * 4.5);
                float row = floor(st.y);
                st.x += RRand(float2(row, seed * 51.0), 11.0) * 0.5 + row * 0.5;
                float2 cell = floor(st), f = st - cell;
                float r = RRand(cell + row * 131.0, 12.0);
                float tone = lerp(0.36, 0.55, r) + mottle * 0.06 + grain * 0.04 * fine;
                col = float3(tone * 1.02, tone * 0.98, tone * 0.93);
                float2 aa = float2(aaS * 7.0, aaV * 4.5);
                float joint = max(1.0 - saturate(min(f.x, 1.0 - f.x) / max(aa.x, 0.06)), 1.0 - saturate(min(f.y, 1.0 - f.y) / max(aa.y, 0.06)));
                col *= 1.0 - joint * 0.45 * saturate(0.3 / max(aa.x, aa.y));
                bump = (f - 0.5) * 0.25 * fine;
            }
            else if (kind == K_CONCRETE || kind == K_SIDEWALK || kind == K_KERB)
            {
                float tone = 0.58 + big * 0.06 + mottle * 0.05 + grain * 0.04 * fine;
                col = float3(tone, tone * 0.98, tone * 0.94);
                if (kind == K_SIDEWALK && s < 0.06) col = float3(0.66, 0.65, 0.63) + mottle * 0.04;      // the kerb stone on top
                else if (kind != K_KERB)
                {
                    // slabs with joints, each a slightly different shade
                    float2 st = float2(s / max(hw * 0.5, 0.2), v / 0.55);
                    float2 cell = floor(st), f = st - cell;
                    col *= lerp(0.92, 1.06, RRand(cell, seed * 7.0 + 13.0));
                    float2 aa = float2(aaS / max(hw * 0.5, 0.2), aaV / 0.55);
                    float joint = max(1.0 - saturate(min(f.x, 1.0 - f.x) / max(aa.x, 0.025)), 1.0 - saturate(min(f.y, 1.0 - f.y) / max(aa.y, 0.025)));
                    col *= 1.0 - joint * 0.3 * saturate(0.25 / max(aa.x, aa.y));
                    // gum, stains and wet patches
                    col *= 1.0 - smoothstep(0.74, 0.8, RFbm(world * 6.0, 14.0)) * 0.1 * detail;
                }
                else col *= 0.92;
            }
            else if (kind == K_DIRT || kind == K_GRAVEL)
            {
                float tone = 0.42 + big * 0.08 + mottle * 0.08 + grain * 0.08 * fine;
                col = kind == K_DIRT ? float3(tone * 1.12, tone * 0.93, tone * 0.68) : float3(tone * 1.02, tone * 0.98, tone * 0.9);
                if (kind == K_GRAVEL) col *= 0.9 + (RNoise(world * 90.0, 15.0) - 0.5) * 0.5 * fine;
                if ((flags & F_RUTS) != 0)
                {
                    float rut = RBand(abs(s), hw * 0.42, hw * 0.13, aaS);
                    float mid = RBand(s, 0.0, hw * 0.16, aaS) * step(0.35, RFbm(world * 1.5, 16.0));
                    col *= 1.0 - rut * 0.18;
                    col = lerp(col, float3(0.25, 0.33, 0.16) * (0.8 + mottle), mid * 0.75 * (kind == K_DIRT ? 1.0 : 0.6));
                }
                // ragged edges blending into the ground
                col *= lerp(0.85, 1.0, saturate((hw - abs(s)) / (hw * 0.3)));
                bump = float2(grain, grain2) * 0.9 * fine;
            }
            else if (kind == K_STEPS)
            {
                float t = v / 0.14;
                float f = t - floor(t);
                float tone = 0.5 + mottle * 0.05 + grain * 0.04 * fine;
                col = float3(tone, tone * 0.97, tone * 0.92) * lerp(0.7, 1.0, smoothstep(0.0, 0.25, f));
                bump = float2(0.0, (f < 0.2 ? 1.0 : 0.0)) * 0.6;
            }
            else // K_RAIL: ballast, sleepers, two rails
            {
                float stone = RNoise(world * 70.0, 17.0);
                float tone = 0.36 + big * 0.05 + mottle * 0.06 + (stone - 0.5) * 0.18 * fine;
                col = float3(tone * 1.05, tone, tone * 0.93);
                float gauge = 0.215, sleeperHalf = 0.39;
                float wood = step(0.5, seed);
                float sl = RDash(v, 0.17, 0.065, aaV) * RBand(s, 0.0, sleeperHalf, aaS);
                float3 slc = wood > 0.5 ? float3(0.27, 0.2, 0.15) : float3(0.5, 0.49, 0.47);
                col = lerp(col, slc * (0.85 + mottle * 0.3), sl);
                float rails = RBand(s, gauge, 0.018, aaS) + RBand(s, -gauge, 0.018, aaS);
                float head = RBand(s, gauge, 0.007, aaS) + RBand(s, -gauge, 0.007, aaS);
                col = lerp(col, float3(0.24, 0.2, 0.17), saturate(rails));
                col = lerp(col, float3(0.7, 0.7, 0.72), saturate(head));
                smoothness = lerp(0.08, 0.8, saturate(head));
                bump = float2(stone - 0.5, RNoise(world * 70.0 + 0.5, 17.0) - 0.5) * 0.8 * fine;
            }

            // ---- painted lines on carriageways
            float paint = 0.0;
            float3 paintCol = float3(0.86, 0.86, 0.84);
            float yellowPaint = 0.0;
            if (kind == K_ASPHALT || kind == K_CYCLE)
            {
                float lw = 0.03;                               // half-width of a line: 20 cm real at road width scale, a touch wide to read
                float clear = saturate(dj / 0.35);             // lines stop short of junctions
                float edgeOff = min(hw - 0.12, lanes * laneW * 0.5 + 0.05);
                bool americas = (flags & F_YELLOW) != 0;
                float per = (flags & F_LONGDASH) != 0 ? (americas ? 3.0 : 4.5) : (americas ? 3.0 : 2.25);
                float on = (flags & F_LONGDASH) != 0 ? (americas ? 0.75 : 1.5) : 0.75;
                if ((flags & F_EDGE) != 0 && edgeOff > 0.3)
                {
                    float left = RBand(s, edgeOff, lw, aaS), right = RBand(s, -edgeOff, lw, aaS);
                    if ((flags & F_YLEFT) != 0) { yellowPaint = max(yellowPaint, left * clear); left = 0.0; }
                    paint = max(paint, (left + right) * clear);
                }
                if ((flags & F_CDOUBLE) != 0)
                {
                    float c2 = RBand(s, lw * 2.2, lw, aaS) + RBand(s, -lw * 2.2, lw, aaS);
                    if (americas) yellowPaint = max(yellowPaint, c2 * clear); else paint = max(paint, c2 * clear);
                }
                else if ((flags & F_CSOLID) != 0)
                {
                    float c1 = RBand(s, 0.0, lw, aaS);
                    if (americas) yellowPaint = max(yellowPaint, c1 * clear); else paint = max(paint, c1 * clear);
                }
                else if ((flags & F_CDASH) != 0)
                {
                    float c1 = RBand(s, 0.0, lw, aaS) * RDash(v, per, on, aaV);
                    if (americas) yellowPaint = max(yellowPaint, c1 * clear); else paint = max(paint, c1 * clear);
                }
                if ((flags & F_LANES) != 0 && laneW > 0.01)
                {
                    // white dashes between lanes going the same way
                    float firstS = (flags & F_ONEWAY) != 0 ? -lanes * laneW * 0.5 : 0.0;
                    float lp = (s - firstS) / laneW;
                    float k = floor(lp + 0.5);
                    bool divider = (flags & F_ONEWAY) != 0 ? (k >= 1.0 && k <= lanes - 1.0) : (abs(k) >= 1.0 && abs(k) <= lanes * 0.5 - 1.0);
                    if (divider) paint = max(paint, RBand(s, firstS + k * laneW, lw * 0.9, aaS) * RDash(v, per, on, aaV) * clear);
                }
                if (stopSide != 0.0 && dj > -0.05 && dj < 0.6)
                {
                    float sideOk = stopSide > 1.5 ? 1.0 : saturate((s * stopSide) / aaS + 0.5);
                    float st = RBand(dj, 0.12, 0.06, max(fwidth(dj), 1e-4)) * sideOk * RBand(s, 0.0, edgeOff > 0.3 ? edgeOff : hw - 0.1, aaS);
                    paint = max(paint, st);
                }
                if (kind == K_CYCLE)
                {
                    float c1 = RBand(s, 0.0, lw * 0.7, aaS) * RDash(v, 1.2, 0.4, aaV);
                    paint = max(paint, c1 * clear);
                }
            }
            if (kind == K_ZEBRA)
            {
                // white bars parallel to the traffic, only on the street (dj < 0 is off it)
                float bars = RDash(v, 0.3, 0.15, aaV) * RBand(s, 0.0, hw - 0.02, aaS) * saturate(dj * 4.0 + 0.5);
                clipv = bars - 0.5;
                paint = 1.0;
            }
            // paint wears: thinner in the wheel tracks, chipped all over
            float wear = 0.7 + 0.3 * RNoise(world * 4.0, 18.0);
            wear *= 1.0 - step(0.78, RNoise(world * 11.0, 19.0)) * 0.6 * fine;
            float3 yellow = float3(0.88, 0.66, 0.16);
            col = lerp(col, paintCol * (0.9 + mottle * 0.1), saturate(paint * wear));
            col = lerp(col, yellow * (0.9 + mottle * 0.1), saturate(yellowPaint * wear));
            smoothness = lerp(smoothness, 0.32, saturate((paint + yellowPaint) * wear));

            albedo = RLin(saturate(col));
        }
        // ==== ROAD SURFACE END ====

        struct RAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv0 : TEXCOORD0;
            float4 uv1 : TEXCOORD1;
            float4 uv2 : TEXCOORD2;
        };
        ENDHLSL

        Pass
        {
            Name "Road"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
                float fog : TEXCOORD5;
            };

            Varyings vert(RAttributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv0 = v.uv0; o.uv1 = v.uv1; o.uv2 = v.uv2;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 nGeo = normalize(i.normalWS);
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                float detail = 1.0 - smoothstep(_DetailFar * 0.5, _DetailFar, dist);
                float2 world = i.positionWS.xz + _GroundOffset.xy;
                float3 albedo; float smoothness; float2 bump; float clipv;
                RoadSurface(i.uv0, i.uv1.x, i.uv1.y, i.uv1.z, i.uv1.w, i.uv2.x, i.uv2.y, i.uv2.z, i.uv2.w, world, detail, albedo, smoothness, bump, clipv);
                clip(clipv);

                // bump on the slope of the road (east = world x, north = world z)
                float3 T = normalize(float3(1, 0, 0) - nGeo * nGeo.x);
                float3 B = cross(T, nGeo);
                if (dot(B, float3(0, 0, 1)) < 0) B = -B;
                float3 n = normalize(nGeo + (T * bump.x + B * bump.y) * 0.35);

                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light sun = GetMainLight(sc);
                half shadow = sun.shadowAttenuation;
                half ndl = saturate(dot(n, sun.direction));
                half3 sky = SampleSH(n);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 H = normalize(sun.direction + V);
                half spec = pow(saturate(dot(n, H)), exp2(10.0 * smoothness + 1.0)) * smoothness * 0.35;
                half3 col = albedo * (sun.color * ndl * shadow + sky) + sun.color * spec * shadow;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Vo { float4 positionCS : SV_POSITION; float2 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            Vo vert(RAttributes v) { Vo o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv0 = v.uv0; o.uv1 = v.uv1; return o; }
            half frag(Vo i) : SV_Target
            {
                // zebra crossings: only the white bars are there
                if (i.uv1.z > 7.5 && i.uv1.z < 8.5)
                {
                    float t = i.uv0.y / 0.3;
                    clip(0.25 - abs(t - floor(t) - 0.25));
                    clip(i.uv1.y);
                }
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Vo { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv0 : TEXCOORD1; float4 uv1 : TEXCOORD2; };
            Vo vert(RAttributes v) { Vo o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); o.uv0 = v.uv0; o.uv1 = v.uv1; return o; }
            half4 frag(Vo i) : SV_Target
            {
                if (i.uv1.z > 7.5 && i.uv1.z < 8.5)
                {
                    float t = i.uv0.y / 0.3;
                    clip(0.25 - abs(t - floor(t) - 0.25));
                    clip(i.uv1.y);
                }
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
