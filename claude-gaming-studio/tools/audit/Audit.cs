// World audit for Mars Needs Water: runs the game's own ground, road and river rules (the real game sources, compiled
// against a small stand-in for Unity's maths) over every road, railway and river on the planet and counts every place
// where something would look or play wrong. usage: dotnet run -c Release -- <Assets/Game dir> <report.txt>
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MarsNeedsWater.Game;

static class Audit
{
    sealed class Issue { public string Kind; public float Lat, Lon, Size; public string Note; }
    static readonly List<Issue> Issues = new List<Issue>();
    static string Summary = "";
    static readonly Dictionary<string, int> Count = new Dictionary<string, int>();
    static readonly Dictionary<string, double> Metres = new Dictionary<string, double>();

    static void Flag(string kind, Vector3 dir, float size, string note = "")
    {
        Count[kind] = Count.TryGetValue(kind, out int n) ? n + 1 : 1;
        EarthData.LatLon(dir, out float la, out float lo);
        Issues.Add(new Issue { Kind = kind, Lat = la, Lon = lo, Size = size, Note = note });
    }
    static void Add(string k, double m) { Metres[k] = Metres.TryGetValue(k, out double v) ? v + m : m; }

    static long Cell(Vector3 p, float size)
    {
        long x = (long)Math.Floor(p.x / size) + 100000, y = (long)Math.Floor(p.y / size) + 100000, z = (long)Math.Floor(p.z / size) + 100000;
        return (x << 42) | (y << 21) | z;
    }

