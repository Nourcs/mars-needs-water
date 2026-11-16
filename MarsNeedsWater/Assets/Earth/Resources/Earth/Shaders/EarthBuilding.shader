// Buildings of the mini Earth (meshes made by Core/Buildings.cs; looks chosen by Core/BuildingStyle.cs).
// Everything is drawn from numbers on each vertex, no textures: brick, stone, stucco, timber, tiles and glass;
// windows of a dozen kinds with frames, glazing bars, blinds and curtains; shutters, balconies, shop fronts, arcades,
// garage doors, cornices, fire escapes; roof tiles, slates, metal sheets and dormers. Each building carries its own
// colours and seed, and each window its own random numbers, so no two walls come out the same.
//   uv0  metres along the wall and metres above the building's foot (roofs: metres along the eave and up the slope)
//   uv1  x: width of one window bay (0 = blank wall), y: height of one floor, z: height of the wall, w: accent colour
//   uv2  x: wall or roof colour, y: trim colour, z and w: packed choices (BK in Core/BuildingStyle.cs)
Shader "MarsNeedsWater/EarthBuilding"
{
    Properties
    {
        _DetailFar ("Fine detail fades out by (m)", Float) = 220
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _DetailFar;
        CBUFFER_END

        // ---- the same numbers as BK in Core/BuildingStyle.cs
        #define S_WALL 0
        #define S_ROOF 1
        #define S_PLAIN 2

        #define M_STUCCO 0
        #define M_BRICK 1
        #define M_STONE 2
        #define M_SIDING 3
        #define M_CONCRETE 4
        #define M_GLASS 5
        #define M_METAL 6
        #define M_ADOBE 7
        #define M_TIMBER 8
        #define M_TILE 9
        #define M_BOARDS 10
        #define M_LIME 11
        #define M_AZULEJO 12

        #define W_NONE 0
        #define W_PLAIN 1
        #define W_FRENCH 2
        #define W_SASH 3
        #define W_ARCH 4
        #define W_RIBBON 5
        #define W_CURTAIN 6
        #define W_SMALL 7
        #define W_MOORISH 8
        #define W_BAY 9
        #define W_GRID 10
        #define W_SLIDING 11

        #define G_SAME 0
        #define G_SHOP 1
        #define G_ARCADE 2
        #define G_RUSTIC 3
        #define G_GARAGE 4
        #define G_STOOP 5
        #define G_BLANK 6

        #define B_IRON 1
        #define B_SLAB 2
        #define B_WOOD 3
        #define BM_ALL 0
        #define BM_PARIS 1
        #define BM_COLUMNS 2
        #define BM_ALTERNATE 3

        #define SH_LOUVER 1
        #define SH_ROLLER 2
        #define SH_SCREEN 3

        #define R_MEMBRANE 0
        #define R_CLAY 1
        #define R_SLATE 2
        #define R_SEAM 3
        #define R_SHINGLE 4
        #define R_THATCH 5
        #define R_CONCRETE 6
        #define R_CORRUGATED 7
        #define R_KAWARA 8
        #define R_GRAVEL 9

        #define F_STRING 4
        #define F_PILASTER 8
        #define F_SURROUND 16
        #define F_SILL 32
        #define F_AC 64
        #define F_FIRE 128
        #define F_AWNING 256
        #define F_WEATHER 512
        #define F_ATTIC 1024
        #define F_DORMER 2048
        #define F_LINTEL 4096
        #define F_GRILLE 8192
        #define F_TWOTONE 16384

        // what kind of opening a cell of the wall has
        #define O_NONE 0
        #define O_WINDOW 1
        #define O_DOOR 2
        #define O_SHOP 3
        #define O_ARCADE 4
        #define O_GARAGE 5

        uint BHash(uint2 v)
        {
            uint h = v.x * 0x8da6b343u ^ v.y * 0xd8163841u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return h;
        }

        float BRand(float2 c, float s)
        {
            int2 ic = int2(floor(c));
            return float(BHash(uint2(ic + int2(32768, 32768)) + uint2(uint(s * 7919.0), uint(s * 104729.0))) & 0xffffu) / 65535.0;
        }

        float BNoise(float2 p, float s)
        {
            float2 i = floor(p);
            float2 f = p - i;
            f = f * f * (3.0 - 2.0 * f);
            float a = BRand(i, s), b = BRand(i + float2(1.0, 0.0), s), c = BRand(i + float2(0.0, 1.0), s), d = BRand(i + float2(1.0, 1.0), s);
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        float BFbm(float2 p, float s)
        {
            return BNoise(p, s) * 0.5 + BNoise(p * 2.03 + float2(17.1, 5.3), s + 1.0) * 0.3 + BNoise(p * 4.1 + float2(3.7, 11.9), s + 2.0) * 0.2;
        }

        float3 BLin(float3 c)
        {
        #if defined(UNITY_COLORSPACE_GAMMA)
            return c;
        #else
            return c * c * (c * 0.2848 + 0.7152);       // close to the sRGB curve
        #endif
        }

        // a colour packed into one number: red * 65536 + green * 256 + blue
        float3 BCol(float p)
        {
            int c = int(p + 0.5);
            return float3(float((c >> 16) & 255), float((c >> 8) & 255), float(c & 255)) / 255.0;
        }

        // how far outside a box (negative inside), metres
        float BBox(float2 p, float2 c, float2 h)
        {
            float2 q = abs(p - c) - h;
            return max(q.x, q.y);
        }

        // 1 inside, 0 outside, smoothed over a pixel
        float BCov(float d, float aa) { return saturate(0.5 - d / aa); }

        // the n - 1 dividers that cut a span 0..1 into n: coverage of lines of half-thickness ht (size is the span in metres)
        float BBars(float t, float n, float size, float ht, float aa)
        {
            float x = t * n;
            if (n < 1.5 || x < 0.5 || x > n - 0.5) return 0.0;
            float d = abs(x - floor(x + 0.5)) / n * size;
            return saturate(0.5 - (d - ht) / aa) * saturate(ht * 2.0 / aa + 0.35);
        }

        // lines repeating every 'period' metres, each 'on' metres thick
        float BStripes(float x, float period, float on, float aa)
        {
            float f = x / period;
            f = abs(f - floor(f) - 0.5) * period;
            return saturate(0.5 - (f - on * 0.5) / aa) * saturate(on / aa + 0.3);
        }

        // ---------------------------------------------------------------- what walls are made of
        float3 BWallStuff(int mat, float3 base, float2 uv, float px, float seed, out float smoothness)
        {
            smoothness = 0.1;
            float big = BFbm(uv * 0.35 + seed * 91.0, 3.0) - 0.5;
            float grain = (BNoise(uv * 43.0, 5.0) + BNoise(uv * 97.0, 6.0) - 1.0) * 0.22 * saturate(0.02 / px);      // a few per cent, close up only
            float3 c = base * (1.0 + big * 0.1);
            if (mat == M_BRICK)
            {
                float2 size = float2(0.13, 0.05);
                float2 st = uv / size;
                float row = floor(st.y);
                st.x += frac(row * 0.5) + BRand(float2(row, 7.0), seed) * 0.12;
                float2 cell = floor(st), f = st - cell;
                float show = saturate(0.05 / px - 0.35);
                c *= 1.0 + (BRand(cell, seed + 1.0) - 0.5) * 0.3 * show;
                float2 e = float2(px, px) / size;
                float joint = max(1.0 - saturate(min(f.x, 1.0 - f.x) / max(e.x, 0.05)), 1.0 - saturate(min(f.y, 1.0 - f.y) / max(e.y, 0.13)));
                c = lerp(c, lerp(c, float3(0.76, 0.73, 0.68), 0.6), joint * show * 0.85);
                c *= 1.0 + grain * 0.6;
            }
            else if (mat == M_STONE)
            {
                float2 size = float2(0.36, 0.17);
                float2 st = uv / size;
                float row = floor(st.y);
                st.x += frac(row * 0.5) + BRand(float2(row, 7.0), seed) * 0.3;
                float2 cell = floor(st), f = st - cell;
                float show = saturate(0.12 / px - 0.3);
                c *= 1.0 + (BRand(cell, seed + 1.0) - 0.5) * 0.11 * show;
                float2 e = float2(px, px) / size;
                float joint = max(1.0 - saturate(min(f.x, 1.0 - f.x) / max(e.x, 0.02)), 1.0 - saturate(min(f.y, 1.0 - f.y) / max(e.y, 0.04)));
                c *= 1.0 - joint * show * 0.2;
                c *= 1.0 + grain * 0.4;
            }
            else if (mat == M_SIDING)
            {
                // overlapping boards: a shadow under each
                float f = frac(uv.y / 0.065);
                float show = saturate(0.05 / px - 0.3);
                c *= 1.0 - smoothstep(0.3, 0.0, f) * 0.3 * show + (f - 0.5) * 0.06 * show;
                c *= 1.0 + (BRand(float2(floor(uv.y / 0.065), floor(uv.x / 0.9)), seed) - 0.5) * 0.05 + grain * 0.2;
            }
            else if (mat == M_BOARDS)
            {
                float st = uv.x / 0.075;
                float f = frac(st);
                float show = saturate(0.05 / px - 0.3);
                c *= 1.0 + (BRand(float2(floor(st), 2.0), seed) - 0.5) * 0.16 * show;
                c *= 1.0 - (1.0 - saturate(min(f, 1.0 - f) / 0.09)) * 0.35 * show;
                c *= 1.0 + (BNoise(float2(uv.x * 60.0, uv.y * 3.0), 6.0) - 0.5) * 0.12 * saturate(0.03 / px);
            }
            else if (mat == M_CONCRETE)
            {
                c *= 1.0 + (BFbm(uv * 2.2 + seed * 13.0, 8.0) - 0.5) * 0.1 + grain * 0.5;
            }
            else if (mat == M_GLASS)
            {
                smoothness = 0.75;
            }
            else if (mat == M_METAL)
            {
                // ribbed sheet
                float show = saturate(0.035 / px - 0.3);
                c *= 1.0 + sin(uv.x / 0.04 * 6.2832) * 0.07 * show;
                c *= 1.0 + (BRand(float2(floor(uv.x / 0.42), 3.0), seed) - 0.5) * 0.05;
                smoothness = 0.3;
            }
            else if (mat == M_ADOBE)
            {
                c *= 1.0 + (BFbm(uv * 3.0 + seed * 7.0, 8.0) - 0.5) * 0.2 + (BNoise(uv * 19.0, 6.0) - 0.5) * 0.1 * saturate(0.06 / px);
            }
            else if (mat == M_TILE || mat == M_AZULEJO)
            {
                float2 st = uv / 0.06;
                float2 cell = floor(st), f = st - cell;
                float show = saturate(0.045 / px - 0.3);
                if (mat == M_AZULEJO)
                {
                    // patterned glazed tiles: a figure in the wall's colour on white, the same on every tile of a house
                    float2 q = abs(f - 0.5);
                    float kind = BRand(float2(1.0, 2.0), seed);
                    float fig = kind < 0.34 ? step(q.x + q.y, 0.3) : kind < 0.67 ? step(abs(length(q) - 0.3), 0.09) : step(min(q.x, q.y), 0.11);
                    float3 white = float3(0.93, 0.93, 0.9);
                    c = lerp(lerp(white, c, 0.45), lerp(white, c, fig), show);
                }
                else c *= 1.0 + (BRand(cell, seed + 1.0) - 0.5) * 0.07 * show;
                float2 e = float2(px, px) / 0.06;
                float joint = max(1.0 - saturate(min(f.x, 1.0 - f.x) / max(e.x, 0.06)), 1.0 - saturate(min(f.y, 1.0 - f.y) / max(e.y, 0.06)));
                c *= 1.0 - joint * show * 0.22;
                smoothness = 0.35;
            }
            else if (mat == M_LIME)
            {
                // limewash over rough masonry: soft, uneven, bright
                c *= 1.0 + (BFbm(uv * 1.6 + seed * 5.0, 8.0) - 0.5) * 0.07 + grain * 0.3;
            }
            else
            {
                // stucco, and timber frames' infill
                c *= 1.0 + (BFbm(uv * 2.6 + seed * 3.0, 8.0) - 0.5) * 0.06 + grain * 0.5;
            }
            return c;
        }

        // ---------------------------------------------------------------- roofs
        void BRoof(float2 uv, float4 d1, float4 d2, float seed, float px, out float3 albedo, out float smoothness, out float glass, out float3 glassCol)
        {
            int A = int(d2.z + 0.5), B = int(d2.w + 0.5);
            int rmat = (A >> 3) & 15;
            float3 c = BCol(d2.x);
            float u = uv.x, v = uv.y;
            smoothness = 0.1; glass = 0.0; glassCol = float3(0.0, 0.0, 0.0);
            float big = BFbm(uv * 0.3 + seed * 70.0, 4.0) - 0.5;
            float grain = (BNoise(uv * 37.0, 5.0) + BNoise(uv * 91.0, 6.0) - 1.0) * 0.25 * saturate(0.02 / px);
            c *= 1.0 + big * 0.16;
            bool weather = (B & F_WEATHER) != 0;
            if (rmat == R_CLAY || rmat == R_KAWARA)
            {
                // rows of rounded tiles running down the slope, in overlapping courses
                float w = rmat == R_CLAY ? 0.085 : 0.07, l = rmat == R_CLAY ? 0.14 : 0.11;
                float ci = floor(u / w), fu = u / w - ci;
                float course = floor(v / l), fv = v / l - course;
                float show = saturate(0.045 / px - 0.3);
                float bulge = sin(fu * 3.14159);
                c *= 1.0 + ((bulge - 0.64) * (rmat == R_CLAY ? 0.5 : 0.36) - smoothstep(0.75, 1.0, fv) * 0.22) * show;
                c *= 1.0 + (BRand(float2(ci, course), seed) - 0.5) * (rmat == R_CLAY ? 0.3 : 0.12) * show;
                // old tiles: lichen and soot in patches
                c = lerp(c, c * float3(0.7, 0.72, 0.62), smoothstep(0.55, 0.8, BFbm(uv * 1.1 + seed * 31.0, 9.0)) * (rmat == R_CLAY ? 0.5 : 0.25));
                smoothness = rmat == R_KAWARA ? 0.4 : 0.12;
            }
            else if (rmat == R_SLATE || rmat == R_SHINGLE)
            {
                float w = rmat == R_SLATE ? 0.07 : 0.11, l = rmat == R_SLATE ? 0.06 : 0.07;
                float course = floor(v / l), fv = v / l - course;
                float su = u / w + frac(course * 0.5) + BRand(float2(course, 1.0), seed) * 0.3;
                float ci = floor(su), fu = su - ci;
                float show = saturate(0.04 / px - 0.3);
                c *= 1.0 + (BRand(float2(ci, course), seed) - 0.5) * (rmat == R_SLATE ? 0.2 : 0.3) * show;
                c *= 1.0 - (smoothstep(0.8, 1.0, fv) * 0.3 + (1.0 - saturate(min(fu, 1.0 - fu) / 0.07)) * 0.18) * show;
                c *= 1.0 + grain * (rmat == R_SLATE ? 0.15 : 0.5);
                smoothness = rmat == R_SLATE ? 0.3 : 0.06;
            }
            else if (rmat == R_SEAM)
            {
                // sheet metal (zinc, lead, copper) with standing seams
                float w = 0.17;
                float ci = floor(u / w), fu = u / w - ci;
                float show = saturate(0.08 / px - 0.3);
                c *= 1.0 + (BRand(float2(ci, 2.0), seed) - 0.5) * 0.07;
                float d = min(fu, 1.0 - fu) * w;
                c *= 1.0 - saturate(1.0 - d / max(px, 0.012)) * 0.3 * show + saturate(1.0 - abs(d - 0.02) / max(px, 0.012)) * 0.1 * show;
                c *= 1.0 + (BFbm(uv * 1.7 + seed * 3.0, 8.0) - 0.5) * 0.16;       // weathering in soft patches
                smoothness = 0.36;
            }
            else if (rmat == R_CORRUGATED)
            {
                float show = saturate(0.04 / px - 0.3);
                c *= 1.0 + sin(u / 0.05 * 6.2832) * 0.1 * show;
                float2 sheet = floor(float2(u / 0.45, v / 1.1));
                c *= 1.0 + (BRand(sheet, seed) - 0.5) * 0.14;
                float rust = smoothstep(weather ? 0.5 : 0.68, 0.85, BFbm(uv * 1.3 + seed * 17.0, 9.0) + (BRand(sheet, seed + 2.0) - 0.5) * 0.3);
                c = lerp(c, float3(0.42, 0.25, 0.16) * (0.8 + grain), rust * 0.75);
                smoothness = lerp(0.34, 0.08, rust);
            }
            else if (rmat == R_THATCH)
            {
                c *= 1.0 + (BNoise(float2(u * 55.0, v * 5.0), 6.0) - 0.5) * 0.3 * saturate(0.03 / px) + (BFbm(uv * 2.0, 8.0) - 0.5) * 0.2;
            }
            else
            {
                // flat roofs: felt, concrete, gravel; stains, patches, and the odd puddle mark
                float speck = (BNoise(uv * 70.0, 6.0) - 0.5) * 0.5 * saturate(0.02 / px);
                c *= 1.0 + speck * (rmat == R_GRAVEL ? 0.45 : 0.15) + (BFbm(uv * 0.9 + seed * 9.0, 8.0) - 0.5) * 0.22;
                float stain = smoothstep(0.6, 0.75, BFbm(uv * 0.45 + seed * 23.0, 10.0));
                c *= 1.0 - stain * 0.16;
                if (rmat == R_MEMBRANE)
                {
                    // strips of roofing felt
                    float f = frac(u / 0.9 + seed);
                    c *= 1.0 - (1.0 - saturate(min(f, 1.0 - f) * 0.9 / max(px, 0.012))) * 0.14;
                }
                smoothness = 0.08;
            }

            // dormer windows in a mansard
            if ((B & F_DORMER) != 0 && d1.x > 0.01)
            {
                float bw = d1.x, L = d1.y;
                float bi = floor(u / bw), fx = u / bw - bi;
                float2 p = float2((fx - 0.5) * bw, v);
                if (BRand(float2(bi, 5.0), seed) < 0.88)
                {
                    float hw = min(bw * 0.27, 0.15), y0 = L * 0.16, y1 = L * 0.74;
                    float3 trimC = BCol(d2.y);
                    float dOut = BBox(p, float2(0.0, (y0 + y1) * 0.5 + 0.02), float2(hw + 0.035, (y1 - y0) * 0.5 + 0.05));
                    float dIn = BBox(p, float2(0.0, (y0 + y1) * 0.5), float2(hw, (y1 - y0) * 0.5));
                    float face = BCov(dOut, px);
                    float3 fc = trimC * (p.y > y1 ? 0.8 : 1.0);
                    c = lerp(c, fc, face);
                    c *= 1.0 - BCov(BBox(p, float2(0.0, y0 - 0.05), float2(hw + 0.05, 0.02)), px) * 0.25;      // its shadow on the roof below
                    float g = BCov(dIn + 0.014, px);
                    float bars = max(BBars((p.x + hw) / (2.0 * hw), 2.0, 2.0 * hw, 0.007, px), BBars((p.y - y0) / (y1 - y0), 3.0, y1 - y0, 0.006, px));
                    c = lerp(c, float3(0.9, 0.9, 0.88), BCov(dIn, px) * (1.0 - g));
                    c = lerp(c, float3(0.9, 0.9, 0.88), g * bars);
                    glass = g * (1.0 - bars);
                    glassCol = float3(0.1, 0.13, 0.16) * (0.6 + 0.8 * BRand(float2(bi, 9.0), seed));
                    smoothness = lerp(smoothness, 0.1, face);
                }
            }
            albedo = BLin(saturate(c));
        }

        // ---------------------------------------------------------------- walls
        // What the surface is at one spot. glass: how much of it is window glass (lit as a mirror of the sky).
        void BSurface(float2 uv, float4 d1, float4 d2, bool front, out float3 albedo, out float smoothness, out float glass, out float3 glassCol)
        {
            int A = int(d2.z + 0.5), B = int(d2.w + 0.5);
            int surf = A & 7, mat = (A >> 3) & 15, win = (A >> 7) & 15, shut = (A >> 17) & 3, bal = (A >> 19) & 3, ground = (A >> 21) & 7;
            float ww = 0.25 + 0.09 * float((A >> 11) & 7), wh = 0.3 + 0.085 * float((A >> 14) & 7);
            int cornice = B & 3, tint = (B >> 15) & 7, balMode = (B >> 18) & 3;
            float3 wallC = BCol(d2.x), trimC = BCol(d2.y), accC = BCol(d1.w);
            float seed = frac(d2.x * 0.00013719 + d1.w * 0.00007331 + d2.z * 0.0000311);
            float px = max(max(fwidth(uv.x), fwidth(uv.y)), 1e-4);
            glass = 0.0; glassCol = float3(0.0, 0.0, 0.0);

            if (surf == S_ROOF) { BRoof(uv, d1, d2, seed, px, albedo, smoothness, glass, glassCol); return; }
            if (surf == S_PLAIN || !front)
            {
                // chimneys, tanks, gable ends; and the backs of walls where they stand above a flat roof
                float3 c = BWallStuff(front ? mat : M_STUCCO, wallC, uv, px, seed, smoothness);
                if (!front) c *= 0.88;
                albedo = BLin(saturate(c));
                return;
            }

            float bayW = d1.x, floorH = max(d1.y, 0.05), H = d1.z;
            float u = uv.x, v = uv.y;
            float nF = max(1.0, floor(H / floorH + 0.5));
            float fi = clamp(floor(v / floorH), 0.0, nF - 1.0);
            float fy = v / floorH - fi;
            bool blank = bayW < 0.01;
            float bw = blank ? 1.0 : bayW;
            float bi = blank ? 0.0 : floor(u / bw);
            float fx = blank ? 0.5 : u / bw - bi;
            float2 p = float2((fx - 0.5) * bw, fy * floorH);        // metres within this cell, from the middle of its floor line
            bool isGround = fi < 0.5, isTop = fi > nF - 1.5;
            float rB = BRand(float2(bi, 3.0), seed), rB2 = BRand(float2(bi, 4.0), seed), rB3 = BRand(float2(bi, 5.0), seed);     // one set per bay
            float rW = BRand(float2(bi, fi + 11.0), seed), rW2 = BRand(float2(bi + 57.0, fi), seed), rW3 = BRand(float2(bi + 131.0, fi + 29.0), seed);   // per window
            bool weather = (B & F_WEATHER) != 0;

            // ---- the wall itself
            float3 base = wallC;
            int wmat = mat;
            bool rustic = isGround && ground == G_RUSTIC;
            if (isGround && (B & F_TWOTONE) != 0) base = lerp(wallC, trimC, 0.75);
            if (rustic) { base = lerp(base, trimC, 0.45) * 0.94; if (mat != M_BRICK) wmat = M_STONE; }
            float3 col = BWallStuff(wmat, base, uv, px, seed, smoothness);
            if (rustic)
            {
                // deep grooves between the courses of a rusticated base
                float f = frac(v / 0.17);
                col *= 1.0 - (1.0 - saturate(min(f, 1.0 - f) * 0.17 / max(px, 0.014))) * 0.3;
            }
            if (mat == M_TIMBER && !rustic && !blank)
            {
                // the frame: posts, rails, and a brace in some panels
                float beam = max(BCov(bw * 0.5 - abs(p.x) - 0.03, px), BCov(p.y - 0.045, px));
                beam = max(beam, BCov(abs(p.x) - 0.018, px) * step(rW3, 0.5));
                float brace = abs((fx - 0.5) * (rW2 < 0.5 ? 1.0 : -1.0) - (fy - 0.5)) * min(bw, floorH) * 0.7;
                beam = max(beam, BCov(brace - 0.02, px) * step(rW, 0.35));
                col = lerp(col, trimC * (0.9 + 0.2 * BNoise(uv * float2(30.0, 4.0), 7.0)), beam);
            }
            if (mat == M_CONCRETE && !blank)
            {
                // panel joints
                float j = max(1.0 - saturate((bw * 0.5 - abs(p.x)) / max(px, 0.01)), 1.0 - saturate(p.y / max(px, 0.01)));
                col *= 1.0 - j * 0.16 * saturate(0.05 / px);
            }
            // weather: streaks down from the top, dirt at the foot
            float streak = BNoise(float2(u * 5.0, v * 0.3) + seed * 53.0, 9.0);
            float wAmt = weather ? 1.0 : 0.4;
            col *= 1.0 - wAmt * 0.22 * smoothstep(0.5, 0.95, streak + (BFbm(uv * 0.8 + seed * 5.0, 12.0) - 0.5) * 0.5);
            col *= 1.0 - wAmt * 0.14 * saturate(1.0 - v / 0.3);
            if (v < 0.0)
            {
                albedo = BLin(saturate(col * 0.86));     // the footing, where the ground falls away
                return;
            }

            bool glassWall = win == W_CURTAIN;
            if (!glassWall)
            {
                if ((B & F_STRING) != 0 && fi > 0.5)
                {
                    float sc = BCov(p.y - 0.04, px);
                    col = lerp(col, trimC, sc);
                }
                if ((B & F_PILASTER) != 0 && !blank)
                {
                    float pd = bw * 0.5 - abs(p.x);
                    col = lerp(col, lerp(col, trimC, 0.8), BCov(pd - 0.035, px));
                    col *= 1.0 - saturate(1.0 - abs(pd - 0.045) / max(px, 0.012)) * 0.12;
                }
            }

            // ---- the opening in this cell
            int kind = (blank || win == W_NONE) ? O_NONE : O_WINDOW;
            int wk = win;
            int arch = (win == W_ARCH) ? 1 : (win == W_MOORISH) ? 2 : 0;
            float hw = ww * 0.5 * bw;
            float wHeight = wh * floorH;
            float y0 = floorH * 0.52 - wHeight * 0.5;
            if (wk == W_FRENCH) y0 = min(y0, 0.07 * floorH);
            if (wk == W_SLIDING) y0 = min(y0, 0.2 * floorH);
            if (wk == W_RIBBON) hw = bw * 0.5 + 0.01;
            if (wk == W_BAY) y0 = 0.3 * floorH;
            float y1 = min(y0 + wHeight, floorH * 0.93);
            float3 doorC = lerp(accC, accC * float3(0.8, 0.85, 0.9) + 0.05, rB3);
            bool steps = false;
            if (isTop && nF > 1.5 && (B & F_ATTIC) != 0 && kind == O_WINDOW)
            {
                // a low attic storey under the eaves
                hw *= 0.85; y0 = floorH * 0.3; y1 = floorH * 0.72;
                if (wk == W_FRENCH) wk = W_PLAIN;
            }
            if (isGround && !blank)
            {
                float doorHw = min(0.19, bw * 0.3);
                if (ground == G_SHOP)
                {
                    kind = O_SHOP; arch = 0;
                    hw = bw * 0.5 - 0.035; y0 = 0.0; y1 = floorH * 0.7;
                }
                else if (ground == G_ARCADE)
                {
                    kind = O_ARCADE; arch = 1;
                    hw = bw * 0.36; y0 = -0.05; y1 = max(floorH * 0.86, hw + 0.1);
                }
                else if (ground == G_GARAGE)
                {
                    if (rB < 0.55) { kind = O_GARAGE; arch = 0; hw = min(bw * 0.42, 0.34); y0 = -0.05; y1 = min(floorH * 0.66, 0.62); }
                    else if (rB < 0.8) { kind = O_DOOR; arch = 0; hw = doorHw; y0 = 0.1; y1 = min(floorH * 0.8, 0.82); steps = true; }
                }
                else if (ground == G_STOOP)
                {
                    if (rB < 0.36) { kind = O_DOOR; hw = doorHw; y0 = 0.14; y1 = min(floorH * 0.84, 0.9); steps = true; if (arch == 2) arch = 1; }
                }
                else if (ground == G_RUSTIC)
                {
                    if (rB < 0.22) { kind = O_DOOR; arch = 1; hw = min(0.24, bw * 0.34); y0 = 0.0; y1 = floorH * 0.82; }
                    else if (kind == O_WINDOW) { y0 = max(y0, floorH * 0.3); if (wk == W_FRENCH || wk == W_SLIDING) wk = W_PLAIN; }
                }
                else if (ground == G_BLANK)
                {
                    kind = O_NONE;
                    if (rB < 0.16) { kind = O_DOOR; arch = 0; hw = doorHw; y0 = 0.0; y1 = min(floorH * 0.75, 0.8); }
                }
                else
                {
                    if (rB < 0.2) { kind = O_DOOR; hw = doorHw; y0 = 0.0; y1 = min(floorH * 0.8, 0.84); if (arch == 2) arch = 1; }
                    else if (kind == O_WINDOW && (wk == W_FRENCH || wk == W_SLIDING)) y0 = max(y0, floorH * 0.28);
                }
            }
            // some windows were never made, or have been walled up
            if (kind == O_WINDOW && !isGround && rW3 > (weather ? 0.93 : 0.975) && !glassWall && wk != W_RIBBON) kind = O_NONE;

            float d = 1e3;
            if (kind != O_NONE)
            {
                d = max(abs(p.x) - hw, max(y0 - p.y, p.y - y1));
                if (arch == 1)
                {
                    float cy = max(y1 - hw, y0);
                    if (p.y > cy) d = max(length(float2(p.x, p.y - cy)) - hw, y0 - p.y);
                }
                else if (arch == 2)
                {
                    // a pointed arch: where two wide circles overlap
                    float R = hw * 1.6, cy = max(y1 - hw * 1.48, y0);
                    if (p.y > cy) d = max(max(length(float2(p.x + R - hw, p.y - cy)) - R, length(float2(p.x - R + hw, p.y - cy)) - R), y0 - p.y);
                }
            }
            float inside = BCov(d, px);

            if (glassWall && !(isGround && kind == O_SHOP))
            {
                // a curtain wall: glass from floor to floor, a band of opaque panel at each slab, thin frames
                float span = 0.15 * floorH;
                float frame = max(1.0 - saturate((bw * 0.5 - abs(p.x) - 0.011) / px + 0.5), BCov(abs(p.y - span) - 0.009, px));
                frame = max(frame, BCov(abs(p.y - floorH * 0.62) - 0.006, px) * step(0.5, BRand(float2(2.0, 3.0), seed)));
                float3 tintC = tint == 1 ? float3(0.10, 0.2, 0.33) : tint == 2 ? float3(0.1, 0.25, 0.23) : tint == 3 ? float3(0.27, 0.2, 0.12) : tint == 4 ? float3(0.4, 0.48, 0.55) : float3(0.1, 0.13, 0.16);
                float sp = BCov(p.y - span, px);
                float3 panel = lerp(tintC * 0.7, wallC, 0.35);
                float blind = step(0.86, rW2) * saturate((p.y - floorH * (1.0 - 0.5 * rW)) / px + 0.5);
                col = lerp(tintC, panel, sp);
                col = lerp(col, float3(0.8, 0.79, 0.74), blind * (1.0 - sp) * 0.55);
                col = lerp(col, lerp(wallC, accC, 0.5) * 0.8, frame);
                glass = (1.0 - frame) * (1.0 - sp * 0.35);
                glassCol = col * (0.75 + 0.5 * rW);
                smoothness = 0.8;
                albedo = BLin(saturate(col));
                return;
            }

            if (kind == O_WINDOW || kind == O_DOOR)
            {
                bool dressed = (B & F_SURROUND) != 0 || (kind == O_DOOR && arch != 0);
                if (dressed) col = lerp(col, trimC, BCov(d - 0.042, px));
                if ((B & F_LINTEL) != 0 && arch == 0)
                {
                    float l = BCov(BBox(p, float2(0.0, y1 + 0.035), float2(hw + 0.05, 0.03)), px);
                    col = lerp(col, trimC * 1.03, l);
                }
                if ((B & F_SILL) != 0 && kind == O_WINDOW && y0 > 0.12)
                {
                    col *= 1.0 - BCov(BBox(p, float2(0.0, y0 - 0.06), float2(hw + 0.04, 0.022)), px) * 0.2;
                    col = lerp(col, trimC * 1.04, BCov(BBox(p, float2(0.0, y0 - 0.018), float2(hw + 0.045, 0.02)), px));
                }
                if (steps)
                {
                    // a few steps up to the door
                    float st = BCov(BBox(p, float2(0.0, y0 * 0.5), float2(hw + 0.06, y0 * 0.5)), px);
                    float tread = frac(p.y / max(y0, 0.01) * 3.0);
                    col = lerp(col, float3(0.62, 0.6, 0.57) * (0.75 + 0.3 * tread), st);
                }
            }

            if (kind == O_WINDOW)
            {
                // ---- shutters beside the window (or closed over it)
                bool closed = false;
                if (shut == SH_LOUVER)
                {
                    float sw = min(hw * 0.98, bw * 0.5 - hw - 0.012);
                    closed = rW3 < 0.16;
                    float3 sc = accC * (0.9 + 0.2 * rW2) * (1.0 - BStripes(p.y, 0.032, 0.012, px) * 0.3);
                    if (!closed && sw > 0.03)
                    {
                        float leaf = BCov(BBox(float2(abs(p.x), p.y), float2(hw + sw * 0.5 + 0.004, (y0 + y1) * 0.5), float2(sw * 0.5, (y1 - y0) * 0.5)), px);
                        leaf *= (p.x < 0.0 && rW3 > 0.94) ? 0.0 : 1.0;      // one leaf has gone
                        col = lerp(col, sc, leaf);
                    }
                    else if (closed) { col = lerp(col, sc * (1.0 - BCov(abs(p.x) - 0.006, px) * 0.4), inside); }
                }
                if (!closed)
                {
                    float ft = (wk == W_SLIDING || wk == W_RIBBON || wk == W_GRID) ? 0.012 : 0.022;
                    float3 frameC = (wk == W_SLIDING || wk == W_RIBBON) ? accC : (wk == W_GRID ? accC * 0.55 : lerp(trimC, float3(0.95, 0.95, 0.93), dot(trimC, float3(0.33, 0.33, 0.33)) > 0.45 ? 0.6 : 0.0));
                    float g = BCov(d + ft, px);
                    float w2 = 2.0 * hw, h2 = y1 - y0;
                    float gx = (p.x + hw) / w2, gy = (p.y - y0) / h2;
                    float bars = 0.0;
                    float sideShade = 1.0;
                    if (wk == W_FRENCH) bars = max(BBars(gx, 2.0, w2, 0.011, px), BBars(gy, 4.0, h2, 0.006, px));
                    else if (wk == W_SASH) bars = max(BBars(gy, 2.0, h2, 0.012, px), BRand(float2(5.0, 6.0), seed) < 0.45 ? max(BBars(gx, 2.0, w2, 0.005, px), BBars(gy, 4.0, h2, 0.005, px)) : 0.0);
                    else if (wk == W_PLAIN) bars = w2 > 0.26 ? BBars(gx, w2 > 0.52 ? 3.0 : 2.0, w2, 0.01, px) : 0.0;
                    else if (wk == W_ARCH) bars = max(BBars(gx, 2.0, w2, 0.01, px), BCov(abs(p.y - (y1 - hw)) - 0.008, px));
                    else if (wk == W_RIBBON) bars = BBars(gx, max(1.0, floor(w2 / 0.3 + 0.5)), w2, 0.009, px);
                    else if (wk == W_SMALL) bars = rB2 < 0.5 ? max(BBars(gx, 2.0, w2, 0.008, px), BBars(gy, 2.0, h2, 0.008, px)) : 0.0;
                    else if (wk == W_BAY)
                    {
                        // a bay window: the two side lights are turned away, so one is brighter and one darker
                        bars = max(max(BCov(abs(gx - 0.24) * w2 - 0.014, px), BCov(abs(gx - 0.76) * w2 - 0.014, px)), BBars(gy, 2.0, h2, 0.01, px));
                        sideShade = gx < 0.24 ? 1.25 : gx > 0.76 ? 0.7 : 1.0;
                    }
                    else if (wk == W_GRID) bars = max(BBars(gx, max(2.0, floor(w2 / 0.12 + 0.5)), w2, 0.006, px), BBars(gy, max(2.0, floor(h2 / 0.14 + 0.5)), h2, 0.006, px));
                    else if (wk == W_SLIDING) bars = BBars(gx, w2 > 0.6 ? 3.0 : 2.0, w2, 0.011, px);

                    float3 tintC = tint == 1 ? float3(0.10, 0.2, 0.33) : tint == 2 ? float3(0.1, 0.25, 0.23) : tint == 3 ? float3(0.27, 0.2, 0.12) : tint == 4 ? float3(0.4, 0.48, 0.55) : float3(0.1, 0.13, 0.16);
                    float3 gcol = tintC * (0.55 + 0.9 * rW) * sideShade;
                    // blinds, curtains, a roller shutter part way down
                    float cover = 0.0;
                    float3 coverC = float3(0.84, 0.82, 0.76) * (0.8 + 0.25 * rW3);
                    if (shut == SH_ROLLER)
                    {
                        float down = rW2 < 0.3 ? 0.0 : rW2 < 0.55 ? 0.3 : rW2 < 0.8 ? 0.62 : 1.0;
                        cover = saturate((gy - (1.0 - down)) * h2 / px + 0.5) * step(0.01, down);
                        coverC = lerp(float3(0.8, 0.8, 0.78), accC, 0.5) * (1.0 - BStripes(p.y, 0.028, 0.008, px) * 0.25);
                    }
                    else if (shut == SH_SCREEN && rW2 < 0.62)
                    {
                        // a wooden lattice over the whole window
                        float lat = max(BStripes(p.x + p.y, 0.03, 0.012, px), BStripes(p.x - p.y, 0.03, 0.012, px));
                        cover = lerp(0.72, 1.0, lat);
                        coverC = accC * (0.55 + 0.45 * lat);
                    }
                    else
                    {
                        float blind = rW2 < 0.42 ? rW * 0.75 : 0.0;
                        cover = saturate((gy - (1.0 - blind)) * h2 / px + 0.5) * step(0.01, blind) * 0.9;
                        float curtain = rW2 > 0.66 ? 0.2 + 0.2 * rW : 0.0;
                        float cc = saturate((abs(gx - 0.5) - (0.5 - curtain)) * w2 / px + 0.5) * step(0.01, curtain);
                        float3 cloth = lerp(float3(0.86, 0.84, 0.8), 0.35 + 0.5 * float3(rW, rW3, rB3), 0.35) * (0.85 + 0.15 * sin(p.x * 180.0));
                        coverC = lerp(coverC, cloth, cc * (1.0 - cover));
                        cover = max(cover, cc * 0.85);
                    }
                    float3 fill = lerp(gcol, coverC, cover);
                    // the reveal: the top and one side of the opening lie in shadow
                    float shade = 1.0 - 0.45 * saturate(1.0 - (y1 - p.y) / 0.05) - 0.2 * saturate(1.0 - (p.x + hw) / 0.035);
                    col = lerp(col, frameC, inside);
                    col = lerp(col, fill * shade, g);
                    col = lerp(col, frameC * 0.96, g * bars);
                    float gl = g * (1.0 - bars) * (1.0 - cover);
                    if ((B & F_GRILLE) != 0 && (isGround || rB2 > 0.55))
                    {
                        float bar = BStripes(p.x, 0.045, 0.009, px) * inside;
                        col = lerp(col, float3(0.09, 0.09, 0.1), bar * 0.85);
                        gl *= 1.0 - bar;
                    }
                    glass = gl;
                    glassCol = gcol * shade;
                    if (wk == W_BAY)
                    {
                        // the panel under a bay window, and the shade of its sides on the wall
                        float pn = BCov(BBox(p, float2(0.0, y0 * 0.5 + 0.02), float2(hw, y0 * 0.5 - 0.03)), px);
                        col = lerp(col, lerp(col, trimC, 0.35) * (gx < 0.24 ? 1.12 : gx > 0.76 ? 0.8 : 1.0), pn);
                        col *= 1.0 - BCov(BBox(p, float2(hw + 0.03, floorH * 0.5), float2(0.03, floorH * 0.5)), px) * 0.22;
                    }
                }
                // an awning over the window
                if ((B & F_AWNING) != 0 && !isGround && rW3 > 0.3 && rW3 < 0.55)
                {
                    float aw = BCov(BBox(p, float2(0.0, y1 - 0.02), float2(hw + 0.03, 0.065)), px);
                    float3 ac = lerp(accC, float3(0.9, 0.88, 0.8), BStripes(p.x, 0.05, 0.025, px) * step(0.5, rB3));
                    col = lerp(col, ac * (0.8 + 0.3 * saturate((p.y - y1 + 0.085) / 0.13)), aw);
                    col *= 1.0 - BCov(BBox(p, float2(0.0, y1 - 0.13), float2(hw + 0.03, 0.045)), px) * (1.0 - aw) * 0.3;
                    glass *= 1.0 - aw;
                }
                // an air conditioner under it
                if ((B & F_AC) != 0 && rW3 < 0.3 && y0 > 0.16)
                {
                    float2 c = float2(hw * (rW2 < 0.5 ? 0.45 : -0.45), y0 - 0.07);
                    float box = BCov(BBox(p, c, float2(0.075, 0.048)), px);
                    float fan = BCov(length(p - c - float2(0.02, 0.0)) - 0.034, px);
                    col = lerp(col, float3(0.8, 0.8, 0.77) * (1.0 - fan * 0.55), box);
                    col *= 1.0 - BCov(BBox(p, c - float2(0.0, 0.06), float2(0.075, 0.014)), px) * (1.0 - box) * 0.25;
                }
            }
            else if (kind == O_DOOR)
            {
                float ft = 0.02;
                float g = BCov(d + ft, px);
                float h2 = y1 - y0;
                float3 dc = doorC;
                // panels, and a light over the door
                float panel = BCov(BBox(float2(abs(p.x), p.y), float2(hw * 0.48, y0 + h2 * 0.26), float2(hw * 0.3, h2 * 0.17)), px) + BCov(BBox(float2(abs(p.x), p.y), float2(hw * 0.48, y0 + h2 * 0.62), float2(hw * 0.3, h2 * 0.12)), px);
                dc *= 1.0 - saturate(panel) * 0.16;
                dc *= 1.0 - BCov(abs(p.x) - 0.004, px) * 0.3 * step(0.5, rB2);
                float light = BCov(BBox(p, float2(0.0, y0 + h2 * 0.89), float2(hw - 0.03, h2 * 0.07)), px) * step(arch == 0 ? 0.35 : 2.0, rB3) + (arch != 0 ? step(y1 - hw, p.y) * g : 0.0);
                float shade = 1.0 - 0.4 * saturate(1.0 - (y1 - p.y) / 0.06);
                col = lerp(col, lerp(trimC, float3(0.9, 0.9, 0.88), 0.3), inside);
                col = lerp(col, dc * shade, g);
                col = lerp(col, float3(0.1, 0.12, 0.14), g * saturate(light));
                glass = g * saturate(light) * 0.8;
                glassCol = float3(0.1, 0.12, 0.14);
            }
            else if (kind == O_SHOP)
            {
                bool entrance = rB < 0.22;
                float3 sf = lerp(float3(0.1, 0.1, 0.11), accC * 0.5, step(0.5, BRand(float2(8.0, 1.0), seed)));     // the shop front's frame
                float riser = entrance ? 0.0 : 0.075 + 0.05 * rB2;
                float gd = max(abs(p.x) - (hw - 0.014), max(riser - p.y, p.y - (y1 - 0.014)));
                float g = BCov(gd, px);
                float w2 = 2.0 * hw;
                float bars = entrance ? BBars((p.x + hw) / w2, 3.0, w2, 0.008, px) : BBars((p.x + hw) / w2, max(1.0, floor(w2 / 0.4 + 0.5)), w2, 0.008, px);
                bars = max(bars, BCov(abs(p.y - y1 * 0.8) - 0.006, px) * step(0.4, rB3));
                col = lerp(col, sf, inside);
                // what is in the window: a bright mess of goods low down, dark above
                float2 shelf = float2(p.x / 0.07, p.y / 0.085);
                float2 item = floor(shelf) + bi * 13.0, gap = frac(shelf);
                float3 goods = (0.2 + 0.65 * float3(BRand(item, 21.0 + seed), BRand(item, 22.0 + seed), BRand(item, 23.0 + seed))) * step(0.14, gap.x) * step(0.22, gap.y);
                goods = lerp(dot(goods, float3(0.33, 0.33, 0.33)) * float3(1.05, 1.0, 0.92), goods, 0.4);       // muted, as seen through glass
                goods = lerp(float3(0.27, 0.25, 0.23), goods, saturate(0.02 / px - 0.3));
                float3 inner = lerp(float3(0.13, 0.13, 0.14), goods, saturate(1.6 - p.y / (y1 * 0.4)) * (entrance ? 0.0 : 0.85));
                inner *= 1.0 - 0.4 * saturate(1.0 - (y1 - p.y) / 0.08);
                col = lerp(col, inner, g);
                col = lerp(col, sf, g * bars);
                glass = g * (1.0 - bars) * 0.5;
                glassCol = inner;
                // the name board over it, each shop its own colour
                float3 signC = 0.12 + 0.75 * float3(BRand(float2(bi, 31.0), seed), BRand(float2(bi, 32.0), seed), BRand(float2(bi, 33.0), seed));
                signC = lerp(signC, dot(signC, float3(0.33, 0.33, 0.33)) > 0.5 ? float3(0.93, 0.91, 0.86) : float3(0.11, 0.12, 0.13), 0.45);
                float sb = BCov(BBox(p, float2(0.0, y1 + 0.075), float2(hw, 0.05)), px);
                float letters = BCov(BBox(p, float2(0.0, y1 + 0.075), float2(hw * (0.35 + 0.4 * rB2), 0.02)), px) * step(0.45, BNoise(float2(p.x * 55.0 + bi * 7.0, 1.0), 24.0)) * saturate(0.012 / px);
                col = lerp(col, signC, sb);
                col = lerp(col, dot(signC, float3(0.33, 0.33, 0.33)) > 0.45 ? float3(0.1, 0.1, 0.1) : float3(0.95, 0.93, 0.85), letters);
                if ((B & F_AWNING) != 0 && rB2 < 0.55 && !entrance)
                {
                    float3 ac = lerp(lerp(accC, signC, 0.5), float3(0.92, 0.9, 0.84), BStripes(p.x, 0.07, 0.035, px) * step(0.55, rB3));
                    float aw = BCov(BBox(p, float2(0.0, y1 - 0.05), float2(hw + 0.02, 0.085)), px);
                    col = lerp(col, ac * (0.72 + 0.4 * saturate((p.y - y1 + 0.135) / 0.17)), aw);
                    col *= 1.0 - BCov(BBox(p, float2(0.0, y1 - 0.19), float2(hw + 0.02, 0.055)), px) * (1.0 - aw) * 0.35;
                    glass *= 1.0 - aw;
                }
            }
            else if (kind == O_ARCADE)
            {
                // a deep, shaded arch with a lit floor and a shop window far inside
                float3 dark = base * 0.2 * (1.0 + 0.9 * saturate(1.0 - p.y / (y1 * 0.6)));
                float back = BCov(BBox(p, float2(0.0, y1 * 0.3), float2(hw * 0.62, y1 * 0.3)), px);
                dark = lerp(dark, float3(0.16, 0.17, 0.18) + 0.1 * float3(rB, rB2, rB3), back * 0.7);
                col = lerp(col, trimC * 0.97, BCov(d - 0.04, px) * (1.0 - inside) * ((B & F_SURROUND) != 0 ? 1.0 : 0.5));
                col = lerp(col, dark, inside);
            }
            else if (kind == O_GARAGE)
            {
                float3 gc = lerp(float3(0.9, 0.89, 0.86), doorC, rB2 < 0.5 ? 0.12 : 0.8);
                float rib = BStripes(p.y - y0, 0.115, 0.012, px);
                float lights = BCov(BBox(float2(abs(frac((p.x + hw) / (2.0 * hw) * 4.0) - 0.5) * hw * 0.5, p.y), float2(0.0, y1 - 0.09), float2(hw * 0.085, 0.028)), px) * step(0.6, rB3);
                float shade = 1.0 - 0.4 * saturate(1.0 - (y1 - p.y) / 0.05);
                col = lerp(col, trimC * 0.9, inside);
                col = lerp(col, gc * (1.0 - rib * 0.22) * shade, BCov(d + 0.016, px));
                col = lerp(col, float3(0.1, 0.12, 0.14), lights * BCov(d + 0.016, px));
            }

            // ---- balconies
            if (bal != 0 && !blank && !isGround && win != W_NONE)
            {
                bool here = balMode == BM_ALL ? true : balMode == BM_PARIS ? (abs(fi - (nF > 4.5 ? 2.0 : 1.0)) < 0.5 || (nF > 3.5 && isTop)) : balMode == BM_COLUMNS ? rB < 0.55 : frac((bi + fi) * 0.5) < 0.25;
                bool above = balMode == BM_ALL ? !isTop : balMode == BM_PARIS ? (abs(fi + 1.0 - (nF > 4.5 ? 2.0 : 1.0)) < 0.5 || (nF > 3.5 && fi > nF - 2.5 && !isTop)) : balMode == BM_COLUMNS ? (rB < 0.55 && !isTop) : frac((bi + fi + 1.0) * 0.5) < 0.25 && !isTop;
                bool continuous = balMode == BM_PARIS || (balMode == BM_ALL && bal == B_SLAB);
                float reach = continuous ? bw : min(bw * 0.5 - 0.02, hw + (bal == B_IRON ? 0.05 : 0.12));
                if (here)
                {
                    float railH = min(0.36, floorH * 0.3);
                    float slab = BCov(BBox(p, float2(0.0, 0.012), float2(reach + 0.012, 0.024)), px);
                    float rd = BBox(p, float2(0.0, 0.036 + railH * 0.5), float2(reach, railH * 0.5));
                    float rail = BCov(rd, px);
                    if (bal == B_IRON)
                    {
                        // wrought iron: a top rail, close bars, a curl of ornament along the middle
                        float bars = max(BStripes(p.x, 0.034, 0.008, px), BCov(abs(p.y - 0.036 - railH) - 0.008, px));
                        bars = max(bars, BCov(abs(p.y - 0.036 - railH * 0.5 - sin(p.x * 95.0) * railH * 0.18) - 0.006, px));
                        float a = rail * max(bars, 0.3 * saturate(px / 0.02));
                        col = lerp(col, float3(0.07, 0.07, 0.08), a * 0.92);
                        glass *= 1.0 - a;
                    }
                    else if (bal == B_SLAB)
                    {
                        // a solid or glazed front
                        float kindB = BRand(float2(4.0, 9.0), seed);
                        float3 bc = kindB < 0.5 ? lerp(wallC, trimC, 0.5) * 1.03 : kindB < 0.75 ? accC : float3(0.3, 0.38, 0.42);
                        bc *= 1.0 + saturate(1.0 - (0.036 + railH - p.y) / 0.02) * 0.12;
                        if (kindB >= 0.5 && kindB < 0.75) bc *= 1.0 - BStripes(p.x, 0.04, 0.008, px) * 0.35;
                        col = lerp(col, bc * (0.86 + 0.14 * saturate((p.y - 0.036) / railH)), rail);
                        glass *= 1.0 - rail;
                        // washing, plants and clutter show over the top here and there
                        if (rW3 > 0.72 && weather)
                        {
                            float cl = BCov(BBox(p, float2((rW - 0.5) * reach, 0.036 + railH + 0.03), float2(reach * 0.3, 0.04)), px);
                            col = lerp(col, 0.25 + 0.6 * float3(rW, rW2, rW3), cl * 0.8);
                            glass *= 1.0 - cl;
                        }
                    }
                    else
                    {
                        // timber balusters
                        float bars = max(BStripes(p.x, 0.05, 0.022, px), BCov(abs(p.y - 0.036 - railH) - 0.014, px));
                        float a = rail * max(bars, 0.45 * saturate(px / 0.03));
                        col = lerp(col, accC * 0.85, a);
                        glass *= 1.0 - a;
                    }
                    col = lerp(col, lerp(trimC, float3(0.8, 0.79, 0.76), 0.4) * 0.95, slab);
                    glass *= 1.0 - slab;
                }
                if (above && abs(p.x) < reach + 0.012)
                {
                    // the shadow of the balcony overhead
                    float sh = saturate(1.0 - (floorH - p.y) / 0.11);
                    col *= 1.0 - sh * 0.38;
                    glassCol *= 1.0 - sh * 0.3;
                }
            }

            // ---- a fire escape down one bay
            if ((B & F_FIRE) != 0 && !blank && !isGround && rB > 0.8 && nF > 2.5)
            {
                float railH = min(0.3, floorH * 0.26);
                float deck = BCov(BBox(p, float2(0.0, 0.015), float2(bw * 0.5, 0.015)), px);
                float rail = BCov(BBox(p, float2(0.0, 0.03 + railH * 0.5), float2(bw * 0.5, railH * 0.5)), px) * max(max(BStripes(p.x, 0.04, 0.007, px), BCov(abs(p.y - 0.03 - railH) - 0.007, px)), 0.25 * saturate(px / 0.02));
                float ladder = BCov(abs((fx - 0.2) * bw - (fy - 0.02) * bw * 0.55) - 0.035, px) * max(BStripes(p.y, 0.06, 0.012, px), max(BCov(abs(abs((fx - 0.2) * bw - (fy - 0.02) * bw * 0.55) - 0.03) - 0.005, px), 0.2 * saturate(px / 0.02)));
                float a = saturate(max(max(deck, rail), isTop ? 0.0 : ladder));
                col = lerp(col, float3(0.08, 0.075, 0.07) + accC * 0.06, a * 0.92);
                col *= 1.0 - saturate(1.0 - (floorH - p.y) / 0.07) * 0.2 * (isTop ? 0.0 : 1.0);
                glass *= 1.0 - a;
            }

            // ---- the top of the wall
            if (cornice != 0 && !glassWall)
            {
                float topD = H - v;
                if (cornice == 1) col = lerp(col, trimC, BCov(topD - 0.075, px));
                else if (cornice == 2)
                {
                    // a heavy cornice: mouldings, a row of dentils, and its shadow on the wall
                    float band = BCov(topD - 0.17, px);
                    float dent = BCov(abs(topD - 0.125) - 0.02, px) * BStripes(u, 0.055, 0.027, px);
                    float3 cc = trimC * (1.0 - 0.14 * saturate((topD - 0.06) / 0.11)) * (1.0 - dent * 0.3);
                    cc *= 1.0 - BCov(abs(topD - 0.062) - 0.006, px) * 0.25;
                    col *= 1.0 - saturate(1.0 - (topD - 0.17) / 0.09) * (1.0 - band) * 0.34;
                    col = lerp(col, cc, band);
                    glass *= 1.0 - band;
                }
                else
                {
                    // a stepped or battlemented crest
                    float band = BCov(topD - 0.14, px);
                    float gap = BStripes(u, 0.16, 0.08, px) * BCov(topD - 0.06, px);
                    col = lerp(col, lerp(trimC, base, 0.3) * (1.0 - gap * 0.45), band);
                }
            }
            else if (!glassWall)
            {
                // a plain capping along the top
                col *= 1.0 - BCov(H - v - 0.02, px) * 0.15;
            }

            albedo = BLin(saturate(col));
        }

        struct BAttributes
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
            Name "Building"
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
                float2 uv0 : TEXCOORD0;
                nointerpolation float4 uv1 : TEXCOORD1;
                nointerpolation float4 uv2 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
                float fog : TEXCOORD5;
            };

            Varyings vert(BAttributes v)
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

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                if (!front) n = -n;
                float3 albedo; float smoothness; float glass; float3 glassCol;
                BSurface(i.uv0, i.uv1, i.uv2, front, albedo, smoothness, glass, glassCol);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = sun.shadowAttenuation;
                half ndlRaw = dot(n, sun.direction);
                half ndl = saturate(ndlRaw);
                half3 sky = SampleSH(n);
                half3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half3 Hv = normalize(sun.direction + V);
                half spec = ToonSpec(dot(n, Hv), smoothness);
                half3 col = ToonLight(ToonPop(albedo), ndlRaw, shadow, sun.color, sky, 1.0) + sun.color * spec * shadow;
                col += ToonRim(n, V, sun.direction, sky, 0.12) * albedo;

                if (glass > 0.001)
                {
                    // window glass: the room behind, and over it a mirror of the sky and of the street opposite
                    half3 R = reflect(-V, n);
                    half lum = dot(sky, half3(0.3, 0.5, 0.2)) + dot(sun.color, half3(0.3, 0.5, 0.2)) * 0.35;
                    half3 horizon = half3(0.62, 0.72, 0.84), zenith = half3(0.2, 0.36, 0.7);
                    half3 env = lerp(horizon, zenith, saturate(R.y * 1.3)) * lum * 1.25;
                    half3 street = half3(0.085, 0.082, 0.08) * lum;
                    half opp = BNoise(i.positionWS.xz * 0.45 + R.xz * 3.0, 3.0);
                    half blocked = saturate((0.32 * opp + 0.03 - R.y) * 9.0);
                    env = lerp(env, street * (0.7 + 0.8 * opp), blocked);
                    half fres = 0.1 + 0.9 * pow(1.0 - saturate(dot(n, V)), 3.0);
                    half3 room = BLin(saturate(glassCol)) * (sun.color * (ndl * shadow * 0.5 + 0.12) + sky * 0.7);
                    half glint = pow(saturate(dot(n, Hv)), 600.0) * 2.5 * shadow;
                    half3 g = lerp(room, env, saturate(fres * 0.9 + 0.1)) + sun.color * glint;
                    col = lerp(col, g, glass);
                }
                col = MixFog(col, i.fog);
                return half4(col, 1);
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
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(BAttributes v)
            {
                Vo o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, _MainLightPosition.xyz));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half frag(Vo i) : SV_Target { return 0; }
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
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(BAttributes v) { Vo o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Vo i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            struct Vo { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Vo vert(BAttributes v) { Vo o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Vo i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = NormalizeNormalPerPixel(i.normalWS);
                return half4(front ? n : -n, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
