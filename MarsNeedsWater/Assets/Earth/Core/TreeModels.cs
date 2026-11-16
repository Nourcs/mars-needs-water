using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    public struct V3
    {
        public float x, y, z;
        public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static V3 operator +(V3 a, V3 b) { return new V3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static V3 operator -(V3 a) { return new V3(-a.x, -a.y, -a.z); }
        public static V3 operator *(V3 a, float k) { return new V3(a.x * k, a.y * k, a.z * k); }
        public static V3 operator *(float k, V3 a) { return new V3(a.x * k, a.y * k, a.z * k); }
        public static float Dot(V3 a, V3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static V3 Cross(V3 a, V3 b) { return new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public float Len { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }
        public V3 N { get { float l = Len; return l > 1e-8f ? this * (1f / l) : new V3(0, 1, 0); } }
        public static readonly V3 Up = new V3(0, 1, 0);
        public static V3 Lerp(V3 a, V3 b, float t) { return a + (b - a) * t; }
    }

    /// <summary>
    /// One tree model, 1 unit tall (scaled per tree). Per vertex: position, normal, uvw and four bytes:
    /// uv is a leaf atlas spot (w = 10), a bark texture spot (w = bark layer 0..7, repeating) or an impostor
    /// spot (w = 20); the bytes are how much it sways in the wind (0 on the trunk, 1 at twig tips), leaf
    /// (255 on leaves, which flutter), how much light reaches it (ambient occlusion), and a wind phase.
    /// </summary>
    public sealed class TreeMesh
    {
        public readonly List<float> P = new List<float>(), N = new List<float>(), U = new List<float>();
        public readonly List<byte> C = new List<byte>();
        public readonly List<int> I = new List<int>();
        public int Count { get { return P.Count / 3; } }
        public float HalfX, HalfZ, Top = 1;

        public int Add(V3 p, V3 n, float u, float v, float w, float wind, float leaf, float ao, float phase)
        {
            P.Add(p.x); P.Add(p.y); P.Add(p.z); N.Add(n.x); N.Add(n.y); N.Add(n.z); U.Add(u); U.Add(v); U.Add(w);
            C.Add(B(wind)); C.Add(B(leaf)); C.Add(B(ao)); C.Add(B(phase - (float)Math.Floor(phase)));
            return Count - 1;
        }

        static byte B(float v) { int i = (int)(v * 255f + 0.5f); return (byte)(i < 0 ? 0 : i > 255 ? 255 : i); }

        V3 Pos(int i) { return new V3(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]); }

        /// <summary>A triangle facing the given way (Unity: clockwise is the front).</summary>
        public void Tri(int a, int b, int c, V3 facing)
        {
            V3 pa = Pos(a), n = V3.Cross(Pos(b) - pa, Pos(c) - pa);
            if (V3.Dot(n, facing) < 0) { int t = b; b = c; c = t; }
            I.Add(a); I.Add(b); I.Add(c);
        }

        public void Scale(float k)
        {
            for (int i = 0; i < P.Count; i++) P[i] *= k;
        }

        public void Measure()
        {
            HalfX = HalfZ = 0; Top = 0;
            for (int i = 0; i < Count; i++)
            {
                HalfX = Math.Max(HalfX, Math.Abs(P[i * 3]));
                HalfZ = Math.Max(HalfZ, Math.Abs(P[i * 3 + 2]));
                Top = Math.Max(Top, P[i * 3 + 1]);
            }
        }

        public int Triangles { get { return I.Count / 3; } }
    }

    /// <summary>
    /// Builds every tree model in code: real branching skeletons (trunk, limbs, branches, twigs) clothed in leaf
    /// cards from the TreeTextures atlas, three levels of detail each: full (up close), lighter (fewer, bigger
    /// cards, no thin twigs) and an impostor (two crossed pictures of the tree) for the distance.
    /// The same seed always gives the same tree, at every level.
    /// </summary>
    public static class TreeModels
    {
        public const int Lods = 3;

        sealed class Gen
        {
            public readonly TreeMesh M = new TreeMesh();
            public readonly Random R;       // the tree's shape: drawn the same at every level of detail
            public readonly Random K;       // which cards a lighter level keeps
            public readonly int Lod;
            public V3 EnvC, EnvR;           // the crown's rough oval (for leaf lighting and shade)
            public float CardKeep = 1f, CardGrow = 1f, MinTube = 0f;
            public int TrunkSides = 10, LimbSides = 6, TwigSides = 4;

            public Gen(int seed, int lod)
            {
                R = new Random(seed); K = new Random(seed * 7 + 3); Lod = lod;
                if (lod >= 1) { CardKeep = 0.38f; CardGrow = 1.6f; MinTube = 0.0065f; TrunkSides = 6; LimbSides = 4; TwigSides = 3; }
            }

            public float Rf(float a, float b) { return a + (float)R.NextDouble() * (b - a); }

            public V3 RandUnit()
            {
                while (true)
                {
                    var v = new V3(Rf(-1, 1), Rf(-1, 1), Rf(-1, 1));
                    float l = v.Len;
                    if (l > 0.05f && l <= 1) return v * (1f / l);
                }
            }

            public float Env(V3 p)
            {
                V3 d = p - EnvC;
                return (float)Math.Sqrt(d.x * d.x / (EnvR.x * EnvR.x) + d.y * d.y / (EnvR.y * EnvR.y) + d.z * d.z / (EnvR.z * EnvR.z));
            }

            public V3 EnvNormal(V3 p)
            {
                V3 d = p - EnvC;
                return new V3(d.x / (EnvR.x * EnvR.x), d.y / (EnvR.y * EnvR.y), d.z / (EnvR.z * EnvR.z)).N;
            }

            public float Shade(V3 p)
            {
                float e = Math.Min(1f, Env(p));
                float ao = 0.38f + 0.62f * (float)Math.Pow(e, 0.9);
                return ao * (0.82f + 0.18f * Math.Min(1f, p.y / Math.Max(0.1f, EnvC.y + EnvR.y)));
            }

            /// <summary>A tube along a path, radius per point; wind weight from w0 at the base to w1 at the tip.</summary>
            public void Tube(List<V3> pts, List<float> rad, int sides, int bark, float w0, float w1, float phase, bool cap = false)
            {
                if (pts.Count < 2 || rad[0] < MinTube) return;
                float circ = (float)(2 * Math.PI * rad[0]);
                float vScale = 1f / Math.Max(1e-4f, circ * 2f);
                float len = 0;
                V3 t0 = (pts[1] - pts[0]).N;
                V3 side = Math.Abs(t0.y) < 0.9f ? V3.Cross(t0, V3.Up).N : V3.Cross(t0, new V3(1, 0, 0)).N;
                int prev = -1;
                for (int i = 0; i < pts.Count; i++)
                {
                    V3 t = i == 0 ? (pts[1] - pts[0]).N : i == pts.Count - 1 ? (pts[i] - pts[i - 1]).N : (pts[i + 1] - pts[i - 1]).N;
                    // carry the frame along the path without twisting
                    side = (side - t * V3.Dot(side, t)).N;
                    V3 other = V3.Cross(t, side);
                    if (i > 0) len += (pts[i] - pts[i - 1]).Len;
                    float f = (float)i / (pts.Count - 1);
                    float wind = w0 + (w1 - w0) * f;
                    float ao = Math.Min(1f, Shade(pts[i]) * 0.95f + 0.1f);
                    int first = M.Count;
                    for (int s = 0; s <= sides; s++)
                    {
                        double a = 2 * Math.PI * s / sides;
                        V3 n = side * (float)Math.Cos(a) + other * (float)Math.Sin(a);
                        M.Add(pts[i] + n * rad[i], n, (float)s / sides, len * vScale, bark, wind, 0, ao, phase);
                    }
                    if (prev >= 0)
                        for (int s = 0; s < sides; s++)
                        {
                            int a0 = prev + s, a1 = prev + s + 1, b0 = first + s, b1 = first + s + 1;
                            V3 mid = (pts[i] + pts[i - 1]) * 0.5f;
                            V3 out1 = (new V3(M.P[a0 * 3], M.P[a0 * 3 + 1], M.P[a0 * 3 + 2]) + new V3(M.P[b1 * 3], M.P[b1 * 3 + 1], M.P[b1 * 3 + 2])) * 0.5f - mid;
                            M.Tri(a0, b0, b1, out1);
                            M.Tri(a0, b1, a1, out1);
                        }
                    prev = first;
                }
                if (cap)
                {
                    V3 tip = pts[pts.Count - 1], t = (pts[pts.Count - 1] - pts[pts.Count - 2]).N;
                    int c = M.Add(tip + t * rad[rad.Count - 1] * 0.5f, t, 0.5f, len * vScale, bark, w1, 0, 0.8f, phase);
                    for (int s = 0; s < sides; s++) M.Tri(prev + s, prev + s + 1, c, t);
                }
            }

            /// <summary>
            /// A leaf card: a quad from the atlas cell, its foot at at (or its middle, if centred), running along up
            /// (the picture's v) by len, wid across along right. Lit as part of the crown rather than as a flat card.
            /// </summary>
            public void Card(V3 at, V3 up, V3 right, float len, float wid, int cell, float wind, float phase, bool centred = false, bool keepAlways = false)
            {
                bool keep = keepAlways || K.NextDouble() < CardKeep;
                if (!keep) return;
                len *= CardGrow; wid *= CardGrow;
                up = up.N; right = (right - up * V3.Dot(right, up)).N;
                V3 foot = centred ? at - up * (len * 0.5f) : at - up * (len * 0.04f);
                float u0, v0, u1, v1;
                TreeTextures.CellRect(cell, out u0, out v0, out u1, out v1);
                V3 face = V3.Cross(up, right).N;
                V3 mid = foot + up * (len * 0.5f);
                V3 crown = EnvNormal(mid);
                if (V3.Dot(face, crown) < 0) face = -face;
                V3 n = (crown * 0.75f + face * 0.25f).N;
                V3[] q = { foot - right * (wid * 0.5f), foot + right * (wid * 0.5f), foot + up * len + right * (wid * 0.5f), foot + up * len - right * (wid * 0.5f) };
                float[] uu = { u0, u1, u1, u0 }, vv = { v0, v0, v1, v1 };
                int b = M.Count;
                for (int k = 0; k < 4; k++)
                {
                    float w = wind * (k < 2 ? 0.85f : 1f);
                    M.Add(q[k], n, uu[k], vv[k], 10, w, 1, Shade(q[k]), phase + (k < 2 ? 0 : 0.05f));
                }
                M.Tri(b, b + 1, b + 2, face);
                M.Tri(b, b + 2, b + 3, face);
            }

            /// <summary>A path from start along dir, bending upwards by up (negative: droops), with random wobble.</summary>
            public List<V3> Path(V3 start, V3 dir, float len, int segs, float up, float wobble, List<V3> kicks)
            {
                var pts = new List<V3> { start };
                V3 p = start, d = dir.N;
                for (int i = 0; i < segs; i++)
                {
                    p = p + d * (len / segs);
                    pts.Add(p);
                    d = (d + V3.Up * (up / segs) + kicks[i] * (wobble / (float)Math.Sqrt(segs))).N;
                }
                return pts;
            }

            public List<V3> Kicks(int n)
            {
                var k = new List<V3>();
                for (int i = 0; i < n; i++) k.Add(RandUnit());
                return k;
            }
        }

        // ------------------------------------------------------------ broadleaf and branching trees

        sealed class Spec
        {
            public bool Leader;              // the trunk goes on to the top (birch, gum, pine) rather than splitting
            public float TrunkH = 0.3f, TrunkR = 0.03f, Lean = 0.03f, Flare = 1.4f, Buttress = 0;
            public int Limbs = 4; public float LimbStart = 0.85f, LimbAng = 45, LimbAngJ = 12, LimbLen = 0.45f;
            public int Levels = 3, Kids = 4; public float KidAng = 45, KidRatio = 0.55f, Up = 0.25f, Wobble = 0.35f, Gravity = 0;
            public V3 EnvC = new V3(0, 0.6f, 0), EnvR = new V3(0.4f, 0.38f, 0.4f);
            public int Cell = TreeTextures.Oak; public float CardSize = 0.13f; public int Cards = 4; public int Mode = 0;
            public float Aspect = 1f;            // card width / length
            public int Bark = TreeTextures.BarkOak;
            public int Stems = 1;                // scrub: several stems from the ground
        }

        const int ModeRandom = 0, ModeFlat = 1, ModeHang = 2, ModeTuft = 3;

        static void Branching(Gen g, Spec s)
        {
            g.EnvC = s.EnvC; g.EnvR = s.EnvR;
            float trunkTop = s.Leader ? 0.96f : s.TrunkH;
            for (int stem = 0; stem < s.Stems; stem++)
            {
                V3 lean = new V3(g.Rf(-1, 1), 0, g.Rf(-1, 1)).N * s.Lean;
                V3 sdir = (V3.Up + lean * (s.Stems > 1 ? 9f : 1f)).N;
                V3 sbase = s.Stems > 1 ? new V3(g.Rf(-0.03f, 0.03f), 0, g.Rf(-0.03f, 0.03f)) : new V3(0, -0.02f, 0);
                float sh = s.Stems > 1 ? trunkTop * g.Rf(0.8f, 1.1f) : trunkTop + 0.02f;
                int segs = s.Leader ? 9 : 5;
                var kicks = g.Kicks(segs);
                List<V3> trunk = g.Path(sbase, sdir, sh, segs, 0, s.Leader ? 0.07f : 0.12f, kicks);
                var rad = new List<float>();
                float r0 = s.TrunkR / (float)Math.Sqrt(s.Stems);
                for (int i = 0; i < trunk.Count; i++)
                {
                    float f = (float)i / (trunk.Count - 1);
                    float r = r0 * (s.Leader ? 1 - 0.85f * f : 1 - 0.3f * f);
                    // flared foot, and buttress roots on rainforest giants
                    r *= 1 + (s.Flare - 1) * (float)Math.Exp(-f * (s.Leader ? 18 : 10)) + s.Buttress * (float)Math.Exp(-f * 9);
                    rad.Add(r);
                }
                g.Tube(trunk, rad, g.TrunkSides, s.Bark, 0, s.Leader ? 0.25f : 0.08f, g.Rf(0, 1));
                if (s.Buttress > 0)
                    for (int k = 0; k < 5; k++)
                    {
                        // root fins round the foot
                        double a = k * 2 * Math.PI / 5 + g.Rf(-0.3f, 0.3f);
                        if (g.Lod > 0) continue;
                        var dir = new V3((float)Math.Cos(a), -0.35f, (float)Math.Sin(a));
                        var fin = new List<V3> { new V3(0, 0.12f, 0), new V3(0, 0.1f, 0) + dir * 0.035f, new V3(0, 0, 0) + new V3(dir.x, 0, dir.z) * 0.075f };
                        g.Tube(fin, new List<float> { r0 * 0.65f, r0 * 0.5f, r0 * 0.25f }, 4, s.Bark, 0, 0, 0);
                    }

                // limbs
                int limbs = Math.Max(1, s.Limbs / s.Stems + (s.Stems > 1 ? 1 : 0));
                float az0 = g.Rf(0, 6.283f);
                for (int l = 0; l < limbs; l++)
                {
                    float f = s.Leader ? s.LimbStart + (0.97f - s.LimbStart) * (l + g.Rf(0.1f, 0.9f)) / limbs : s.LimbStart + (1 - s.LimbStart) * g.Rf(0, 1);
                    int idx = Math.Min(trunk.Count - 2, (int)(f * (trunk.Count - 1)));
                    float frac = f * (trunk.Count - 1) - idx;
                    V3 at = V3.Lerp(trunk[idx], trunk[idx + 1], frac);
                    float rAt = rad[idx] + (rad[idx + 1] - rad[idx]) * frac;
                    float az = az0 + l * 2.39996f + g.Rf(-0.4f, 0.4f);
                    float ang = (s.LimbAng + g.Rf(-s.LimbAngJ, s.LimbAngJ)) * (float)(Math.PI / 180);
                    if (s.Leader) ang += (f - s.LimbStart) / (1 - s.LimbStart) * -0.35f;   // higher limbs more upright
                    var dir = new V3((float)(Math.Sin(ang) * Math.Cos(az)), (float)Math.Cos(ang), (float)(Math.Sin(ang) * Math.Sin(az)));
                    float len = s.LimbLen * g.Rf(0.8f, 1.15f) * (s.Leader ? 1 - 0.55f * (f - s.LimbStart) / (1 - s.LimbStart) : 1);
                    Grow(g, s, at, dir, len, rAt * (s.Leader ? 0.55f : 0.72f), 1);
                }
                if (!s.Leader && limbs > 0)
                {
                    // the trunk's own continuation into the crown
                    V3 top = trunk[trunk.Count - 1];
                    Grow(g, s, top, (sdir + new V3(g.Rf(-0.2f, 0.2f), 0, g.Rf(-0.2f, 0.2f))).N, s.LimbLen * 0.8f, rad[rad.Count - 1] * 0.75f, 1);
                }
                else if (s.Leader)
                {
                    // leaves round the very top of the leader
                    V3 top = trunk[trunk.Count - 1];
                    for (int k = 0; k < s.Cards; k++) Leaves(g, s, top, (V3.Up + g.RandUnit() * 0.6f).N, 0.5f, g.Rf(0, 1));
                }
            }
        }

        /// <summary>One branch and everything growing from it, fitted into the crown's oval.</summary>
        static void Grow(Gen g, Spec s, V3 start, V3 dir, float len, float r0, int level)
        {
            int segs = Math.Max(3, (int)(len / 0.05f) + 2);
            if (segs > 9) segs = 9;
            var kicks = g.Kicks(segs);
            float up = s.Up - s.Gravity * level;
            List<V3> pts = g.Path(start, dir, len, segs, up, s.Wobble, kicks);
            // shorten the branch until its tip is inside the crown
            for (int it = 0; it < 4; it++)
            {
                float e = g.Env(pts[pts.Count - 1]);
                if (e <= 1.02f) break;
                len /= Math.Min(1.6f, e);
                pts = g.Path(start, dir, len, segs, up, s.Wobble, kicks);
            }
            var rad = new List<float>();
            for (int i = 0; i < pts.Count; i++) rad.Add(r0 * (1 - 0.68f * i / (pts.Count - 1)));
            float phase = g.Rf(0, 1);
            int sides = level <= 1 ? g.LimbSides : g.TwigSides;
            g.Tube(pts, rad, sides, s.Bark, 0.05f + 0.25f * (level - 1), 0.3f + 0.25f * level, phase);

            if (level < s.Levels)
            {
                int kids = s.Kids + (g.R.NextDouble() < 0.5 ? 1 : 0);
                for (int k = 0; k < kids; k++)
                {
                    float t = 0.3f + 0.65f * (k + g.Rf(0.2f, 0.8f)) / kids;
                    int idx = Math.Min(pts.Count - 2, (int)(t * (pts.Count - 1)));
                    V3 at = V3.Lerp(pts[idx], pts[idx + 1], t * (pts.Count - 1) - idx);
                    V3 pd = (pts[idx + 1] - pts[idx]).N;
                    V3 side = V3.Cross(pd, g.RandUnit()).N;
                    float ang = (s.KidAng + g.Rf(-12, 12)) * (float)(Math.PI / 180);
                    V3 cd = (pd * (float)Math.Cos(ang) + side * (float)Math.Sin(ang)).N;
                    // children lean outwards from the trunk a little
                    V3 outw = new V3(at.x, 0, at.z);
                    if (outw.Len > 1e-3f) cd = (cd + outw.N * 0.25f).N;
                    Grow(g, s, at, cd, len * s.KidRatio * (1 - 0.35f * t), r0 * (1 - 0.6f * t) * 0.62f, level + 1);
                }
            }
            // leaves on the outer branches
            if (level >= s.Levels - 1)
            {
                int n = level == s.Levels ? s.Cards : Math.Max(1, s.Cards / 2);
                for (int k = 0; k < n; k++)
                {
                    float t = level == s.Levels ? 0.3f + 0.7f * (k + 0.5f) / n : 0.55f + 0.45f * (k + 0.5f) / n;
                    int idx = Math.Min(pts.Count - 2, (int)(t * (pts.Count - 1)));
                    V3 at = V3.Lerp(pts[idx], pts[idx + 1], t * (pts.Count - 1) - idx);
                    Leaves(g, s, at, (pts[idx + 1] - pts[idx]).N, 0.5f + 0.5f * t, phase + k * 0.13f);
                }
            }
        }

        static void Leaves(Gen g, Spec s, V3 at, V3 dir, float wind, float phase)
        {
            float size = s.CardSize * g.Rf(0.8f, 1.2f);
            V3 outw = (at - new V3(0, at.y, 0));
            outw = outw.Len > 1e-3f ? outw.N : g.RandUnit();
            switch (s.Mode)
            {
                case ModeFlat:
                {
                    // pads of leaves held flat to the sky (acacia, stone pine)
                    V3 up = (new V3(outw.x, 0, outw.z) + g.RandUnit() * 0.5f);
                    up = new V3(up.x, up.y * 0.25f + 0.08f, up.z).N;
                    V3 right = V3.Cross(up, V3.Up).N;
                    right = (right + V3.Up * g.Rf(-0.25f, 0.25f)).N;
                    g.Card(at, up, right, size, size * s.Aspect, s.Cell, wind, phase, true);
                    break;
                }
                case ModeHang:
                {
                    V3 up = (new V3(0, -1, 0) + g.RandUnit() * 0.45f + dir * 0.3f).N;
                    V3 right = V3.Cross(up, g.RandUnit()).N;
                    g.Card(at, up, right, size, size * s.Aspect, s.Cell, wind, phase);
                    break;
                }
                case ModeTuft:
                {
                    // a star of crossed cards round a twig tip, seen as a burst of needles from any side
                    V3 d = (dir + V3.Up * 0.5f).N;
                    V3 a = V3.Cross(d, g.RandUnit()).N;
                    V3 b = V3.Cross(d, a).N;
                    g.Card(at, a, b, size, size, s.Cell, wind, phase, true);
                    g.Card(at, b, d, size, size, s.Cell, wind, phase + 0.3f, true);
                    g.Card(at, d, a, size, size, s.Cell, wind, phase + 0.6f, true);
                    break;
                }
                default:
                {
                    V3 up = (dir * 0.45f + g.RandUnit() * 0.7f + outw * 0.45f + V3.Up * 0.2f).N;
                    V3 right = V3.Cross(up, g.RandUnit()).N;
                    g.Card(at, up, right, size, size * s.Aspect, s.Cell, wind, phase);
                    break;
                }
            }
        }

        // ------------------------------------------------------------ conifers

        static void Conifer(Gen g, bool fir, float width)
        {
            g.EnvC = new V3(0, 0.45f, 0); g.EnvR = new V3(width * 0.8f, 0.6f, width * 0.8f);
            var kicks = g.Kicks(10);
            List<V3> trunk = g.Path(new V3(0, -0.02f, 0), V3.Up, 1.0f, 10, 0, 0.03f, kicks);
            var rad = new List<float>();
            for (int i = 0; i < trunk.Count; i++)
            {
                float f = (float)i / (trunk.Count - 1);
                rad.Add(0.016f * (1 - 0.9f * f) * (1 + 0.5f * (float)Math.Exp(-f * 20)));
            }
            g.Tube(trunk, rad, g.TrunkSides, fir ? TreeTextures.BarkSpruce : TreeTextures.BarkSpruce, 0, 0.3f, g.Rf(0, 1));
            int cell = fir ? TreeTextures.Fir : TreeTextures.Spruce;
            float y = 0.1f + g.Rf(0, 0.03f);
            float az = g.Rf(0, 6.283f);
            while (y < 0.97f)
            {
                int n = 5 + g.R.Next(3);
                float h = Math.Min(1, y);
                float reach = width * (float)Math.Pow(1 - h, fir ? 0.9 : 0.95) + 0.02f;
                for (int b = 0; b < n; b++)
                {
                    float a = az + b * 6.283f / n + g.Rf(-0.3f, 0.3f);
                    float len = reach * g.Rf(0.8f, 1.12f);
                    float droop = (fir ? g.Rf(4, 14) : g.Rf(12, 22) + 20 * (1 - h)) * (float)(Math.PI / 180);
                    var dir = new V3((float)(Math.Cos(a) * Math.Cos(droop)), -(float)Math.Sin(droop), (float)(Math.Sin(a) * Math.Cos(droop)));
                    var bk = g.Kicks(4);
                    // spruce branches sag and turn their tips up; fir branches stay flatter
                    List<V3> pts = g.Path(trunk[0] + new V3(0, y, 0), dir, len, 4, fir ? 0.05f : 0.32f, 0.12f, bk);
                    float phase = g.Rf(0, 1);
                    var br = new List<float>();
                    float r0 = 0.0015f + 0.012f * len;
                    for (int i = 0; i < pts.Count; i++) br.Add(r0 * (1 - 0.75f * i / (pts.Count - 1)));
                    g.Tube(pts, br, g.TwigSides, TreeTextures.BarkSpruce, 0.1f, 0.7f, phase);
                    // needles: overlapping sprays along the branch, flat and a second set tilted for depth
                    float cl = Math.Max(0.045f, Math.Min(0.1f, len * 0.55f));
                    float step = cl * 0.5f;
                    int k = 0;
                    for (float s = len * 0.12f; s < len; s += step, k++)
                    {
                        float t = s / len;
                        int idx = Math.Min(pts.Count - 2, (int)(t * (pts.Count - 1)));
                        V3 at = V3.Lerp(pts[idx], pts[idx + 1], t * (pts.Count - 1) - idx);
                        V3 d = (pts[idx + 1] - pts[idx]).N;
                        V3 flat = V3.Cross(d, V3.Up).N;
                        float roll = (k % 2 == 0 ? 1 : -1) * g.Rf(0.1f, 0.45f);
                        V3 right = (flat * (float)Math.Cos(roll) + V3.Up * (float)Math.Sin(roll)).N;
                        float wind = 0.3f + 0.7f * t;
                        g.Card(at, d, right, cl, cl * 0.85f, cell, wind, phase + t * 0.2f);
                        if (g.Lod == 0)
                        {
                            V3 right2 = (flat * 0.45f + V3.Up * (k % 2 == 0 ? -0.9f : 0.9f)).N;
                            g.Card(at + V3.Up * 0.004f, (d + V3.Up * 0.25f).N, right2, cl * 0.85f, cl * 0.7f, cell, wind, phase + t * 0.2f + 0.1f);
                        }
                    }
                }
                y += (fir ? 0.03f : 0.034f) * g.Rf(0.8f, 1.2f) * (g.Lod >= 1 ? 1f : 1f);
                az += g.Rf(0.4f, 1.2f);
            }
            // the leader at the top
            V3 top = trunk[trunk.Count - 1];
            for (int k = 0; k < 3; k++)
            {
                V3 right = new V3((float)Math.Cos(k * 2.1), 0, (float)Math.Sin(k * 2.1));
                g.Card(top - V3.Up * 0.08f, V3.Up, right, 0.11f, 0.07f, cell, 1, g.Rf(0, 1), false, true);
            }
        }

        static void Cypress(Gen g, float width)
        {
            g.EnvC = new V3(0, 0.5f, 0); g.EnvR = new V3(width, 0.52f, width);
            var trunk = g.Path(new V3(0, -0.02f, 0), V3.Up, 0.9f, 6, 0, 0.02f, g.Kicks(6));
            var rad = new List<float>();
            for (int i = 0; i < trunk.Count; i++) rad.Add(0.012f * (1 - 0.8f * i / (trunk.Count - 1)));
            g.Tube(trunk, rad, g.TrunkSides, TreeTextures.BarkScrub, 0, 0.2f, 0);
            int n = 340;
            for (int i = 0; i < n; i++)
            {
                float y = 0.05f + 0.93f * (float)Math.Pow(g.R.NextDouble(), 0.85);
                float f = (y - 0.02f) / 0.98f;
                float r = width * (float)Math.Pow(Math.Sin(Math.PI * Math.Pow(Math.Max(0.02, Math.Min(1, f)), 0.72)), 0.8);
                float a = g.Rf(0, 6.283f), d = r * (0.5f + 0.5f * (float)Math.Sqrt(g.R.NextDouble()));
                var outw = new V3((float)Math.Cos(a), 0, (float)Math.Sin(a));
                V3 at = new V3(outw.x * d, y, outw.z * d);
                V3 up = (V3.Up * 0.85f + outw * 0.45f + g.RandUnit() * 0.25f).N;
                V3 right = V3.Cross(up, outw).N;
                float size = g.Rf(0.08f, 0.12f);
                g.Card(at, up, right, size, size * 0.8f, TreeTextures.Cypress, 0.4f + 0.6f * y, g.Rf(0, 1), true);
            }
        }

        // ------------------------------------------------------------ palms

        static void PalmTree(Gen g, bool date)
        {
            g.EnvC = new V3(0, 0.85f, 0); g.EnvR = new V3(0.38f, 0.22f, 0.38f);
            float lean = date ? g.Rf(0, 0.08f) : g.Rf(0.1f, 0.35f);
            float az = g.Rf(0, 6.283f);
            var leanDir = new V3((float)Math.Cos(az), 0, (float)Math.Sin(az));
            var pts = new List<V3>();
            var rad = new List<float>();
            int segs = 14;
            float h = 0.82f;
            V3 p = new V3(0, -0.02f, 0), d = (V3.Up + leanDir * lean).N;
            for (int i = 0; i <= segs; i++)
            {
                float f = (float)i / segs;
                pts.Add(p);
                float r = (date ? 0.03f : 0.022f) * (1 - 0.25f * f) * (1 + 0.6f * (float)Math.Exp(-f * 14));
                rad.Add(r);
                p = p + d * (h / segs);
                // coconut palms curve back up towards the sky
                d = (d + V3.Up * (lean * 0.12f) + g.RandUnit() * 0.015f).N;
            }
            g.Tube(pts, rad, g.TrunkSides, TreeTextures.BarkPalm, 0, 0.35f, 0);
            V3 crown = pts[pts.Count - 1];
            g.EnvC = crown + V3.Up * 0.02f;
            // a knob of old leaf bases at the top
            var knob = new List<V3> { crown - V3.Up * 0.05f, crown, crown + V3.Up * 0.04f };
            g.Tube(knob, new List<float> { rad[rad.Count - 1] * 1.1f, rad[rad.Count - 1] * 1.45f, rad[rad.Count - 1] * 0.6f }, g.TrunkSides, TreeTextures.BarkPalm, 0.35f, 0.4f, 0, true);

            int fronds = date ? 24 : 15;
            int fsegs = g.Lod == 0 ? 7 : 4;
            for (int k = 0; k < fronds; k++)
            {
                float a = k * 2.39996f + g.Rf(-0.2f, 0.2f);
                float age = (float)k / fronds;               // young fronds stand up, old ones hang
                float elev = (date ? 65 : 55) - age * (date ? 95 : 85) + g.Rf(-8, 8);
                bool dead = date ? age > 0.85f : age > 0.9f && g.R.NextDouble() < 0.6;
                float len = (date ? 0.3f : 0.4f) * g.Rf(0.85f, 1.1f) * (1 - 0.25f * Math.Abs(age - 0.4f));
                float er = elev * (float)(Math.PI / 180);
                var fd = new V3((float)(Math.Cos(a) * Math.Cos(er)), (float)Math.Sin(er), (float)(Math.Sin(a) * Math.Cos(er)));
                float stiff = date ? 1.0f : 1.6f;
                var cps = new List<V3>();
                V3 q = crown + fd * 0.02f, qd = fd;
                for (int i = 0; i <= fsegs; i++)
                {
                    cps.Add(q);
                    q = q + qd * (len / fsegs);
                    qd = (qd - V3.Up * (stiff * 0.9f / fsegs) * (0.4f + (float)i / fsegs)).N;
                }
                if (dead)
                {
                    // dead fronds hang down along the trunk
                    cps.Clear();
                    var dd = (fd * 0.35f - V3.Up).N;
                    for (int i = 0; i <= fsegs; i++) cps.Add(crown - V3.Up * 0.03f + dd * (len * 0.8f * i / fsegs));
                }
                float width = (date ? 0.15f : 0.24f) * g.Rf(0.9f, 1.1f);
                float u0, v0, u1, v1;
                TreeTextures.CellRect(dead ? TreeTextures.DeadFrond : TreeTextures.Frond, out u0, out v0, out u1, out v1);
                float phase = g.Rf(0, 1);
                int prev = -1;
                for (int i = 0; i <= fsegs; i++)
                {
                    float t = (float)i / fsegs;
                    V3 c = cps[i];
                    V3 tan = i < fsegs ? (cps[i + 1] - cps[i]).N : (cps[i] - cps[i - 1]).N;
                    V3 side = V3.Cross(tan, V3.Up);
                    side = side.Len > 1e-3f ? side.N : V3.Cross(tan, new V3(1, 0, 0)).N;
                    V3 fold = V3.Cross(side, tan).N;               // the frond's own up
                    if (fold.y < 0) fold = -fold;
                    float w = width * (float)Math.Pow(Math.Sin(Math.PI * (0.1 + 0.9 * t)), 0.6) * 0.5f;
                    // leaflets droop either side of the midrib (a shallow V turned over)
                    V3 l = c - side * w - fold * (w * 0.45f), r = c + side * w - fold * (w * 0.45f);
                    float v = v0 + (v1 - v0) * t;
                    float wind = 0.35f + 0.65f * t;
                    float sh = g.Shade(c);
                    V3 n = (g.EnvNormal(c) * 0.6f + fold * 0.4f).N;
                    int il = g.M.Add(l, n, u0, v, 10, wind, 1, sh, phase + t * 0.15f);
                    int im = g.M.Add(c, n, (u0 + u1) * 0.5f, v, 10, wind, 1, sh, phase + t * 0.15f);
                    int ir = g.M.Add(r, n, u1, v, 10, wind, 1, sh, phase + t * 0.15f);
                    if (prev >= 0)
                    {
                        g.M.Tri(prev, prev + 1, im, fold); g.M.Tri(prev, im, il, fold);
                        g.M.Tri(prev + 1, prev + 2, ir, fold); g.M.Tri(prev + 1, ir, im, fold);
                    }
                    prev = il;
                }
            }
            if (!date && g.Lod == 0)
            {
                // a bunch of coconuts under the fronds
                for (int k = 0; k < 5; k++)
                {
                    float a = g.Rf(0, 6.283f);
                    V3 c = crown + new V3((float)Math.Cos(a) * 0.03f, -0.03f - g.Rf(0, 0.02f), (float)Math.Sin(a) * 0.03f);
                    var nut = new List<V3> { c - V3.Up * 0.012f, c, c + V3.Up * 0.012f };
                    g.Tube(nut, new List<float> { 0.006f, 0.014f, 0.006f }, 6, TreeTextures.BarkSmooth, 0.4f, 0.4f, 0, true);
                }
            }
        }

        // ------------------------------------------------------------ the kinds

        static Spec SpecFor(byte species, Gen g, int variant)
        {
            var s = new Spec();
            float v = variant / 2f;   // 0, 0.5, 1: each variant a little different in shape
            switch (species)
            {
                case TreeSpecies.Oak:
                    s.TrunkH = 0.3f + 0.08f * v; s.TrunkR = 0.045f; s.Limbs = 4 + variant % 2; s.LimbAng = 50; s.LimbAngJ = 15; s.LimbLen = 0.5f;
                    s.Levels = 3; s.Kids = 3; s.KidAng = 50; s.KidRatio = 0.55f; s.Up = 0.15f; s.Wobble = 0.55f;
                    s.EnvC = new V3(0, 0.6f, 0); s.EnvR = new V3(0.46f - 0.06f * v, 0.4f, 0.46f - 0.06f * v);
                    s.Cell = TreeTextures.Oak; s.CardSize = 0.17f; s.Cards = 6; s.Bark = TreeTextures.BarkOak; break;
                case TreeSpecies.Maple:
                    s.TrunkH = 0.3f; s.TrunkR = 0.035f; s.Limbs = 3 + variant % 2; s.LimbAng = 34; s.LimbLen = 0.5f;
                    s.Levels = 3; s.Kids = 3; s.KidAng = 42; s.KidRatio = 0.58f; s.Up = 0.3f; s.Wobble = 0.35f;
                    s.EnvC = new V3(0, 0.62f, 0); s.EnvR = new V3(0.34f + 0.04f * v, 0.4f, 0.34f + 0.04f * v);
                    s.Cell = TreeTextures.Maple; s.CardSize = 0.16f; s.Cards = 6; s.Bark = TreeTextures.BarkSmooth; break;
                case TreeSpecies.Birch:
                    s.Leader = true; s.TrunkR = 0.022f; s.Lean = 0.05f; s.Limbs = 11; s.LimbStart = 0.3f; s.LimbAng = 55; s.LimbLen = 0.3f;
                    s.Levels = 2; s.Kids = 4; s.KidAng = 40; s.KidRatio = 0.6f; s.Up = 0.15f; s.Gravity = 0.35f; s.Wobble = 0.3f;
                    s.EnvC = new V3(0, 0.62f, 0); s.EnvR = new V3(0.22f + 0.04f * v, 0.42f, 0.22f + 0.04f * v);
                    s.Cell = TreeTextures.Birch; s.CardSize = 0.12f; s.Cards = 5; s.Mode = ModeHang; s.Bark = TreeTextures.BarkBirch; break;
                case TreeSpecies.Eucalyptus:
                    s.Leader = true; s.TrunkR = 0.028f; s.Lean = 0.06f; s.Limbs = 7; s.LimbStart = 0.42f; s.LimbAng = 38; s.LimbAngJ = 15; s.LimbLen = 0.36f;
                    s.Levels = 2; s.Kids = 3; s.KidAng = 40; s.KidRatio = 0.6f; s.Up = 0.25f; s.Wobble = 0.55f;
                    s.EnvC = new V3(0, 0.7f, 0); s.EnvR = new V3(0.3f + 0.05f * v, 0.32f, 0.3f + 0.05f * v);
                    s.Cell = TreeTextures.Eucalyptus; s.CardSize = 0.15f; s.Cards = 5; s.Mode = ModeHang; s.Aspect = 0.8f; s.Bark = TreeTextures.BarkEuca; break;
                case TreeSpecies.Jungle:
                    s.Leader = false; s.TrunkH = 0.6f + 0.06f * v; s.TrunkR = 0.03f; s.Flare = 1.3f; s.Buttress = 1.4f; s.Lean = 0.02f;
                    s.Limbs = 5; s.LimbStart = 0.9f; s.LimbAng = 62; s.LimbAngJ = 10; s.LimbLen = 0.42f;
                    s.Levels = 2; s.Kids = 4; s.KidAng = 45; s.KidRatio = 0.6f; s.Up = 0.25f; s.Wobble = 0.4f;
                    s.EnvC = new V3(0, 0.84f, 0); s.EnvR = new V3(0.46f, 0.16f, 0.46f);
                    s.Cell = TreeTextures.Jungle; s.CardSize = 0.17f; s.Cards = 5; s.Bark = TreeTextures.BarkSmooth; break;
                case TreeSpecies.Acacia:
                    s.TrunkH = 0.26f + 0.1f * v; s.TrunkR = 0.032f; s.Lean = 0.08f; s.Limbs = 4; s.LimbStart = 0.9f; s.LimbAng = 42; s.LimbLen = 0.6f;
                    s.Levels = 2; s.Kids = 4; s.KidAng = 55; s.KidRatio = 0.6f; s.Up = 0.1f; s.Wobble = 0.4f;
                    s.EnvC = new V3(0, 0.8f, 0); s.EnvR = new V3(0.72f, 0.16f, 0.72f);
                    s.Cell = TreeTextures.Acacia; s.CardSize = 0.24f; s.Cards = 5; s.Mode = ModeFlat; s.Bark = TreeTextures.BarkScrub; break;
                case TreeSpecies.StonePine:
                    s.TrunkH = 0.45f + 0.08f * v; s.TrunkR = 0.038f; s.Lean = 0.06f; s.Limbs = 4; s.LimbStart = 0.92f; s.LimbAng = 38; s.LimbLen = 0.55f;
                    s.Levels = 2; s.Kids = 4; s.KidAng = 50; s.KidRatio = 0.6f; s.Up = 0.15f; s.Wobble = 0.3f;
                    s.EnvC = new V3(0, 0.8f, 0); s.EnvR = new V3(0.56f, 0.2f, 0.56f);
                    s.Cell = TreeTextures.PineTuft; s.CardSize = 0.17f; s.Cards = 3; s.Mode = ModeTuft; s.Bark = TreeTextures.BarkPine; break;
                case TreeSpecies.Pine:
                    s.Leader = true; s.TrunkR = 0.026f; s.Lean = 0.05f; s.Limbs = 10; s.LimbStart = 0.55f + 0.08f * v; s.LimbAng = 62; s.LimbAngJ = 15; s.LimbLen = 0.3f;
                    s.Levels = 2; s.Kids = 3; s.KidAng = 40; s.KidRatio = 0.55f; s.Up = 0.35f; s.Wobble = 0.45f;
                    s.EnvC = new V3(0, 0.78f, 0); s.EnvR = new V3(0.26f, 0.26f, 0.26f);
                    s.Cell = TreeTextures.PineTuft; s.CardSize = 0.14f; s.Cards = 2; s.Mode = ModeTuft; s.Bark = TreeTextures.BarkPine; break;
                case TreeSpecies.Scrub:
                    s.Stems = 4 + variant; s.TrunkH = 0.5f; s.TrunkR = 0.03f; s.Lean = 0.06f; s.Flare = 1.1f; s.Limbs = 6; s.LimbStart = 0.4f; s.LimbAng = 40; s.LimbLen = 0.35f;
                    s.Levels = 2; s.Kids = 3; s.KidAng = 45; s.KidRatio = 0.6f; s.Up = 0.1f; s.Wobble = 0.7f;
                    s.EnvC = new V3(0, 0.52f, 0); s.EnvR = new V3(0.55f, 0.48f, 0.55f);
                    s.Cell = TreeTextures.Scrub; s.CardSize = 0.17f; s.Cards = 4; s.Bark = TreeTextures.BarkScrub; break;
            }
            return s;
        }

        /// <summary>Build one model. species from TreeSpecies, variant 0..2, lod 0 (full) or 1 (lighter).</summary>
        public static TreeMesh Build(byte species, int variant, int lod)
        {
            var g = new Gen(7919 * (species + 1) + 104729 * (variant + 1), lod);
            float v = variant / 2f;
            switch (species)
            {
                case TreeSpecies.Spruce: Conifer(g, false, 0.26f + 0.04f * v); break;
                case TreeSpecies.Fir: Conifer(g, true, 0.22f + 0.04f * v); break;
                case TreeSpecies.Cypress: Cypress(g, 0.085f + 0.025f * v); break;
                case TreeSpecies.Palm: PalmTree(g, false); break;
                case TreeSpecies.DatePalm: PalmTree(g, true); break;
                default: Branching(g, SpecFor(species, g, variant)); break;
            }
            g.M.Measure();
            return g.M;
        }

        /// <summary>The distant look: two crossed upright pictures of the tree (the front and the side).</summary>
        public static TreeMesh Impostor(TreeMesh full, float u0f, float v0f, float u1f, float v1f, float u0s, float v0s, float u1s, float v1s)
        {
            var m = new TreeMesh();
            float top = full.Top * 1.02f, hx = full.HalfX * 1.04f, hz = full.HalfZ * 1.04f, bottom = -0.03f;
            Quad(m, new V3(-hx, bottom, 0), new V3(hx, bottom, 0), new V3(hx, top, 0), new V3(-hx, top, 0), new V3(0, 0, -1), u0f, v0f, u1f, v1f);
            Quad(m, new V3(0, bottom, -hz), new V3(0, bottom, hz), new V3(0, top, hz), new V3(0, top, -hz), new V3(1, 0, 0), u0s, v0s, u1s, v1s);
            m.Measure();
            return m;
        }

        static void Quad(TreeMesh m, V3 a, V3 b, V3 c, V3 d, V3 n, float u0, float v0, float u1, float v1)
        {
            int i = m.Count;
            m.Add(a, n, u0, v0, 20, 0, 1, 1, 0); m.Add(b, n, u1, v0, 20, 0, 1, 1, 0);
            m.Add(c, n, u1, v1, 20, 0.3f, 1, 1, 0); m.Add(d, n, u0, v1, 20, 0.3f, 1, 1, 0);
            m.Tri(i, i + 1, i + 2, n); m.Tri(i, i + 2, i + 3, n);
        }
    }
}