    static int Main(string[] a)
    {
        string game = a[0];
        Resources.Roots = new[] { System.IO.Path.Combine(game, "Data/Resources"), System.IO.Path.Combine(game, "Art/Resources") };
        var planet = new Planet();
        if (!planet.Headless()) { Console.WriteLine("could not load the Earth data"); return 2; }
        var farm = EarthData.Dir(Planet.FarmLat, Planet.FarmLon);
        Roads.Focus(farm); Rivers.Focus(farm);
        var roads = Roads._chunks; var rivers = Rivers._chunks;
        Console.WriteLine("hub placement, times each thing stood in the way: " + string.Join(", ", Hubs.Why.Select(k => k.Key + " " + k.Value)));
        if (a.Length > 2 && a[2] == "hubs") { Console.WriteLine($"airports {Hubs._all.Count(h => !h.Port)}, ports {Hubs._all.Count(h => h.Port)}"); return 0; }
        if (a.Length > 2 && a[2] == "probe")
        {
            for (int q = 3; q + 1 < a.Length; q += 2)
            {
                var d = EarthData.Dir(float.Parse(a[q]), float.Parse(a[q + 1]));
                Rivers.Focus(d); Roads.Focus(d);
                bool rl = Rivers.Level(d, out float surf);
                Console.WriteLine($"{a[q]},{a[q + 1]}: natural {Planet.NaturalHeight(d):0.00} beforeRivers {Planet.HeightBeforeRivers(d):0.00} height {planet.Height(d):0.00} water {Planet.WaterLevel(d):0.00} river {rl} {surf:0.00} hub {Hubs.Near(d, 0f)} city {CityReplica.Covers(d):0.00} farmcheb {planet.FarmCheb(d):0}");
                Planet.TangentAt(d, out var e, out var n);
                for (int k = -4; k <= 4; k++) { var p = (d * Planet.Radius + e * (k * 5f)).normalized; Rivers.Level(p, out float s2); Console.Write($"  E{k * 5}: {Planet.HeightBeforeRivers(p):0.0}/{planet.Height(p):0.0}/{s2:0.0}"); }
                Console.WriteLine();
                for (int i = 0; i < roads.Length; i++)
                {
                    var c = roads[i];
                    if (c.Cls > 3 || (c.Centre - d).magnitude * Planet.Radius > c.Rad + 30f) continue;
                    var pr = Roads.Profile(c, planet); if (pr.M < 1) continue;
                    int bs = 0; float bd = 1e9f;
                    for (int s = 0; s <= pr.M; s++) { float dd = (pr.Dir[s] - d).magnitude * Planet.Radius; if (dd < bd) { bd = dd; bs = s; } }
                    if (bd > 30f) continue;
                    Console.WriteLine($"  chunk {i} cls {c.Cls} half {c.Half} dist {bd:0.0} sec {bs}/{pr.M} from {pr.From} to {pr.To} flags {c.Flags}");
                    for (int s = Math.Max(0, bs - 4); s <= Math.Min(pr.M, bs + 4); s++)
                        Console.WriteLine($"    s{s} need {pr.Need[s]:0.0} gc {pr.Gc[s]:0.0} yc {pr.Yc[s]:0.0} wet {pr.Wet[s]} raised {pr.Raised[s]} now {planet.Height(pr.Dir[s]):0.0} riv {Rivers.Level(pr.Dir[s], out _)} water {Planet.WaterLevel(pr.Dir[s]):0.0}");
                }
            }
            return 0;
        }
        Console.WriteLine($"road chunks {roads.Length}, river chunks {rivers.Length}, cities {CityReplica.Count}, airports {Hubs._all.Count(h => !h.Port)}, ports {Hubs._all.Count(h => h.Port)}");
        Summary = $"airports {Hubs._all.Count(h => !h.Port)}, ports {Hubs._all.Count(h => h.Port)}, rebuilt cities {CityReplica.Count}, towns {Places.All.Count}";

        // Which line each road chunk belongs to.
        var line = new int[roads.Length];
        for (int i = 0, id = -1; i < roads.Length; i++) { if (i == 0 || (roads[i].Flags & 1) != 0 || roads[i].Cls != roads[i - 1].Cls) id++; line[i] = id; }

        const float Region = 1000f;
        var regions = new Dictionary<long, (List<int> roads, List<int> rivers, Vector3 sum)>();
        void Put(Vector3 centre, int i, bool river)
        {
            long k = Cell(centre * Planet.Radius, Region);
            if (!regions.TryGetValue(k, out var r)) r = (new List<int>(), new List<int>(), Vector3.zero);
            (river ? r.rivers : r.roads).Add(i); r.sum += centre; regions[k] = r;
        }
        for (int i = 0; i < roads.Length; i++) Put(roads[i].Centre, i, false);
        for (int i = 0; i < rivers.Length; i++) Put(rivers[i].Centre, i, true);
        Console.WriteLine($"regions {regions.Count}");

        var sw = System.Diagnostics.Stopwatch.StartNew(); int done = 0;
        foreach (var kv in regions.OrderBy(k => k.Key))
        {
            var f = kv.Value.sum.normalized;
            Rivers.Focus(f); Roads.Focus(f);
            AuditRoads(planet, kv.Value.roads, line);
            AuditRivers(planet, kv.Value.rivers);
            if (++done % 500 == 0) Console.WriteLine($"  {done}/{regions.Count} regions, {sw.Elapsed.TotalSeconds:0}s, issues {Issues.Count}");
        }

        using (var w = new System.IO.StreamWriter(a[1]))
        {
            w.WriteLine("WORLD AUDIT");
            w.WriteLine("on the map: " + Summary);
            w.WriteLine("checked, km: " + string.Join(", ", Metres.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value / 1000:0}")));
            w.WriteLine("issues: " + (Issues.Count == 0 ? "none" : string.Join(", ", Count.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))));
            w.WriteLine("sizes (median / 90% / worst): " + string.Join(", ", Issues.GroupBy(i => i.Kind).OrderBy(g => g.Key).Select(g => { var v = g.Select(x => x.Size).OrderBy(x => x).ToArray(); return $"{g.Key} {v[v.Length / 2]:0.00}/{v[(int)(v.Length * 0.9)]:0.00}/{v[v.Length - 1]:0.00}"; })));
            foreach (var g in Issues.GroupBy(i => i.Kind).OrderBy(g => g.Key))
            {
                w.WriteLine(); w.WriteLine($"== {g.Key}: {g.Count()}");
                foreach (var i in g.OrderByDescending(i => i.Size).Take(25)) w.WriteLine($"  {i.Lat:0.0000}, {i.Lon:0.0000}  {i.Size:0.00}  {i.Note}");
            }
        }
        Console.WriteLine(System.IO.File.ReadAllText(a[1]).Split("\n\n")[0]);
        Console.WriteLine($"total {sw.Elapsed.TotalSeconds:0}s");
        return Issues.Count == 0 ? 0 : 1;
    }

