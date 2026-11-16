// Seas, lakes and rivers of the mini Earth. Waves (a swell that moves the surface near the camera, plus finer
// ripples in the normals), colour by depth, refraction of the bed, sky reflection, sun glint, foam on shores,
// round things standing in the water and in rings where something disturbs it (set from EarthWater.cs).
Shader "MarsNeedsWater/EarthWater"
{
    Properties
    {
        _WaveTex ("Wave normals (rg) and foam (b)", 2D) = "gray" {}
        _WaterMask ("The map's water pixels for this square (set per square)", 2D) = "white" {}
        _TileSize ("Width of this square, game metres (set per square)", Float) = 1
        _DeepColour ("Deep sea", Color) = (0.02, 0.36, 0.72, 1)
        _LakeColour ("Deep lake", Color) = (0.04, 0.46, 0.56, 1)
        _FoamColour ("Foam", Color) = (0.93, 0.96, 1.0, 1)
        _SkyZenith ("Sky overhead", Color) = (0.22, 0.52, 0.98, 1)
        _SkyHorizon ("Sky at the horizon", Color) = (0.70, 0.80, 0.92, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EarthToon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_WaveTex);
            SAMPLER(sampler_WaveTex);
            // per square, from a MaterialPropertyBlock (EarthWorld.Show)
            TEXTURE2D(_WaterMask);
            SAMPLER(sampler_WaterMask);
            float _TileSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _WaveTex_ST;
                half4 _DeepColour;
                half4 _LakeColour;
                half4 _FoamColour;
                half4 _SkyZenith;
                half4 _SkyHorizon;
            CBUFFER_END

            // set every frame by EarthWater.cs
            float4 _WaterWaves[4];      // xy: direction times wave number, z: height, w: angular speed
            float4 _WaterRipples[32];   // x, z: where (world), y: strength, w: start time
            float _WaterTime;
            float4 _WaterOffset;        // xy: world x, z offset so the waves carry on when the world re-centres
            float _WaterUnder;          // 1 while the camera is under water
            // the mirror image of the world (WaterReflection.cs), for water at _WaterReflectionLevel
            TEXTURE2D(_WaterReflection);
            SAMPLER(sampler_WaterReflection);
            float _WaterReflectionOn;
            float _WaterReflectionLevel;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;    // x: depth of water here (game metres, below zero on a dry bank), y: 0 sea, 1 lake or river, 2 narrow water on the ground
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 data : TEXCOORD1;
                float3 upWS : TEXCOORD2;
                float fog : TEXCOORD3;
                float2 tileUV : TEXCOORD4;
            };

            // smooth random-looking values that never repeat (EarthWater.cs has the same sums for the swell)
            float Hash(float2 c)
            {
                float3 q = frac(float3(c.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float Noise(float2 p)
            {
                float2 c = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(c), Hash(c + float2(1, 0)), f.x), lerp(Hash(c + float2(0, 1)), Hash(c + float2(1, 1)), f.x), f.y);
            }

            // the swell is bigger in some places than others, so its crests do not line up in a grid
            float Patchy(float2 p) { return 0.35 + 1.3 * Noise(p / 70.0 + _WaterTime * 0.004); }

            float2 Turn(float2 v, float a) { float s, c; sincos(a, s, c); return float2(c * v.x - s * v.y, s * v.x + c * v.y); }

            // the swell's crests wander: each spot is pushed a few metres by slow noise before the waves are read
            float2 Warp(float2 p) { return float2(Noise(p / 37.0 + 3.1), Noise(p / 37.0 - 8.7)) * 9.0 - 4.5; }

            float Swell(float2 p, out float2 slope)
            {
                float h = 0;
                slope = 0;
                float2 q = p + Warp(p);
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float4 w = _WaterWaves[i];
                    // every wave grows and fades in its own patches, so where they cross never looks the same twice
                    float m = 0.2 + 1.5 * Noise(p / (53.0 + i * 19.0) + i * 5.3 + _WaterTime * 0.004);
                    float s, c;
                    sincos(dot(w.xy, q) - w.w * _WaterTime + i * 1.7, s, c);
                    h += w.z * m * s;
                    slope += w.z * m * c * w.xy;
                }
                return h;
            }

            // how much a spot heaves: less on lakes, none at the very edge, none far from the camera
            float Calm(float depth, float kind) { return lerp(1.0, 0.3, saturate(kind)) * saturate(depth / 1.5) * (1 - saturate(kind - 1)); }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pos = TransformObjectToWorld(v.positionOS.xyz);
                float3 up = normalize(TransformObjectToWorldNormal(v.normalOS));
                // no heave here: the water mesh's points are about 8 m apart, shorter than the swell, so moving them
                // sampled the waves into a regular zigzag of light and dark bands. The swell is drawn per pixel instead.
                o.positionWS = pos;
                o.positionCS = TransformWorldToHClip(pos);
                o.data = v.uv;
                o.upWS = up;
                o.fog = ComputeFogFactor(o.positionCS.z);
                float size = max(_TileSize, 1.0);
                o.tileUV = float2(v.positionOS.x / size + 0.5, 0.5 - v.positionOS.z / size);
                return o;
            }

            half3 Sky(float3 dir)
            {
                float y = dir.y < 0 ? -dir.y * 0.3 : dir.y;
                return lerp(_SkyHorizon.rgb, _SkyZenith.rgb, pow(saturate(y), 0.55));
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 pos = i.positionWS;
                float depthHere = i.data.x, lake = saturate(i.data.y), stream = saturate(i.data.y - 1);
                float3 toCam = _WorldSpaceCameraPos - pos;
                float dist = length(toCam);
                float3 V = toCam / max(dist, 1e-4);
                float2 p = pos.xz + _WaterOffset.xy;

                // ---- the shoreline. The map's water pixels are a couple of metres wide, which drew a blocky edge with
                // square notches. The outline is read slightly blurred (rounded corners) at a spot pushed about by noise
                // (a wandering, natural line), and the water washes up and back over it.
                float msize = max(_TileSize, 1.0);
                float2 wob = (float2(Noise(p / 6.0 + 4.1), Noise(p / 6.0 - 9.3)) - 0.5) * 2.4
                           + (float2(Noise(p / 1.7 + 1.3), Noise(p / 1.7 - 5.1)) - 0.5) * 0.7;
                float2 shoreUV = i.tileUV + float2(wob.x, -wob.y) / msize;
                float edgeField = 0.5 * SAMPLE_TEXTURE2D_BIAS(_WaterMask, sampler_WaterMask, shoreUV, 0.6).r
                                + 0.5 * SAMPLE_TEXTURE2D_BIAS(_WaterMask, sampler_WaterMask, shoreUV, 1.6).r;
                float nearShore = SAMPLE_TEXTURE2D_BIAS(_WaterMask, sampler_WaterMask, shoreUV, 2.5).r;
                float swashPhase = _WaterTime * 0.75 + Noise(p / 23.0 + 0.7) * 6.2832;
                float swash = (sin(swashPhase) * 0.5 + 0.5) * (1 - 0.7 * lake) * (1 - stream) * saturate(1.5 - dist / 400.0);
                float waterline = 0.5 + 0.2 * swash;                 // higher = the water has drawn back
                float onMap = smoothstep(waterline - 0.05, waterline + 0.05, edgeField);
                // ---- the surface's slope: swell, finer ripples, rings
                float2 swellSlope;
                float calm = Calm(depthHere, i.data.y);
                float swell = Swell(p, swellSlope) * calm;
                swellSlope *= calm * saturate(1.2 - dist / 700.0);      // far off, the swell's lines would read as stripes
                // three layers of small waves, each turned and scaled differently so their repeats never line up,
                // mixed by slow noise so no stretch of water looks like the next
                float2 uv1 = p / 23.7 + _WaterTime * float2(0.011, 0.006);
                float2 uv2 = Turn(p, 0.83) / 9.13 + _WaterTime * float2(-0.017, 0.013);
                float2 uv3 = Turn(p, 2.29) / 4.41 + _WaterTime * float2(0.021, -0.019);
                half4 t1 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv1);
                half4 t2 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv2);
                half4 t3 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, uv3);
                float n1 = Noise(p / 31.0 + 7.1), n2 = Noise(p / 13.0 - 3.7);
                float2 s1 = Turn(t1.rg * 2 - 1, 0.0), s2 = Turn(t2.rg * 2 - 1, 0.83), s3 = Turn(t3.rg * 2 - 1, 2.29);
                float fine = lerp(0.2, 0.11, lake) * saturate(1.25 - dist / 2500.0);     // stylized: a calm, clean surface
                float2 slope = swellSlope + (s1 * (0.3 + 0.5 * n1) + s2 * (0.2 + 0.4 * n2) + s3 * 0.25) * fine;

                float ringFoam = 0;
                [loop]
                for (int r = 0; r < 32; r++)
                {
                    if (dist > 300) break;          // rings are only ever near the player
                    float4 rp = _WaterRipples[r];
                    float age = _WaterTime - rp.w;
                    if (age < 0 || age > 3.0 || rp.y <= 0) continue;
                    float2 d = pos.xz - rp.xz;
                    float len = length(d);
                    float x = len - (0.25 + age * 1.7 * sqrt(rp.y + 0.2));
                    float fade = (1 - age / 3.0);
                    float env = exp(-x * x * 5.0) * fade * fade * rp.y;
                    if (env < 0.002) continue;
                    slope += (d / max(len, 1e-3)) * cos(x * 11.0) * env * 0.9;
                    ringFoam += env * saturate(1.0 - age * 0.9) * 0.7;
                }

                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));
                if (!front) N = -N;

                // ---- what is under the surface, and how much water the eye looks through
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float eyeHere = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float eyeScene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float through = max(0, eyeScene - eyeHere);
                float path = min(through, max(depthHere, 0) / max(abs(V.y), 0.08));
                path = lerp(path, 0.7, stream);      // narrow water lies on the ground: give it some body

                float2 bend = slope * 0.045 * saturate(path * 0.6) / (1.0 + eyeHere * 0.01);
                float2 ruv = suv + bend;
                if (LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams) < eyeHere) ruv = suv;   // never bend in what is above the water
                half3 below = SampleSceneColor(ruv);

                Light sun = GetMainLight(TransformWorldToShadowCoord(pos));
                half shadow = sun.shadowAttenuation;

                // caustics: the waves focus sunlight into a moving net of bright lines on a shallow bed
                if (front && dist < 160)
                {
                    float3 bedPos = ComputeWorldSpacePosition(ruv, SampleSceneDepth(ruv), UNITY_MATRIX_I_VP);
                    float bedDepth = pos.y - bedPos.y;
                    float2 cp = bedPos.xz + _WaterOffset.xy + slope * 0.6;
                    half c1 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, cp / 5.3 + _WaterTime * float2(0.021, 0.013)).a;
                    half c2 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, Turn(cp, 1.9) / 6.1 - _WaterTime * float2(0.017, 0.024)).a;
                    float caustic = min(c1, c2) * 2.2 * saturate(bedDepth * 2.0) * saturate(1 - bedDepth / 6.0) * saturate(1.6 - dist / 100.0);
                    below *= 1 + caustic * shadow * saturate(sun.direction.y * 2);
                }
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 lit = sun.color * (saturate(sun.direction.y) * (0.45 + 0.55 * shadow)) + ambient;

                float3 sigma = lerp(float3(0.9, 0.16, 0.12), float3(0.8, 0.2, 0.26), lake);
                float3 keep = exp(-sigma * path);
                float cloud = 1 - exp(-path * lerp(0.2, 0.34, lake));
                half3 deep = lerp(_DeepColour.rgb, _LakeColour.rgb, lake) * lit;
                // stylized: a bright turquoise band in the shallows, stepping to the deep colour
                half3 shallow = lerp(half3(0.10, 0.80, 0.78), half3(0.16, 0.74, 0.62), lake) * lit;
                half3 body = lerp(shallow, deep, smoothstep(0.5, 3.5, path));
                half3 water = lerp(below * keep, body, cloud);

                // light shining through the thin top of a wave when you look towards the sun
                float2 viewH = normalize(-V.xz + 1e-5), sunH = normalize(sun.direction.xz + 1e-5);
                // only seen low across the water and close by; from the air it painted the swell's crests as teal stripes.
                // the crest is broken up by the small waves and by noise so it never lines up into rows
                float lowView = pow(saturate(1 - abs(V.y) * 2.5), 2);
                float bits = saturate(swell / 0.15 + (t1.r + t2.g - 1.0) * 0.8) * Noise(p / 9.0 + 2.3);
                float sss = (1 - lake) * bits * lowView * pow(saturate(dot(viewH, sunH)), 3) * saturate(1.2 - dist / 250);
                water += half3(0.05, 0.36, 0.30) * sun.color * sss * 0.35 * shadow;

                half3 colour;
                if (front)
                {
                    float fres = 0.02 + 0.98 * pow(1 - saturate(dot(N, V)), 5);
                    fres *= 0.55;                                                     // stylized: the water keeps its own colour
                    fres = lerp(fres, 1, saturate((dist - 1500) / 40000) * 0.6);      // far off the sea is mostly sky
                    float3 R = reflect(-V, N);
                    half3 sky = Sky(R);
                    // the mirror image of hills, buildings and bridges, where this water is the water it was made for
                    float mirrorOn = _WaterReflectionOn * saturate(1 - abs(pos.y - _WaterReflectionLevel) / 1.5) * (1 - stream * 0.5);
                    if (mirrorOn > 0)
                    {
                        float2 muv = suv + slope * 0.035 / (1.0 + eyeHere * 0.004);
                        half3 mirror = SAMPLE_TEXTURE2D(_WaterReflection, sampler_WaterReflection, muv).rgb;
                        sky = lerp(sky, mirror, mirrorOn);
                    }
                    colour = lerp(water, sky, fres);
                    float3 H = normalize(sun.direction + V);
                    float nh = saturate(dot(N, H));
                    // stylized: the sun breaks into crisp sparkles rather than a smooth sheen
                    colour += sun.color * shadow * (smoothstep(0.35, 0.5, pow(nh, 500)) * 1.6 + pow(nh, 90) * 0.05) * saturate(sun.direction.y * 4);
                }
                else
                {
                    // from below: the sky through the surface, mirror-like at a slant
                    float edge = saturate(pow(1 - abs(dot(N, V)), 2) * 1.6);
                    colour = lerp(below * half3(0.75, 0.92, 0.95), deep, edge);
                }

                // ---- foam: shores, things standing in the water, rings, crests out at sea
                half foamNoise = saturate(t1.b * 0.4 + t2.b * 0.35 + t3.b * 0.25);
                // a broken line of froth riding the edge of the wash, and lines of foam rolling in towards the beach
                float edgeFoam = saturate(1 - abs(edgeField - waterline - 0.05) / 0.07) * (0.55 + 0.45 * cos(swashPhase)) * (0.5 + Noise(p / 2.3));
                float rollPhase = nearShore * 30.0 + _WaterTime * 1.3 + Noise(p / 8.0 - 2.2) * 5.0;
                float rolling = saturate((0.97 - nearShore) * 2.5) * saturate(sin(rollPhase) * 1.8 - 0.8) * Noise(p / 4.0 + 6.6) * 1.4;
                float shore = max(max(edgeFoam, rolling) * onMap, saturate(1 - depthHere / 0.06) * 0.3) * (1 - stream) * saturate(1.5 - dist / 3000);
                float hugging = dist < 400 ? saturate(1 - through * max(abs(V.y), 0.15) / 0.08) * (1 - stream) : 0;
                float surf = (1 - lake) * saturate(1 - depthHere / 0.8) * saturate(sin(depthHere * 9.0 - _WaterTime * 1.6) * 1.4 - 0.3);
                // whitecaps: only now and then, where a random patch of rough water meets a high crest
                float rough = saturate((Noise(p / 18.0 + _WaterTime * float2(0.03, -0.02)) - 0.72) * 6.0);
                float crest = (1 - lake) * rough * saturate((swell / 0.2 - 0.45) * 2.5) * saturate(1.2 - dist / 500);
                float foam = max(max(shore, hugging * 0.8), max(surf * 0.6, crest * 0.6)) + ringFoam;
                foam = saturate(smoothstep(0.42, 0.56, foam * (0.55 + foamNoise)));      // stylized: foam with a clean edge
                colour = lerp(colour, _FoamColour.rgb * lit, foam * 0.9);

                // soft edge where the surface meets the bank (the bank is above water past this line)
                // where the water stops: the map's own outline, or (on wide water) wherever the surface is above the ground
                // where the water stops: the map's own outline (smooth, not the zigzag of the height grid)
                half alpha = onMap * saturate(through / 0.03);
                if (!front) alpha = 1;

                colour = MixFog(colour, i.fog);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
