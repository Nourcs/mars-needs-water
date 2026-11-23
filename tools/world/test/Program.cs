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
