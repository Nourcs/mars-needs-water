using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>Polygon triangulation with holes (ear clipping, after the mapbox earcut algorithm).</summary>
    public static class Earcut
    {
        sealed class Node
        {
            public int I; public double X, Y; public Node Prev, Next; public bool Steiner;
            public Node(int i, double x, double y) { I = i; X = x; Y = y; }
        }

        /// <param name="data">x,y pairs: the outer ring first, then each hole.</param>
        /// <param name="holes">Index of the first vertex of each hole, or null.</param>
        public static List<int> Triangulate(double[] data, int[] holes)
        {
            var tris = new List<int>();
            int n = data.Length / 2;
            bool hasHoles = holes != null && holes.Length > 0;
            int outerLen = hasHoles ? holes[0] : n;
            Node outer = LinkedList(data, 0, outerLen, true);
            if (outer == null || outer.Next == outer.Prev) return tris;
            if (hasHoles) outer = EliminateHoles(data, holes, outer);
            int guard = 0;
            EarcutLinked(outer, tris, 0, ref guard);
            return tris;
        }

        static Node LinkedList(double[] d, int start, int end, bool clockwise)
        {
            Node last = null;
            if (clockwise == (SignedArea(d, start, end) > 0))
                for (int i = start; i < end; i++) last = Insert(i, d[2 * i], d[2 * i + 1], last);
            else
                for (int i = end - 1; i >= start; i--) last = Insert(i, d[2 * i], d[2 * i + 1], last);
            if (last != null && Eq(last, last.Next)) { Remove(last); last = last.Next; }
            return last;
        }

        static double SignedArea(double[] d, int start, int end)
        {
            double sum = 0;
            for (int i = start, j = end - 1; i < end; j = i, i++)
                sum += (d[2 * j] - d[2 * i]) * (d[2 * i + 1] + d[2 * j + 1]);
            return sum;
        }

        static Node FilterPoints(Node start, Node end)
        {
            if (start == null) return null;
            if (end == null) end = start;
            Node p = start; bool again;
            do
            {
                again = false;
                if (!p.Steiner && (Eq(p, p.Next) || Area(p.Prev, p, p.Next) == 0))
                {
                    Remove(p);
                    p = end = p.Prev;
                    if (p == p.Next) break;
                    again = true;
                }
                else p = p.Next;
            } while (again || p != end);
            return end;
        }

        static void EarcutLinked(Node ear, List<int> tris, int pass, ref int guard)
        {
            if (ear == null) return;
            Node stop = ear;
            while (ear.Prev != ear.Next)
            {
                if (++guard > 4000000) return;
                Node prev = ear.Prev, next = ear.Next;
                if (IsEar(ear))
                {
                    tris.Add(prev.I); tris.Add(ear.I); tris.Add(next.I);
                    Remove(ear);
                    ear = next.Next; stop = next.Next;
                    continue;
                }
                ear = next;
                if (ear == stop)
                {
                    if (pass == 0) EarcutLinked(FilterPoints(ear, null), tris, 1, ref guard);
                    else if (pass == 1) { ear = CureLocalIntersections(FilterPoints(ear, null), tris); EarcutLinked(ear, tris, 2, ref guard); }
                    else if (pass == 2) SplitEarcut(ear, tris, ref guard);
                    break;
                }
            }
        }

        static bool IsEar(Node ear)
        {
            Node a = ear.Prev, b = ear, c = ear.Next;
            if (Area(a, b, c) >= 0) return false;
            double minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            double minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            Node p = c.Next;
            while (p != a)
            {
                if (p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY &&
                    PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) && Area(p.Prev, p, p.Next) >= 0) return false;
                p = p.Next;
            }
            return true;
        }

        static Node CureLocalIntersections(Node start, List<int> tris)
        {
            Node p = start;
            do
            {
                Node a = p.Prev, b = p.Next.Next;
                if (!Eq(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    tris.Add(a.I); tris.Add(p.I); tris.Add(b.I);
                    Remove(p); Remove(p.Next);
                    p = start = b;
                }
                p = p.Next;
            } while (p != start);
            return FilterPoints(p, null);
        }

        static void SplitEarcut(Node start, List<int> tris, ref int guard)
        {
            Node a = start;
            do
            {
                Node b = a.Next.Next;
                while (b != a.Prev)
                {
                    if (a.I != b.I && IsValidDiagonal(a, b))
                    {
                        Node c = SplitPolygon(a, b);
                        a = FilterPoints(a, a.Next);
                        c = FilterPoints(c, c.Next);
                        EarcutLinked(a, tris, 0, ref guard);
                        EarcutLinked(c, tris, 0, ref guard);
                        return;
                    }
                    b = b.Next;
                }
                a = a.Next;
            } while (a != start);
        }

        static Node EliminateHoles(double[] data, int[] holes, Node outer)
        {
            var queue = new List<Node>();
            int n = data.Length / 2;
            for (int i = 0; i < holes.Length; i++)
            {
                int start = holes[i], end = i < holes.Length - 1 ? holes[i + 1] : n;
                Node list = LinkedList(data, start, end, false);
                if (list == null) continue;
                if (list == list.Next) list.Steiner = true;
                queue.Add(GetLeftmost(list));
            }
            queue.Sort((p, q) => p.X.CompareTo(q.X));
            foreach (Node h in queue) outer = EliminateHole(h, outer);
            return outer;
        }

        static Node EliminateHole(Node hole, Node outer)
        {
            Node bridge = FindHoleBridge(hole, outer);
            if (bridge == null) return outer;
            Node bridgeReverse = SplitPolygon(bridge, hole);
            FilterPoints(bridgeReverse, bridgeReverse.Next);
            return FilterPoints(bridge, bridge.Next);
        }

        static Node FindHoleBridge(Node hole, Node outer)
        {
            Node p = outer;
            double hx = hole.X, hy = hole.Y, qx = double.NegativeInfinity;
            Node m = null;
            do
            {
                if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
                {
                    double x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.X < p.Next.X ? p : p.Next;
                        if (x == hx) return m;
                    }
                }
                p = p.Next;
            } while (p != outer);
            if (m == null) return null;
            Node stop = m;
            double mx = m.X, my = m.Y, tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.X && p.X >= mx && hx != p.X &&
                    PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
                {
                    double tan = Math.Abs(hy - p.Y) / (hx - p.X);
                    if (LocallyInside(p, hole) &&
                        (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                    {
                        m = p; tanMin = tan;
                    }
                }
                p = p.Next;
            } while (p != stop);
            return m;
        }

        static bool SectorContainsSector(Node m, Node p) { return Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0; }

        static Node GetLeftmost(Node start)
        {
            Node p = start, leftmost = start;
            do
            {
                if (p.X < leftmost.X || (p.X == leftmost.X && p.Y < leftmost.Y)) leftmost = p;
                p = p.Next;
            } while (p != start);
            return leftmost;
        }

        static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            return (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
                   (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
                   (bx - px) * (cy - py) >= (cx - px) * (by - py);
        }

        static bool IsValidDiagonal(Node a, Node b)
        {
            return a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b) &&
                   ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
                     (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0)) ||
                    (Eq(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));
        }

        static double Area(Node p, Node q, Node r) { return (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y); }
        static bool Eq(Node a, Node b) { return a.X == b.X && a.Y == b.Y; }

        static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Math.Sign(Area(p1, q1, p2)), o2 = Math.Sign(Area(p1, q1, q2));
            int o3 = Math.Sign(Area(p2, q2, p1)), o4 = Math.Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4) return true;
            if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
            return false;
        }

        static bool OnSegment(Node p, Node q, Node r)
        {
            return q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);
        }

        static bool IntersectsPolygon(Node a, Node b)
        {
            Node p = a;
            do
            {
                if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b)) return true;
                p = p.Next;
            } while (p != a);
            return false;
        }

        static bool LocallyInside(Node a, Node b)
        {
            return Area(a.Prev, a, a.Next) < 0
                ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
                : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;
        }

        static bool MiddleInside(Node a, Node b)
        {
            Node p = a; bool inside = false;
            double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
            do
            {
                if (((p.Y > py) != (p.Next.Y > py)) && p.Next.Y != p.Y &&
                    (px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X)) inside = !inside;
                p = p.Next;
            } while (p != a);
            return inside;
        }

        static Node SplitPolygon(Node a, Node b)
        {
            Node a2 = new Node(a.I, a.X, a.Y), b2 = new Node(b.I, b.X, b.Y), an = a.Next, bp = b.Prev;
            a.Next = b; b.Prev = a;
            a2.Next = an; an.Prev = a2;
            b2.Next = a2; a2.Prev = b2;
            bp.Next = b2; b2.Prev = bp;
            return b2;
        }

        static Node Insert(int i, double x, double y, Node last)
        {
            Node p = new Node(i, x, y);
            if (last == null) { p.Prev = p; p.Next = p; }
            else { p.Next = last.Next; p.Prev = last; last.Next.Prev = p; last.Next = p; }
            return p;
        }

        static void Remove(Node p) { p.Next.Prev = p.Prev; p.Prev.Next = p.Next; }
    }
}