    static readonly string[] ClsName = { "highway", "road", "link road", "railway" };

    static void AuditRoads(Planet planet, List<int> mine, int[] line)
    {
        var roads = Roads._chunks;
        // Every cross-section of every active chunk in reach, for the overlap and crossing checks.
        var grid = new Dictionary<long, List<(int chunk, int s)>>();
        var prof = new Dictionary<int, Roads.Section>();
        Roads.Section Prof(int i) { if (!prof.TryGetValue(i, out var p)) prof[i] = p = Roads.Profile(roads[i], planet); return p; }
        var near = new HashSet<int>(mine);
        foreach (int i in mine)
        {
            // neighbours in reach: any active chunk whose centre is within 700 m
            var c = roads[i];
            for (int d = -40; d <= 40; d++) { int j = i + d; if (j >= 0 && j < roads.Length && roads[j].Active && (roads[j].Centre - c.Centre).magnitude * Planet.Radius < 700f) near.Add(j); }
        }
        foreach (int i in near)
        {
            var c = roads[i];
            if (!c.Active || c.Cls > 3) continue;
            var p = Prof(i);
            for (int s = p.From; s <= p.To; s++)
            {
                long k = Cell(p.Dir[s] * Planet.Radius, 8f);
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(int, int)>();
                l.Add((i, s));
            }
        }
        var overlap = new Dictionary<(int, int), (float m, Vector3 at)>();
        var shallow = new HashSet<(int, int)>();
        foreach (int i in mine)
        {
            var c = roads[i];
            if (!c.Active || c.Cls > 3) continue;
            var p = Prof(i);
            if (p.M < 1 || p.To - p.From < 1) continue;
            string what = ClsName[c.Cls];
            Add(what, p.Along[p.To] - p.Along[p.From]);
            float grade = Roads.Grade(c.Cls);
            float worstPoke = 0f, worstGrade = 0f, worstTurn = 0f, worstAir = 0f; int pokeAt = 0, gradeAt = 0, turnAt = 0, airAt = 0;
            for (int s = p.From; s <= p.To; s++)
            {
                // the finished ground (after the earthworks) against the road surface: under the centre, both edges and between
                float poke = float.MinValue, air = 0f;
                if (s % 2 == 0)
                    for (int e = -2; e <= 2; e++)
                    {
                        float g = planet.Height((p.Dir[s] * Planet.Radius + p.Side[s] * (e * roads[i].Half * 0.5f)).normalized);
                        poke = Mathf.Max(poke, g - p.Yc[s]);
                        if (!p.Raised[s] && e == 0) air = p.Yc[s] - g;
                    }
                if (air > worstAir) { worstAir = air; airAt = s; }
                if (poke > worstPoke) { worstPoke = poke; pokeAt = s; }
                if (s < p.To)
                {
                    float gap = Mathf.Max(0.5f, p.Along[s + 1] - p.Along[s]);
                    float g = Mathf.Abs(p.Yc[s + 1] - p.Yc[s]) / gap;
                    if (g > worstGrade) { worstGrade = g; gradeAt = s; }
                    // a hard turn: more than 40 degrees at one point, or more than 60 within 10 m
                    float turn = Mathf.Acos(Mathf.Clamp(Vector3.Dot(p.Fwd[s], p.Fwd[s + 1]), -1f, 1f)) * Mathf.Rad2Deg;
                    float within = turn;
                    for (int q = s + 1; q < p.To && p.Along[q] - p.Along[s] <= 10f; q++) within += Mathf.Acos(Mathf.Clamp(Vector3.Dot(p.Fwd[q], p.Fwd[q + 1]), -1f, 1f)) * Mathf.Rad2Deg;
                    float hard = Mathf.Max(turn / 40f, within / 60f) * 40f;
                    if (hard > worstTurn) { worstTurn = hard; turnAt = s; }
                }
                if (Hubs.Forbidden(p.Dir[s]) && s % 6 == 0) Flag(what + " across an airport or port", p.Dir[s], 1f);
                if (planet.FarmCheb(p.Dir[s]) < Planet.FarmHalf + 8f && s % 6 == 0) Flag(what + " across the farm", p.Dir[s], 1f);
                // other lines: running on top of this one, or crossing it at a different height
                long k0 = Cell(p.Dir[s] * Planet.Radius, 8f);
                var P = p.Dir[s] * Planet.Radius;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                {
                    long k = k0 + ((long)dx << 42) + ((long)dy << 21) + dz;
                    if (!grid.TryGetValue(k, out var l)) continue;
                    foreach (var (j, t) in l)
                    {
                        if (line[j] == line[i]) continue;
                        var q = Prof(j); var o = roads[j];
                        float dist = (q.Dir[t] * Planet.Radius - P).magnitude;
                        float par = Mathf.Abs(Vector3.Dot(p.Fwd[Mathf.Min(s, p.M)], q.Fwd[Mathf.Min(t, q.M)]));
                        if (dist < (c.Half + o.Half) * 0.7f && par > 0.85f)
                        {
                            var key = (Math.Min(line[i], line[j]), Math.Max(line[i], line[j]));
                            overlap.TryGetValue(key, out var v);
                            if (line[i] < line[j] || true) overlap[key] = (v.m + 2.5f, p.Dir[s]);
                        }
                        // a railway and a road that cross must cross squarely, not ride along each other
                        if (dist < 2.6f && par > 0.766f && (c.Cls == 3) != (o.Cls == 3) && shallow.Add((Math.Min(line[i], line[j]), Math.Max(line[i], line[j]))))
                            Flag("railway crosses a road at a shallow angle", p.Dir[s], Mathf.Acos(Mathf.Min(1f, par)) * Mathf.Rad2Deg);
                        else if (dist < 2.6f && par <= 0.85f)
                        {
                            // the other line's cross-section, dropped onto this line: only when it lies on the centre line
                            int s0 = Math.Max(p.From, s - 1), s1 = Math.Min(p.To, s + 1);
                            float bestD = float.MaxValue, myY = 0f;
                            for (int z = s0; z < s1; z++)
                            {
                                var a0 = p.Dir[z]; var ab = p.Dir[z + 1] - a0;
                                float tt = Mathf.Clamp01(Vector3.Dot(q.Dir[t] - a0, ab) / Mathf.Max(1e-14f, ab.sqrMagnitude));
                                float dd = (q.Dir[t] - (a0 + ab * tt)).magnitude * Planet.Radius;
                                if (dd < bestD) { bestD = dd; myY = Mathf.Lerp(p.Yc[z], p.Yc[z + 1], tt); }
                            }
                            float dh = Mathf.Abs(myY - q.Yc[t]);
                            if (bestD < 0.8f && t >= q.From && t <= q.To && dh > 0.3f) Flag("crossing at different heights", p.Dir[s], dh, what + " and " + ClsName[o.Cls]);
                        }
                    }
                }
            }
            if (worstPoke > 0.05f) Flag(what + " below the ground", p.Dir[pokeAt], worstPoke);
            if (worstAir > 1f) Flag(what + " in the air without a bridge", p.Dir[airAt], worstAir);
            if (worstGrade > grade + 0.04f) Flag(what + " too steep", p.Dir[gradeAt], worstGrade);
            if (worstTurn > 40f) Flag(what + " hard turn", p.Dir[turnAt], worstTurn);
            // the joint with the next piece of the line
            if (p.To == p.M && (c.Flags & 2) == 0 && i + 1 < roads.Length && roads[i + 1].Cls == c.Cls && roads[i + 1].Active)
            {
                var q = Prof(i + 1);
                if (q.M >= 1 && q.From == 0 && q.To >= 1)
                {
                    float step = Mathf.Abs(q.Yc[0] - p.Yc[p.M]);
                    if (step > 0.12f) Flag(what + " step between pieces", p.Dir[p.M], step);
                    float turn = Mathf.Acos(Mathf.Clamp(Vector3.Dot(p.Fwd[p.M], q.Fwd[0]), -1f, 1f)) * Mathf.Rad2Deg;
                    if (turn > 40f) Flag(what + " hard turn", p.Dir[p.M], turn);
                }
                else if (p.Yc[p.M] - planet.Height(p.Dir[p.M]) > 1.2f) Flag(what + " ends in the air", p.Dir[p.M], p.Yc[p.M] - p.Gc[p.M], "next piece not built");
            }
            else if (p.Yc[p.To] - planet.Height(p.Dir[p.To]) > 1.2f && (p.To < p.M || (c.Flags & 2) != 0 || i + 1 >= roads.Length || !roads[i + 1].Active)) Flag(what + " ends in the air", p.Dir[p.To], p.Yc[p.To] - p.Gc[p.To]);
            if (p.Yc[p.From] - planet.Height(p.Dir[p.From]) > 1.2f && (p.From > 0 || (c.Flags & 1) != 0 || i == 0 || !roads[i - 1].Active)) Flag(what + " ends in the air", p.Dir[p.From], p.Yc[p.From] - p.Gc[p.From]);
        }
        // A road may end beside another road, in a town or city, at a shore, at an airport, a port or the farm. Anywhere else it stops in the middle of nowhere.
        foreach (int i in mine)
        {
            var c = roads[i];
            if (!c.Active || c.Cls > 3 || c.P == null) continue;
            bool rail = c.Cls == 3; float touch = 2.5f;                 // an end must lie on the other line, as it looks on screen: beside it is a gap
            for (int e = 0; e < 2; e++)
            {
                if ((c.Flags & (e == 0 ? 1 : 2)) == 0) continue;
                var E = e == 0 ? c.P[0] : c.P[c.N - 1];
                var B = e == 0 ? c.P[Math.Min(c.N - 1, 1)] : c.P[Math.Max(0, c.N - 2)];
                var t = E - B; if (t.magnitude < 1e-9f) continue; t = t * (1f / t.magnitude);
                bool ok = CityReplica.Covers(E) > 0f || Hubs.Near(E, 40f) || planet.FarmCheb(E) < Planet.FarmHalf + 120f;
                for (int q = 0; q <= 3 && !ok; q++)
                {
                    var a = (E * Planet.Radius + t * (q * 15f)).normalized;
                    ok = Planet.HeightBeforeRoads(a) < Planet.WaterLevel(a) + 0.5f || Rivers.Level(a, out _);
                }
                if (!ok) foreach (var pl in Places.All) if ((pl.Dir - E).magnitude * Planet.Radius < pl.Radius + 35f) { ok = true; break; }
                for (int j = 0; j < roads.Length && !ok; j++)
                {
                    var o = roads[j];
                    if (!o.Active || o.Cls > 3 || (o.Cls == 3) != rail || o.P == null || line[j] == line[i]) continue;
                    if ((o.Centre - E).magnitude * Planet.Radius > o.Rad + touch) continue;
                    for (int z = 0; z < o.N - 1 && !ok; z++)
                    {
                        var a0 = o.P[z]; var ab = o.P[z + 1] - a0;
                        float tt = Mathf.Clamp01(Vector3.Dot(E - a0, ab) / Mathf.Max(1e-14f, ab.sqrMagnitude));
                        ok = (E - (a0 + ab * tt)).magnitude * Planet.Radius < touch;
                    }
                }
                if (!ok) Flag(ClsName[c.Cls] + " stops in the middle of nowhere", E, 1f);
            }
        }
        foreach (var kv in overlap)
            if (kv.Value.m > 30f) Flag("two lines on top of each other", kv.Value.at, kv.Value.m, ClsName[roads[Array.IndexOf(line, kv.Key.Item1)].Cls] + " and " + ClsName[roads[Array.IndexOf(line, kv.Key.Item2)].Cls]);
    }

