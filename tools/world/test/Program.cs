using System;
using System.Diagnostics;
using System.IO;
using MarsNeedsWater.Earth;

static class Program
{
    static void Main(string[] a)
    {
        if (a[0] == "ground")
        {
            // ground lat lon zoom size out.raw : the finished ground picture (RGBA, alpha = GroundKind) of one square
            var srcG = new TileSources("/tmp/earthcache");
            int gz = int.Parse(a[3]);
            double gla = double.Parse(a[1]), glo = double.Parse(a[2]);
            var swG = Stopwatch.StartNew();
            var tg = TileBuilder.Build(srcG, gz, (int)Geo.LonToX(glo, gz), (int)Geo.LatToY(gla, gz), int.Parse(a[4]));
            Console.WriteLine("built in " + swG.ElapsedMilliseconds + " ms, size " + tg.Size + " m");
            File.WriteAllBytes(a[5], tg.Texture);
            var counts = new int[GroundKind.Count];
            for (int i = 3; i < tg.Texture.Length; i += 4) counts[Math.Min(tg.Texture[i], (byte)(GroundKind.Count - 1))]++;
            for (int k = 0; k < GroundKind.Count; k++) Console.WriteLine(GroundKind.Names[k] + " " + (100.0 * counts[k] / (tg.Texture.Length / 4)).ToString("F1") + "%");
            return;
        }
        if (a[0] == "cliff")
        {
            // cliff lat lon [r] : heights of the zoom-14 squares round a spot: steepest step inside each, and how
            // well shared edges agree with the neighbours and with the coarser level
            var srcC = new TileSources("/tmp/earthcache");
            double cla = double.Parse(a[1]), clo = double.Parse(a[2]);
            int R = a.Length > 3 ? int.Parse(a[3]) : 1;
            int cx = (int)Geo.LonToX(clo, 14), cy = (int)Geo.LatToY(cla, 14);
            const int Gc = TileResult.Grid, Vc = Gc + 1;
            Func<TileResult, float[]> alt = tq =>
            {
                var fr = new Frame(tq.Lat, tq.Lon); var o = new float[Vc * Vc];
                for (int k = 0; k < Vc * Vc; k++) { double la, lo, al; fr.ToGeo(tq.Pos[k * 3], tq.Pos[k * 3 + 1], tq.Pos[k * 3 + 2], out la, out lo, out al); o[k] = (float)al; }
                return o;
            };
            var hsC = new System.Collections.Generic.Dictionary<long, float[]>();
            float cell = 0;
            for (int dy = -R; dy <= R; dy++) for (int dx = -R; dx <= R; dx++)
            {
                var tc = TileBuilder.Build(srcC, 14, cx + dx, cy + dy, 1024);
                cell = tc.Size / Gc;
                hsC[((long)(cx + dx) << 32) | (uint)(cy + dy)] = alt(tc);
            }
            Console.WriteLine(FormattableString.Invariant($"centre 14/{cx}/{cy}, cell {cell:F2} m, spot at cell {(Geo.LonToX(clo, 14) - cx) * Gc:F1},{(Geo.LatToY(cla, 14) - cy) * Gc:F1}"));
            foreach (var kv in hsC)
            {
                int tx = (int)(kv.Key >> 32), ty = (int)(uint)kv.Key; float[] h = kv.Value;
                float worst = 0; int wi = 0, wj = 0;
                for (int j = 0; j <= Gc; j++) for (int i = 0; i <= Gc; i++)
                {
                    if (i < Gc) { float d = Math.Abs(h[j * Vc + i + 1] - h[j * Vc + i]); if (d > worst) { worst = d; wi = i; wj = j; } }
                    if (j < Gc) { float d = Math.Abs(h[(j + 1) * Vc + i] - h[j * Vc + i]); if (d > worst) { worst = d; wi = i; wj = j; } }
                }
                float eE = 0, eS = 0; float[] n;
                if (hsC.TryGetValue(((long)(tx + 1) << 32) | (uint)ty, out n)) for (int j = 0; j <= Gc; j++) eE = Math.Max(eE, Math.Abs(h[j * Vc + Gc] - n[j * Vc]));
                if (hsC.TryGetValue(((long)tx << 32) | (uint)(ty + 1), out n)) for (int i = 0; i <= Gc; i++) eS = Math.Max(eS, Math.Abs(h[Gc * Vc + i] - n[i]));
                if (a.Length > 4 && hsC.TryGetValue(((long)(tx + 1) << 32) | (uint)ty, out n)) for (int j = 0; j <= Gc; j++) if (Math.Abs(h[j * Vc + Gc] - n[j * Vc]) > 0.3f) Console.WriteLine(FormattableString.Invariant($"   E {tx}/{ty} j {j}: {h[j * Vc + Gc - 1]:F2} {h[j * Vc + Gc]:F2} | {n[j * Vc]:F2} {n[j * Vc + 1]:F2}"));
                if (a.Length > 4 && hsC.TryGetValue(((long)tx << 32) | (uint)(ty + 1), out n)) for (int i = 0; i <= Gc; i++) if (Math.Abs(h[Gc * Vc + i] - n[i]) > 0.3f) Console.WriteLine(FormattableString.Invariant($"   S {tx}/{ty} i {i}: {h[(Gc - 1) * Vc + i]:F2} {h[Gc * Vc + i]:F2} | {n[i]:F2} {n[Vc + i]:F2}"));
                Console.WriteLine(FormattableString.Invariant($"14/{tx}/{ty}: steepest step {worst:F2} m at cell {wi},{wj} (slope {worst / cell:F2}); edge mismatch east {eE:F3} south {eS:F3}"));
            }
            // against the coarser level, along the outer edge of the zoom-14 block is where it shows; print the worst anywhere
            var t12 = TileBuilder.Build(srcC, 12, cx >> 2, cy >> 2, 512);
            float[] h12 = alt(t12);
            float w12 = 0; string where = "";
            foreach (var kv in hsC)
            {
                int tx = (int)(kv.Key >> 32), ty = (int)(uint)kv.Key;
                if (tx >> 2 != cx >> 2 || ty >> 2 != cy >> 2) continue;
                for (int j = 0; j <= Gc; j++) for (int i = 0; i <= Gc; i++)
                {
                    double u = ((tx & 3) + (double)i / Gc) / 4 * Gc, v = ((ty & 3) + (double)j / Gc) / 4 * Gc;
                    int ui = Math.Min(Gc - 1, (int)u), vj = Math.Min(Gc - 1, (int)v); double fu = u - ui, fv = v - vj;
                    double c = (h12[vj * Vc + ui] * (1 - fu) + h12[vj * Vc + ui + 1] * fu) * (1 - fv) + (h12[(vj + 1) * Vc + ui] * (1 - fu) + h12[(vj + 1) * Vc + ui + 1] * fu) * fv;
                    float d = (float)(kv.Value[j * Vc + i] - c);
                    if (Math.Abs(d) > Math.Abs(w12)) { w12 = d; where = $"14/{tx}/{ty} cell {i},{j}"; }
                }
            }
            Console.WriteLine(FormattableString.Invariant($"fine minus coarse (zoom 12), worst {w12:F2} m at {where}"));
            return;
        }
        if (a[0] == "roads")
        {
            // roads lat lon out : build one zoom-14 square and save its ground picture and road mesh for roadview.py
            var srcR = new TileSources("/tmp/earthcache");
            double rla = double.Parse(a[1]), rlo = double.Parse(a[2]);
            int rx = (int)Geo.LonToX(rlo, 14), ry = (int)Geo.LatToY(rla, 14);
            var swR = Stopwatch.StartNew();
            RoadPaint.Counts = new int[16];
            var tr = TileBuilder.Build(srcR, 14, rx, ry, 1024);
            Console.WriteLine("vertices per layer: " + string.Join(" ", RoadPaint.Counts));
            var m = tr.Roads;
            Console.WriteLine($"tile 14/{rx}/{ry} size {tr.Size:F1} m built in {swR.ElapsedMilliseconds} ms; roads {tr.RoadCount}, verts {m.Count}, tris {m.I.Count / 3}; bridge runs {tr.BridgeRuns.Count}");
            Console.WriteLine(FormattableString.Invariant($"at {(Geo.LonToX(rlo, 14) - rx) * tr.Size:F1} {(Geo.LatToY(rla, 14) - ry) * tr.Size:F1}"));
            File.WriteAllBytes(a[3] + ".tex", tr.Texture);
            using (var f = new BinaryWriter(File.Create(a[3] + ".road")))
            {
                f.Write(tr.Size); f.Write(m.Count);
                foreach (float v in m.P) f.Write(v);
                foreach (float v in m.U) f.Write(v);
                foreach (float v in m.X) f.Write(v);
                foreach (float v in m.Y) f.Write(v);
                f.Write(m.I.Count);
                foreach (int v in m.I) f.Write(v);
            }
            return;
        }
        if (a[0] == "flags")
        {
            var src0 = new TileSources("/tmp/earthcache");
            double la = double.Parse(a[1]), lo = double.Parse(a[2]);
            var tl = Mvt.Decode(src0.Transport.GetTile(14, (int)Geo.LonToX(lo, 14), (int)Geo.LatToY(la, 14)), "segment");
            var L = tl["segment"];
            var seen = new System.Collections.Generic.Dictionary<string, int>();
            foreach (string k in new[] { "road_flags", "rail_flags", "level_rules", "level", "id", "subclass", "road_surface", "width_rules", "names" })
                Console.WriteLine(k + " key " + L.Key(k));
            foreach (var f in L.Features)
            {
                string v = (L.Str(f, L.Key("road_flags")) ?? "-") + " | " + (L.Str(f, L.Key("rail_flags")) ?? "-") + " | lvl " + (L.Get(f, L.Key("level_rules")) ?? "-");
                int c; seen.TryGetValue(v, out c); seen[v] = c + 1;
            }
            foreach (var kv in seen) Console.WriteLine(kv.Value + "  " + kv.Key);
            int shown = 0;
            foreach (var f in L.Features)
            {
                string v = L.Str(f, L.Key("road_flags"));
                if (v == null || !v.Contains("is_bridge") || shown++ > 6) continue;
                Console.WriteLine("BRIDGE " + L.Str(f, L.Key("class")) + " id " + L.Get(f, L.Key("id")) + " parts " + f.Parts.Count + " pts " + f.Parts[0].Length / 2 + " first " + f.Parts[0][0] + "," + f.Parts[0][1] + " last " + f.Parts[0][f.Parts[0].Length - 2] + "," + f.Parts[0][f.Parts[0].Length - 1] + " ext " + L.Extent);
            }
            return;
        }
        if (a[0] == "bridge")
        {
            // bridge x0 y0 w h : build a block of zoom-14 tiles, report the decks and how well they agree at the joins
            var srcB = new TileSources("/tmp/earthcache");
            int bx0, by0;
            if (a[1].Contains(".")) { bx0 = (int)Geo.LonToX(double.Parse(a[2]), 14); by0 = (int)Geo.LatToY(double.Parse(a[1]), 14); }
            else { bx0 = int.Parse(a[1]); by0 = int.Parse(a[2]); }
            int bw = int.Parse(a[3]), bh = int.Parse(a[4]);
            var ends = new System.Collections.Generic.List<double[]>();
            using (var w = new StreamWriter(a[5]))
                for (int ty = by0; ty < by0 + bh; ty++)
                    for (int tx = bx0; tx < bx0 + bw; tx++)
                    {
                        var tt = TileBuilder.Build(srcB, 14, tx, ty, 256);
                        float maxClear = 0; int pts = 0;
                        foreach (var r in tt.BridgeRuns)
                        {
                            for (int i = 0; i < r.P.Length; i += 3)
                            {
                                double gx = tx + r.P[i] / tt.Size + 0.5, gy = ty + 0.5 - r.P[i + 2] / tt.Size;
                                float g = tt.HeightAt(r.P[i], r.P[i + 2]);
                                maxClear = Math.Max(maxClear, r.P[i + 1] - g); pts++;
                                w.WriteLine(FormattableString.Invariant($"{gx:F6} {gy:F6} {r.P[i + 1]:F2} {g:F2} {r.HalfWidth:F2} {(i == 0 ? 1 : 0)}"));
                                if (i == 0 || i == r.P.Length - 3) ends.Add(new[] { gx, gy, r.P[i + 1], g, tx * 100000.0 + ty });
                            }
                        }
                        Console.WriteLine($"tile {tx},{ty}: runs {tt.BridgeRuns.Count} points {pts} highest above ground {maxClear:F1} m, deck tris {tt.Bridges.I.Count / 3}");
                    }
            // ends that meet an end from another run: do the heights agree?
            double worst = 0; int joins = 0, bad = 0, hanging = 0, inner = 0;
            for (int i = 0; i < ends.Count; i++)
            {
                bool onBorder = Math.Abs(ends[i][0] - Math.Round(ends[i][0])) < 2e-4 || Math.Abs(ends[i][1] - Math.Round(ends[i][1])) < 2e-4;
                bool met = false;
                for (int j = 0; j < ends.Count; j++)
                {
                    if (i == j) continue;
                    double dx = (ends[i][0] - ends[j][0]) * 386, dy = (ends[i][1] - ends[j][1]) * 386;
                    if (dx * dx + dy * dy > 0.25 * 0.25) continue;
                    met = true;
                    if (!onBorder || ends[i][4] == ends[j][4]) continue;
                    joins++;
                    double d = Math.Abs(ends[i][2] - ends[j][2]);
                    if (d > worst) worst = d;
                    if (d > 0.15) { bad++; if (bad < 12) Console.WriteLine($"  step {d:F2} m at {ends[i][0]:F4},{ends[i][1]:F4}: {ends[i][2]:F2} vs {ends[j][2]:F2}"); }
                }
                if (!met && !onBorder && ends[i][2] - ends[i][3] > 0.6) { hanging++; if (hanging < 8) Console.WriteLine($"  loose end {ends[i][2] - ends[i][3]:F2} m above ground at {ends[i][0]:F4},{ends[i][1]:F4}"); }
                if (!met && !onBorder) inner++;
            }
            Console.WriteLine($"joins across tile borders {joins / 2}, worst step {worst:F2} m, steps over 0.15 m: {bad / 2}; bridge ends inside tiles {inner}, of which left in the air: {hanging}");
            return;
        }
        if (a[0] == "bdump")
        {
            // bdump lat lon radius : every map building within radius real metres, with all it says about itself
            var srcD = new TileSources("/tmp/earthcache");
            double dla = double.Parse(a[1]), dlo = double.Parse(a[2]), rad = double.Parse(a[3]);
            int dx = (int)Geo.LonToX(dlo, 14), dy = (int)Geo.LatToY(dla, 14);
            var ls = Mvt.Decode(srcD.Buildings.GetTile(14, dx, dy), "building", "building_part");
            foreach (var kv in ls)
            {
                var L = kv.Value;
                foreach (var f in L.Features)
                {
                    if (f.Type != 3 || f.Parts.Count == 0) continue;
                    double sx = 0, sy = 0; int[] r = f.Parts[0];
                    for (int i = 0; i < r.Length; i += 2) { sx += r[i]; sy += r[i + 1]; }
                    double la = Geo.TileLat(dy + sy / (r.Length / 2) / L.Extent, 14), lo = Geo.TileLon(dx + sx / (r.Length / 2) / L.Extent, 14);
                    double d = Math.Sqrt(Math.Pow((la - dla) * 111195, 2) + Math.Pow((lo - dlo) * 111195 * Math.Cos(dla * Geo.Deg), 2));
                    if (d > rad) continue;
                    var sb = new System.Text.StringBuilder();
                    for (int k = 0; k < L.Keys.Count; k++) { object o = L.Get(f, k); if (o != null) sb.Append(L.Keys[k] + "=" + o + " "); }
                    Console.WriteLine($"{kv.Key} {d:F0}m e {(lo - dlo) * 111195 * Math.Cos(dla * Geo.Deg):F0} n {(la - dla) * 111195:F0} pts {r.Length / 2} rings {f.Parts.Count}: {sb}");
                }
            }
            return;
        }
        if (a[0] == "bclass")
        {
            // bclass lat lon n : tally of class / subtype over n x n squares
            var srcD = new TileSources("/tmp/earthcache");
            int n = int.Parse(a[3]);
            int dx = (int)Geo.LonToX(double.Parse(a[2]), 14) - n / 2, dy = (int)Geo.LatToY(double.Parse(a[1]), 14) - n / 2;
            var seen = new System.Collections.Generic.SortedDictionary<string, int>();
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
            {
                var ls = Mvt.Decode(srcD.Buildings.GetTile(14, dx + i, dy + j), "building", "building_part");
                foreach (var kv in ls) foreach (var f in kv.Value.Features)
                {
                    var L = kv.Value;
                    string v = kv.Key + " " + (L.Str(f, L.Key("subtype")) ?? "-") + " / " + (L.Str(f, L.Key("class")) ?? "-");
                    int c; seen.TryGetValue(v, out c); seen[v] = c + 1;
                }
            }
            foreach (var kv in seen) Console.WriteLine(kv.Value + "  " + kv.Key);
            return;
        }
        if (a[0] == "infra")
        {
            // infra lat lon [radius] : tally of the infrastructure layer of the base square there; with a radius, what lies near
            var srcD = new TileSources("/tmp/earthcache");
            double dla = double.Parse(a[1]), dlo = double.Parse(a[2]), rad = a.Length > 3 ? double.Parse(a[3]) : 0;
            int dx = (int)Geo.LonToX(dlo, 13), dy = (int)Geo.LatToY(dla, 13);
            var ls = Mvt.Decode(srcD.Base.GetTile(13, dx, dy), "infrastructure");
            MvtLayer L; if (!ls.TryGetValue("infrastructure", out L)) { Console.WriteLine("no layer"); return; }
            var seen = new System.Collections.Generic.SortedDictionary<string, int>();
            foreach (var f in L.Features)
            {
                string v = "type" + f.Type + " " + (L.Str(f, L.Key("subtype")) ?? "-") + " / " + (L.Str(f, L.Key("class")) ?? "-");
                int c; seen.TryGetValue(v, out c); seen[v] = c + 1;
                if (rad > 0)
                {
                    int[] r = f.Parts[0];
                    double la = Geo.TileLat(dy + (double)r[1] / L.Extent, 13), lo = Geo.TileLon(dx + (double)r[0] / L.Extent, 13);
                    double d = Math.Sqrt(Math.Pow((la - dla) * 111195, 2) + Math.Pow((lo - dlo) * 111195 * Math.Cos(dla * Geo.Deg), 2));
                    if (d > rad) continue;
                    var sb = new System.Text.StringBuilder();
                    for (int k = 0; k < L.Keys.Count; k++) { object o = L.Get(f, k); if (o != null && L.Keys[k] != "sources") sb.Append(L.Keys[k] + "=" + o + " "); }
                    Console.WriteLine($"  near {d:F0}m type {f.Type} pts {r.Length / 2}: {sb}");
                }
            }
            foreach (var kv in seen) Console.WriteLine(kv.Value + "  " + kv.Key);
            return;
        }
        if (a[0] == "structs")
        {
            // structs lat lon : towers, tanks and the like in the base square there, and the map buildings that stand on them
            var srcD = new TileSources("/tmp/earthcache");
            double dla = double.Parse(a[1]), dlo = double.Parse(a[2]);
            int dx = (int)Geo.LonToX(dlo, 13), dy = (int)Geo.LatToY(dla, 13);
            var ls = Mvt.Decode(srcD.Base.GetTile(13, dx, dy), "infrastructure");
            MvtLayer L; if (!ls.TryGetValue("infrastructure", out L)) { Console.WriteLine("no layer"); return; }
            var blds = new System.Collections.Generic.List<(double la, double lo, string d, double[] ring)>();
            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
            {
                var bl = Mvt.Decode(srcD.Buildings.GetTile(14, dx * 2 + i, dy * 2 + j), "building");
                MvtLayer B; if (!bl.TryGetValue("building", out B)) continue;
                foreach (var f in B.Features)
                {
                    if (f.Type != 3) continue;
                    int[] r = f.Parts[0]; var ring = new double[r.Length]; double sx = 0, sy = 0;
                    for (int k = 0; k < r.Length; k += 2) { ring[k] = Geo.TileLon(dx * 2 + i + (double)r[k] / B.Extent, 14); ring[k + 1] = Geo.TileLat(dy * 2 + j + (double)r[k + 1] / B.Extent, 14); sx += ring[k]; sy += ring[k + 1]; }
                    double ar = 0; for (int k = 0, q = r.Length - 2; k < r.Length; q = k, k += 2) ar += ring[q] * ring[k + 1] - ring[k] * ring[q + 1];
                    ar = Math.Abs(ar) / 2 * 111195.0 * 111195.0 * Math.Cos(dla * Geo.Deg);
                    blds.Add((sy / (r.Length / 2), sx / (r.Length / 2), $"cls {B.Str(f, B.Key("class")) ?? "-"} sub {B.Str(f, B.Key("subtype")) ?? "-"} h {B.Num(f, B.Key("height"), -1):F0} area {ar:F0} parts {B.Flag(f, B.Key("has_parts"))} mat {B.Str(f, B.Key("facade_material")) ?? "-"}", ring));
                }
            }
            Func<double[], double, double, bool> inside = (ring, lo, la) => { bool c = false; for (int k = 0, q = ring.Length - 2; k < ring.Length; q = k, k += 2) if ((ring[k + 1] > la) != (ring[q + 1] > la) && lo < (ring[q] - ring[k]) * (la - ring[k + 1]) / (ring[q + 1] - ring[k + 1]) + ring[k]) c = !c; return c; };
            var tally = new System.Collections.Generic.SortedDictionary<string, int>();
            foreach (var f in L.Features)
            {
                string sub = L.Str(f, L.Key("subtype")), cls = L.Str(f, L.Key("class"));
                if (sub != "communication" && sub != "tower" && sub != "utility" && !(sub == "power" && (cls == "power_tower" || cls == "generator"))) continue;
                if (cls == "lighting" || cls == "utility_pole" || cls == "pipeline" || f.Type == 2) continue;
                int[] r = f.Parts[0]; var ring = new double[r.Length]; double sx = 0, sy = 0;
                for (int k = 0; k < r.Length; k += 2) { ring[k] = Geo.TileLon(dx + (double)r[k] / L.Extent, 13); ring[k + 1] = Geo.TileLat(dy + (double)r[k + 1] / L.Extent, 13); sx += ring[k]; sy += ring[k + 1]; }
                double clo = sx / (r.Length / 2), cla = sy / (r.Length / 2);
                string hit = null; int hits = 0;
                foreach (var b in blds)
                    if (f.Type == 3 ? inside(ring, b.lo, b.la) || inside(b.ring, clo, cla) : inside(b.ring, clo, cla)) { hits++; hit = b.d; }
                string key = (f.Type == 3 ? "area " : "point ") + sub + "/" + cls + (hits == 0 ? "  no building" : "  ON BUILDING");
                int c; tally.TryGetValue(key, out c); tally[key] = c + 1;
                if (hits > 0 && a.Length > 3) Console.WriteLine($"  {(f.Type == 3 ? "area" : "point")} {cls} h {L.Num(f, L.Key("height"), -1):F0} {L.Str(f, L.Key("@name"))} at {cla:F5},{clo:F5} -> {hits} bld: {hit}");
            }
            foreach (var kv in tally) Console.WriteLine(kv.Value + "  " + kv.Key);
            return;
        }
        if (a[0] == "bview")
        {
            // bview lat lon radius out : the buildings within radius game metres of a spot, for bview.py
            var srcD = new TileSources("/tmp/earthcache");
            double dla = double.Parse(a[1]), dlo = double.Parse(a[2]); float rad = float.Parse(a[3]);
            int dx = (int)Geo.LonToX(dlo, 14), dy = (int)Geo.LatToY(dla, 14);
            Buildings.Counts = new int[16];
            var swB = Stopwatch.StartNew();
            var tb = TileBuilder.Build(srcD, 14, dx, dy, 256);
            Console.WriteLine("built in " + swB.ElapsedMilliseconds + " ms; shapes by kind (mast shaft cooling water tank silo covered chimney lighthouse): " + string.Join(" ", new ArraySegment<int>(Buildings.Counts, 1, 9)));
            float px = (float)((Geo.LonToX(dlo, 14) - dx - 0.5) * tb.Size), pz = (float)((0.5 - (Geo.LatToY(dla, 14) - dy)) * tb.Size);
            float py = tb.HeightAt(px, pz);
            var all = new System.Collections.Generic.List<MeshData>(tb.Walls) { tb.Roofs };
            int n = 0;
            using (var f = new BinaryWriter(File.Create(a[4])))
            {
                f.Write(0);
                foreach (var m in all)
                    for (int i = 0; i + 2 < m.I.Count; i += 3)
                    {
                        int v0 = m.I[i];
                        if (Math.Abs(m.P[v0 * 3] - px) > rad || Math.Abs(m.P[v0 * 3 + 2] - pz) > rad) continue;
                        for (int j = 0; j < 3; j++) { int v = m.I[i + j]; f.Write(m.P[v * 3] - px); f.Write(m.P[v * 3 + 1] - py); f.Write(m.P[v * 3 + 2] - pz); }
                        for (int j = 0; j < 3; j++) { int v = m.I[i + j]; f.Write(m.U[v * 2]); f.Write(m.U[v * 2 + 1]); }
                        f.Write(m.X[v0 * 4 + 1]); f.Write(m.X[v0 * 4 + 2]); f.Write(m.Y[v0 * 4]); f.Write(m.Y[v0 * 4 + 2]);
                        n++;
                    }
                f.Seek(0, SeekOrigin.Begin); f.Write(n);
            }
            Console.WriteLine($"buildings {tb.BuildingCount}, triangles written {n}");
            return;
        }
        if (a[0] == "lm")
        {
            Directory.CreateDirectory(a[1]);
            int k = 0;
            foreach (var l in Landmarks.All)
            {
                if (l.Build == null) continue;
                var sw0 = Stopwatch.StartNew(); var lmm = Landmarks.Model(l); var md = lmm.Mesh; uint[] pal = lmm.Palette;
                float top = 0, ext = 0; for (int i = 0; i < md.P.Count; i += 3) { top = Math.Max(top, md.P[i + 1]); ext = Math.Max(ext, Math.Max(Math.Abs(md.P[i]), Math.Abs(md.P[i + 2]))); }
                Console.WriteLine($"{k,2} {l.Name,-40} tris {md.I.Count / 6,6} verts {md.Count,6} top {top,6:F1} m half-extent {ext,6:F1} m parts {lmm.Parts}  {sw0.ElapsedMilliseconds} ms");
                using (var f = new BinaryWriter(File.Create(Path.Combine(a[1], k.ToString("00") + ".lm"))))
                {
                    f.Write(l.Yaw); f.Write(md.I.Count / 6);
                    for (int ti = 0; ti < md.I.Count; ti += 6)
                    {
                        for (int j = 0; j < 3; j++) { int v = md.I[ti + j]; f.Write(md.P[v * 3]); f.Write(md.P[v * 3 + 1]); f.Write(md.P[v * 3 + 2]); }
                        int v0 = md.I[ti]; int ci = (int)(md.U[v0 * 2] * 16) + 16 * (int)(md.U[v0 * 2 + 1] * 16);
                        f.Write(pal[ci]);
                    }
                }
                File.WriteAllText(Path.Combine(a[1], k.ToString("00") + ".txt"), l.Name);
                k++;
            }
            return;
        }
        double lat = double.Parse(a[0]), lon = double.Parse(a[1]);
        int z = int.Parse(a[2]); int tex = int.Parse(a[3]); string outp = a[4];
        var src = new TileSources("/tmp/earthcache");
        int x = (int)Geo.LonToX(lon, z), y = (int)Geo.LatToY(lat, z);
        var sw = Stopwatch.StartNew();
        var t = TileBuilder.Build(src, z, x, y, tex);
        Console.WriteLine($"tile {z}/{x}/{y} size {t.Size:F1} m  built in {sw.ElapsedMilliseconds} ms, downloaded {Net.BytesDownloaded / 1e6:F1} MB in {Net.Requests} requests");
        float minY = 1e9f, maxY = -1e9f;
        for (int i = 1; i < t.Pos.Length; i += 3) { minY = Math.Min(minY, t.Pos[i]); maxY = Math.Max(maxY, t.Pos[i]); }
        Console.WriteLine($"ground y {minY:F1}..{maxY:F1}, patches {t.Ground.Length}, ground verts {t.Ground[0].Count}");
        if (t.Roofs != null)
        {
            int wv = 0; foreach (var w in t.Walls) wv += w.Count;
            Console.WriteLine($"buildings {t.BuildingCount} roofVerts {t.Roofs.Count} roofTris {t.Roofs.I.Count / 3} wallVerts {wv}; roads {t.RoadCount} verts {t.Roads.Count}; trees {t.TreeCount} verts {t.Trees.Count}");
            // roof area check and winding check
            double area = 0; int down = 0;
            var m = t.Roofs;
            for (int i = 0; i < m.I.Count; i += 3)
            {
                int A = m.I[i] * 3, B = m.I[i + 1] * 3, C = m.I[i + 2] * 3;
                double ux = m.P[B] - m.P[A], uz = m.P[B + 2] - m.P[A + 2], vx = m.P[C] - m.P[A], vz = m.P[C + 2] - m.P[A + 2];
                double cy = uz * vx - ux * vz; area += Math.Abs(cy) / 2; if (cy < 0) down++;
            }
            Console.WriteLine($"roof area {area:F0} m2 ({area / (t.Size * t.Size) * 100:F1}% of tile), downward tris {down}");
            using (var f = new BinaryWriter(File.Create(outp + ".mesh")))
            {
                foreach (var md in new[] { t.Roofs, t.Roads, t.Trees }) { f.Write(md.Count); foreach (float v in md.P) f.Write(v); f.Write(md.I.Count); foreach (int v in md.I) f.Write(v); }
            }
        }
        File.WriteAllBytes(outp, t.Texture);
        using (var f = new BinaryWriter(File.Create(outp + ".pos"))) foreach (float v in t.Pos) f.Write(v);
    }
}
