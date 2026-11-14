// Grass blades, reeds and stones near the player (EarthGrass.cs). One draw for many clumps: each clump's place,
// turn, size and colour come from a buffer, indexed by the instance number. Blades sway in gusts, bend away from
// the player, and thin out and sink into the ground towards the edge of the grass area.
Shader "MarsNeedsWater/EarthGrass"
{
    Properties
    {
        _Wind ("Wind strength", Float) = 1
        _UpBlend ("Light blades like the ground (0..1)", Float) = 0.65
        _Stone ("Stone (1) or grass (0)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Inst { float3 pos; float yaw; float height; float width; uint tint; float rand; };
        StructuredBuffer<Inst> _Instances;

        CBUFFER_START(UnityPerMaterial)
            float _Wind;
            float _UpBlend;
            float _Stone;
            float _Cull;
        CBUFFER_END

        float4 _GrassPlayer;       // the player's feet-ish (world)
        float _GrassRange;
        float4 _GroundOffset;      // EarthGround.cs: keeps the wind pattern fixed to the ground when the world re-centres

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            uint id : SV_InstanceID;
        };

        // sRGB colour to linear light (written out here: the library's version is not included on every platform)
        half3 ToLinear(half3 c) { return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878); }

        // where a vertex ends up, its normal, its colour and how far up the blade it is
        void Place(Attributes v, out float3 pw, out float3 nw, out half3 tint, out float t)
        {
            Inst I = _Instances[v.id];
            float c = cos(I.yaw), s = sin(I.yaw);
            float3 lp = v.positionOS.xyz;
            float3 ln = v.normalOS;
            lp.xz = float2(c * lp.x - s * lp.z, s * lp.x + c * lp.z);
            ln.xz = float2(c * ln.x - s * ln.z, s * ln.x + c * ln.z);
            t = saturate(v.uv.y);

            // thin out with distance (each clump has its own threshold), and sink the last ones into the ground
            float d = distance(I.pos, _WorldSpaceCameraPos);
            float keep = lerp(1.0, 0.3, saturate((d - 10.0) / max(_GrassRange - 10.0, 1.0)));
            float fade = saturate((_GrassRange - d) / (_GrassRange * 0.25)) * step(I.rand, keep);
            float grow = 1.0 + (1.0 - keep) * 0.6;        // fewer, fuller clumps further out

            if (_Stone > 0.5)
            {
                lp *= I.height * fade * float3(I.width, 1, 1.0 / max(I.width, 0.3));
                lp.y *= 1.0;
            }
            else
            {
                lp.xz *= I.width * grow;
                lp.y *= I.height * fade;
                float h = I.height * fade;
                // wind: travelling gusts plus a quick flutter
                float2 wdir = normalize(float2(1.0, 0.45));
                float2 g = I.pos.xz + _GroundOffset.xy;
                float ph = dot(g, wdir) * 0.28 - _Time.y * 1.6;
                float gust = sin(ph) * 0.5 + 0.5;
                gust = gust * gust * (0.6 + 0.4 * sin(dot(g, float2(-0.07, 0.11)) + _Time.y * 0.3));
                float flutter = sin(_Time.y * 3.3 + I.rand * 31.0 + lp.x * 9.0) * 0.12;
                float sway = (gust * 0.55 + flutter + 0.08) * _Wind;
                lp.xz += wdir * sway * t * t * h;
                // pushed aside by the player
                float3 dp = I.pos - _GrassPlayer.xyz;
                float dd = length(dp.xz);
                float push = saturate(1.0 - dd / 0.75) * step(abs(dp.y + 0.8), 1.6);
                lp.xz += (dp.xz / max(dd, 1e-3)) * push * t * t * h * 0.9;
                lp.y *= 1.0 - push * 0.45 * t;
            }
            pw = I.pos + lp;
            nw = normalize(lerp(ln, float3(0, 1, 0), _UpBlend));
            half3 col = half3(I.tint & 255u, (I.tint >> 8) & 255u, (I.tint >> 16) & 255u) / 255.0;
            tint = ToLinear(col);
        }
        ENDHLSL

        Pass
        {
            Name "Grass"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half3 tint : TEXCOORD2;
                float t : TEXCOORD3;
                float fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pw, nw; half3 tint; float t;
                Place(v, pw, nw, tint, t);
                o.positionWS = pw;
                o.positionCS = TransformWorldToHClip(pw);
                o.normalWS = nw;
                o.tint = tint;
                o.t = t;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 albedo;
                if (_Stone > 0.5) albedo = i.tint;
                else
                {
                    // dark and a little brown at the root, light and slightly yellow at the tip
                    albedo = i.tint * lerp(0.45, 1.15, i.t);
                    albedo = lerp(albedo, albedo * half3(1.08, 1.04, 0.8), i.t * i.t);
                }
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = sun.shadowAttenuation;
                half ndl = saturate(dot(n, sun.direction));
                half3 col = albedo * (sun.color * ndl * shadow + SampleSH(n));
                if (_Stone < 0.5)
                {
                    // light shining through thin blades from behind
                    half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                    half back = pow(saturate(dot(-V, sun.direction)), 4.0) * i.t;
                    col += albedo * sun.color * back * shadow * 0.6;
                    // the bottom of a lawn is in its own shade
                    col *= lerp(0.55, 1.0, saturate(i.t * 1.6));
                }
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 pw, nw; half3 tint; float t;
                Place(v, pw, nw, tint, t);
                return TransformWorldToHClip(pw);
            }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 pw, nw; half3 tint; float t;
                Place(v, pw, nw, tint, t);
                float4 cs = TransformWorldToHClip(pw);
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