    static void AuditRivers(Planet planet, List<int> mine)
    {
        var rivers = Rivers._chunks;
        foreach (int i in mine)
        {
            var c = rivers[i];
            if (!c.Active || c.P == null) continue;
            // A river that stops although its line carries on: a rebuilt city, an airport or a port, or the farm took the ground.
            for (int k = 0; k + 1 < c.N; k++)
                if ((c.Off[k] == 2) != (c.Off[k + 1] == 2) && c.Off[k] != 1 && c.Off[k + 1] != 1)
                {
                    var x = c.Off[k] == 2 ? c.P[k] : c.P[k + 1];
                    Flag("river stops where its line carries on", x, 1f, planet.FarmCheb(x) < Planet.FarmHalf + 110f ? "the farm" : CityReplica.Covers(x) > 0.5f ? "a rebuilt city" : "an airport or port");
                }
            var rb = Rivers.Shape(c, planet);
            if (rb.M < 1) continue;
            float worstFloat = 0f, worstDry = float.MinValue, worstLevee = 0f; Vector3 floatAt = default, dryAt = default, leveeAt = default; string note = "";
            for (int s = 0; s <= rb.M; s++)
            {
                bool drawn = (s < rb.M && !rb.Skip[s + 1]) || (s > 0 && !rb.Skip[s]);
                if (!drawn) continue;
                var d = rb.Dir[s];
                if (s < rb.M && !rb.Skip[s + 1]) Add("river", (rb.Dir[s + 1] - d).magnitude * Planet.Radius);
                float hc = planet.Height(d);
                if (hc < Planet.WaterLevel(d) - 0.2f) continue;         // the mouth: under the lake or sea already
                float top = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    var ed = (d * Planet.Radius + rb.Side[s] * rb.Off4[k]).normalized;
                    float g = planet.Height(ed);
                    top = Mathf.Max(top, rb.Y[s][k]);
                    if (g < Planet.WaterLevel(ed) + 0.05f) continue;    // this point meets other water
                    float fl = rb.Y[s][k] - g;
                    if (Rivers.Level(ed, out float sv) && g <= sv) fl = 0f;      // over another river's water, not in the air
                    if ((k == 0 || k == 3) && fl > worstFloat) { worstFloat = fl; floatAt = d; note = $"water {rb.Y[s][k]:0.0} ground {g:0.0}"; }
                    if (k == 0 || k == 3)
                    {
                        var bd = (d * Planet.Radius + rb.Side[s] * (rb.Off4[k] * 0.9f)).normalized;
                        float lv = planet.Height(bd) - Planet.HeightBeforeRivers(bd);
                        if (lv > worstLevee) { worstLevee = lv; leveeAt = d; }
                    }
                }
                // water must show: the bed under the middle of the ribbon lies below it
                float bed = Mathf.Min(hc, Mathf.Min(planet.Height((d * Planet.Radius + rb.Side[s] * rb.Off4[1]).normalized), planet.Height((d * Planet.Radius + rb.Side[s] * rb.Off4[2]).normalized)));
                float dry = bed - top;
                if (dry > worstDry) { worstDry = dry; dryAt = d; }
            }
            if (worstFloat > 0.45f) Flag("river water in the air", floatAt, worstFloat, note);
            if (worstDry > -0.1f) Flag("river with no water showing", dryAt, worstDry + 0.1f);
            if (worstLevee > 2f) Flag("river on a raised wall", leveeAt, worstLevee);
        }
    }
}
