// The painted sky of the mini Earth: a bright blue fading to a pale horizon (the fog's colour, so far land melts
// into it), a soft sun, and small fluffy clouds with flat bases standing above the horizon, no two alike.
Shader "MarsNeedsWater/EarthSky"
{
    Properties
    {
        _Zenith ("Overhead", Color) = (0.13, 0.42, 0.95, 1)
        _Middle ("Half-way up", Color) = (0.33, 0.66, 1.0, 1)
        _Horizon ("Horizon (same as the fog)", Color) = (0.78, 0.92, 1.0, 1)
        _CloudLight ("Cloud, sunny side", Color) = (1.0, 0.99, 0.96, 1)
        _CloudShade ("Cloud, shaded side", Color) = (0.80, 0.86, 0.98, 1)
        _Cover ("How much sky the clouds fill", Range(0, 1)) = 0.34
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Middle, _Horizon, _CloudLight, _CloudShade;
                half _Cover;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

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

            // big rounded shapes: few octaves, each turned so nothing lines up
            float Puff(float2 p)
            {
                float a = Noise(p) * 0.55;
                p = mul(float2x2(0.8, -0.6, 0.6, 0.8), p) * 2.1 + 7.3;
                a += Noise(p) * 0.28;
                p = mul(float2x2(0.8, -0.6, 0.6, 0.8), p) * 2.3 + 3.1;
                a += Noise(p) * 0.17;
                return a;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                Light sun = GetMainLight();
                float up = saturate(d.y);

                // the blue: horizon, middle, overhead
                half3 col = lerp(_Horizon.rgb, _Middle.rgb, smoothstep(0.0, 0.28, up));
                col = lerp(col, _Zenith.rgb, smoothstep(0.2, 0.95, up));
                // below the horizon: the haze carries on
                col = lerp(col, _Horizon.rgb, saturate(-d.y * 8.0));

                // the sun: a soft disc with a wide warm glow
                float sd = dot(d, sun.direction);
                col += half3(1.0, 0.9, 0.65) * (smoothstep(0.9993, 0.9997, sd) * 3.0 + pow(saturate(sd), 180.0) * 0.5 + pow(saturate(sd), 12.0) * 0.12);

                // clouds: separate cumulus puffs standing in rows above the horizon, each a cluster of round
                // lobes on a flat base, lit from above. Placed by chance in a grid around the sky, so no two match.
                float az = atan2(d.x, d.z) + _Time.y * 0.0025;
                float el = asin(clamp(d.y, -1.0, 1.0));
                const float COLS = 16.0, CW = 6.2831853 / 16.0, RH = 0.17, EL0 = 0.05;
                float cx = az / CW, ry = (el - EL0) / RH;
                float wob = (Noise(float2(az, el) * 38.0) - 0.5) * 0.10 + (Noise(float2(az, el) * 90.0) - 0.5) * 0.04;
                float alpha = 0.0, tone = 0.0;
                if (el > 0.0 && el < EL0 + RH * 3.6)
                {
                    [loop] for (int j = -1; j <= 1; j++)
                    [loop] for (int k = -1; k <= 1; k++)
                    {
                        float col_ = floor(cx) + k, row = floor(ry) + j;
                        if (row < 0.0 || row > 2.0) continue;
                        float2 id = float2(fmod(col_ + COLS * 8.0, COLS), row);
                        if (Hash(id + 3.7) > _Cover + row * 0.12) continue;                 // most cells stay empty
                        float size = lerp(0.05, 0.085, Hash(id + 9.1)) * (0.75 + 0.3 * row);   // nearer the horizon: farther away, smaller
                        float baseEl = EL0 + (row + 0.15 + 0.45 * Hash(id + 5.3)) * RH;
                        float midAz = (col_ + 0.25 + 0.5 * Hash(id + 1.9)) * CW;
                        float dx0 = (az - midAz) * cos(el), dy0 = el - baseEl;
                        float f = -1.0;
                        [unroll] for (int b = 0; b < 5; b++)
                        {
                            float t = b - 2.0;
                            float r = size * (1.0 - 0.2 * abs(t) + 0.3 * (Hash(id + b * 7.3) - 0.5));
                            float2 c = float2(t * size * 0.62 + (Hash(id + b * 3.1 + 20.0) - 0.5) * size * 0.3, r * 0.62);
                            f = max(f, 1.0 - length(float2(dx0, dy0) - c) / r);
                        }
                        f += wob;
                        float a = smoothstep(0.0, 0.16, f) * smoothstep(-0.004, 0.008, dy0 + wob * 0.02);
                        if (a > alpha)
                        {
                            alpha = a;
                            // bright tops, a soft lavender underside
                            tone = smoothstep(0.1, 0.75, dy0 / (size * 1.3) + f * 0.35);
                        }
                    }
                }
                if (alpha > 0.0)
                {
                    half3 cloud = lerp(_CloudShade.rgb, _CloudLight.rgb, tone);
                    // far ones near the horizon melt into the haze
                    float far = smoothstep(0.03, 0.3, el);
                    cloud = lerp(_Horizon.rgb, cloud, 0.45 + 0.55 * far);
                    col = lerp(col, cloud, alpha * (0.55 + 0.45 * far));
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
