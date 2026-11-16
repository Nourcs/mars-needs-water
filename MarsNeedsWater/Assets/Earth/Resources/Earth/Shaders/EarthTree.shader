// Trees (EarthTrees.cs). One draw per model and level of detail: each tree's place, turn, size and leaf colour
// come from a buffer, indexed by the instance number plus _Base. Leaves and impostors are cut out of their
// pictures (alpha test); bark repeats from a texture array. Whole trees lean in gusts, branches sway, leaves
// flutter, all the same in the shadow pass so shadows move with the trees.
Shader "MarsNeedsWater/EarthTree"
{
    Properties
    {
        _Leaves ("Leaf atlas", 2D) = "white" {}
        _Impostors ("Impostors", 2D) = "white" {}
        _Bark ("Bark", 2DArray) = "" {}
        _Wind ("Wind strength", Float) = 1
        _Cutoff ("Alpha cut-off", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Inst { float3 pos; float yaw; float height; float width; uint tint; float rand; };
        StructuredBuffer<Inst> _Instances;

        CBUFFER_START(UnityPerMaterial)
            float4 _Leaves_ST;
            float _Wind;
            float _Cutoff;
        CBUFFER_END

        float _Base;               // first instance of this draw in the buffer (per draw, from a property block)
        float4 _GroundOffset;      // EarthGround.cs: keeps the wind pattern fixed to the ground when the world re-centres

        TEXTURE2D(_Leaves); SAMPLER(sampler_Leaves);
        TEXTURE2D(_Impostors); SAMPLER(sampler_Impostors);
        TEXTURE2D_ARRAY(_Bark); SAMPLER(sampler_Bark);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float3 uv : TEXCOORD0;
            float4 color : COLOR;      // r: sway, g: leaf, b: light (ambient occlusion), a: phase
            uint id : SV_InstanceID;
        };

        // where a vertex ends up after size, turn and wind; its normal; the tree's leaf tint and middle
        void Place(Attributes v, out float3 pw, out float3 nw, out half3 tint, out float3 centre, out float rand)
        {
            Inst I = _Instances[v.id + (uint)_Base];
            float c = cos(I.yaw), s = sin(I.yaw);
            float3 lp = v.positionOS.xyz;
            float3 ln = v.normalOS;
            float wide = I.height * I.width;
            lp = float3(lp.x * wide, lp.y * I.height, lp.z * wide);
            ln = normalize(float3(ln.x / max(I.width, 0.2), ln.y, ln.z / max(I.width, 0.2)));
            lp.xz = float2(c * lp.x - s * lp.z, s * lp.x + c * lp.z);
            ln.xz = float2(c * ln.x - s * ln.z, s * ln.x + c * ln.z);

            // wind: travelling gusts lean the whole tree, branches sway, leaves flutter
            float t = _Time.y;
            float2 wdir = normalize(float2(1.0, 0.45));
            float2 g = I.pos.xz + _GroundOffset.xy;
            float ph = dot(g, wdir) * 0.045 - t * 0.9;
            float gust = sin(ph) * 0.5 + 0.5;
            gust = gust * gust * (0.6 + 0.4 * sin(dot(g, float2(-0.013, 0.021)) + t * 0.23));
            float h01 = saturate(lp.y / max(I.height, 0.1));
            float bend = h01 * h01 * I.height * _Wind;
            float sway = sin(t * (0.9 + I.rand * 0.4) + I.rand * 17.0) * 0.006 + gust * 0.018 + 0.004;
            lp.xz += wdir * bend * sway;
            lp.xz += float2(-wdir.y, wdir.x) * bend * sin(t * 1.3 + I.rand * 9.0) * 0.003;
            float phase = v.color.a * 6.2832;
            float br = v.color.r * I.height * _Wind * (0.004 + gust * 0.008);
            lp += float3(wdir.x, 0.3, wdir.y) * br * sin(t * 2.1 + phase + I.rand * 5.0);
            float leaf = v.color.g * v.color.r * I.height * _Wind * (0.0025 + gust * 0.004);
            lp += ln * leaf * sin(t * 7.3 + phase * 4.0);

            pw = I.pos + lp;
            nw = ln;
            tint = half3(I.tint & 255u, (I.tint >> 8) & 255u, (I.tint >> 16) & 255u) / 128.0;
            centre = I.pos + float3(0, I.height * 0.6, 0);
            rand = I.rand;
        }

        // the colour and see-through of a spot: leaf atlas, impostor picture or bark
        half4 Albedo(float3 uvw, half3 tint, float rand)
        {
            if (uvw.z > 15.0)
            {
                half4 c = SAMPLE_TEXTURE2D(_Impostors, sampler_Impostors, uvw.xy);
                c.rgb *= lerp(half3(1, 1, 1), tint, 0.85);
                return c;
            }
            if (uvw.z > 9.5)
            {
                half4 c = SAMPLE_TEXTURE2D(_Leaves, sampler_Leaves, uvw.xy);
                c.rgb *= tint;
                return c;
            }
            half4 b = SAMPLE_TEXTURE2D_ARRAY(_Bark, sampler_Bark, uvw.xy, round(uvw.z));
            b.rgb *= 0.88 + 0.24 * rand;
            b.a = 1;
            return b;
        }
        ENDHLSL

        Pass
        {
            Name "Tree"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EarthToon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 uvw : TEXCOORD2;
                half4 tintAo : TEXCOORD3;     // rgb tint, a light
                float4 centreRand : TEXCOORD4;
                float fog : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pw, nw, centre; half3 tint; float rand;
                Place(v, pw, nw, tint, centre, rand);
                o.positionWS = pw;
                o.positionCS = TransformWorldToHClip(pw);
                o.normalWS = nw;
                o.uvw = v.uv;
                o.tintAo = half4(tint, v.color.b);
                o.centreRand = float4(centre, rand);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 alb = Albedo(i.uvw, i.tintAo.rgb, i.centreRand.w);
                clip(alb.a - _Cutoff);
                bool foliage = i.uvw.z > 9.5;
                float3 n = normalize(i.normalWS);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                if (i.uvw.z > 15.0)
                {
                    // impostor: lit as a rounded crown rather than a flat picture
                    float3 r = i.positionWS - i.centreRand.xyz;
                    n = normalize(normalize(r) * 0.6 + V * 0.3 + float3(0, 0.35, 0));
                }
                else if (!foliage && !front) n = -n;
                half ao = i.tintAo.a;
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = ToonShadow(sun.shadowAttenuation, i.positionWS);
                // stylized: the leaf and bark pictures are flattened into a few tones, like cut paper
                half tl = max(dot(alb.rgb, half3(0.3, 0.55, 0.15)), 0.004);
                half steps = foliage ? 3.0 : 2.5;
                half q = tl * steps * 3.2;
                half tq = (floor(q) + smoothstep(0.35, 0.65, frac(q))) / (steps * 3.2);
                alb.rgb *= lerp(1.0, max(tq, 0.03) / tl, 0.8);
                half ndl = foliage ? (dot(n, sun.direction) + 0.35) / 1.35 : dot(n, sun.direction);
                half3 sky = SampleSH(n);
                half3 col = ToonLight(ToonPop(alb.rgb, foliage ? 1.5 : 1.25), ndl, shadow, sun.color, sky, ao);
                if (foliage)
                {
                    // sunlight through the leaves when looking towards the sun
                    half back = pow(saturate(dot(-V, sun.direction)), 3.0);
                    col += alb.rgb * half3(1.0, 1.05, 0.7) * sun.color * back * shadow * 0.45 * ao;
                    // a faint sheen on leaves facing the sun
                    half3 hv = normalize(sun.direction + V);
                    col += sun.color * pow(saturate(dot(n, hv)), 24.0) * 0.04 * shadow;
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            struct V2F { float4 positionCS : SV_POSITION; float3 uvw : TEXCOORD0; half4 tint : TEXCOORD1; };
            V2F vert(Attributes v)
            {
                V2F o;
                float3 pw, nw, centre; half3 tint; float rand;
                Place(v, pw, nw, tint, centre, rand);
                o.positionCS = TransformWorldToHClip(pw);
                o.uvw = v.uv; o.tint = half4(tint, rand);
                return o;
            }
            half frag(V2F i) : SV_Target
            {
                clip(Albedo(i.uvw, i.tint.rgb, i.tint.a).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            struct V2F { float4 positionCS : SV_POSITION; float3 uvw : TEXCOORD0; half4 tint : TEXCOORD1; };
            V2F vert(Attributes v)
            {
                V2F o;
                float3 pw, nw, centre; half3 tint; float rand;
                Place(v, pw, nw, tint, centre, rand);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, _MainLightPosition.xyz));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uvw = v.uv; o.tint = half4(tint, rand);
                return o;
            }
            half frag(V2F i) : SV_Target
            {
                clip(Albedo(i.uvw, i.tint.rgb, i.tint.a).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
