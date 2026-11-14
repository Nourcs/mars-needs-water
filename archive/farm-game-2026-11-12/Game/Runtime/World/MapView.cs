using MarsNeedsWater.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using Sim = MarsNeedsWater.Simulation.Simulation;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The map: a minimap in the top-right corner and a full-screen map on M (+/- to zoom, from 200 m up to the
    /// whole Earth). Local zooms read one small texture painted from the planet's real height data (seas by depth,
    /// land by height with hill shading, ice, the farm drawn on top), north up, centred on the player. It is
    /// repainted only after a good walk or a zoom change, a few rows per frame, so it never costs a frame hitch.
    /// The world zoom shows the Blue Marble image with your position. Presentation only.
    /// </summary>
    public sealed class MapView : MonoBehaviour
    {
        const int Res = 256;                      // texture size in pixels (64k samples per full repaint)
        const int RowsPerFrame = 32;              // repaint budget per frame
        static readonly float[] Zooms = { 200f, 400f, 800f, 1600f, 3200f, 12800f, 0f }; // metres across; 0 = whole Earth
        const float MiniRange = 160f;             // metres across the minimap

        Sim _sim;
        Transform _player;
        Planet _planet;
        Texture2D _world;
        // Tangent frame (planet space) the current texture was painted in.
        Vector3 _dir, _east, _north, _paintDir, _paintEast, _paintNorth;
        Texture2D _tex, _arrow, _dot, _star;
        Color[] _px;
        bool _open;
        int _zoom = 2;

        // The area the texture currently covers, and the one being painted.
        float _span, _paintSpan;
        int _paintRow = -1;
        GUIStyle _label, _title, _small;

        public void Init(Sim sim, Transform player, Planet planet)
        {
            _sim = sim;
            _player = player;
            _planet = planet;
            _world = Resources.Load<Texture2D>("Earth/earth_color");
            _tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false) { name = "Map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _px = new Color[Res * Res];
            _arrow = Arrow(32);
            _dot = Dot(12);
            _star = StarTex(24);
            // Paint the first map right away so the minimap is never blank.
            StartPaint(SpanFor(_zoom));
            while (_paintRow >= 0) PaintRows(Res);
        }

        Vector3 PlayerDir => _planet.WorldToDir(_player.position);

        /// <summary>Player's offset in metres (east, north) from the centre of the painted texture.</summary>
        Vector2 Offset(Vector3 dirNow, Vector3 centreDir, Vector3 east, Vector3 north)
        {
            var d = (dirNow - centreDir) * Planet.Radius;
            return new Vector2(Vector3.Dot(d, east), Vector3.Dot(d, north));
        }

        // The texture covers more than the screen shows, so short walks need no repaint. With the map closed it
        // covers just the minimap's surroundings, so the minimap stays sharp.
        float SpanFor(int zoom) => _open && Zooms[zoom] > 0f ? Zooms[zoom] * 1.5f : MiniRange * 2.5f;

        void Update()
        {
            if (_player == null) return;
            var kb = Keyboard.current;
            if (kb != null && _sim.Run == RunState.Running)
            {
                if (kb.mKey.wasPressedThisFrame) _open = !_open;
                // T with the map open: jump to the next rebuilt city, and after the last one back to the farm.
                if (_open && kb.tKey.wasPressedThisFrame && CityReplica.Count > 0)
                {
                    _visit = (_visit + (kb.shiftKey.isPressed ? CityReplica.Count : 1)) % (CityReplica.Count + 1);
                    _planet.Travel(_visit == 0 ? _planet.FarmDir : CityReplica.Arrival(_visit - 1));
                }
                if (_open && (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)) _zoom = Mathf.Max(0, _zoom - 1);
                if (_open && (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)) _zoom = Mathf.Min(Zooms.Length - 1, _zoom + 1);
            }

            // Repaint when the player nears the edge of what is painted, or the zoom changed.
            if (_paintRow < 0)
            {
                float want = SpanFor(_zoom);
                if (Mathf.Abs(want - _span) > 1f || Offset(PlayerDir, _dir, _east, _north).magnitude > _span * 0.12f) StartPaint(want);
            }
            if (_paintRow >= 0) PaintRows(RowsPerFrame);
        }

        int _visit; // 0 = the farm, otherwise 1 + the rebuilt city last jumped to

        void StartPaint(float span)
        {
            _paintDir = PlayerDir;
            Planet.TangentAt(_paintDir, out _paintEast, out _paintNorth);
            _paintSpan = span;
            _paintRow = 0;
        }

        void PaintRows(int rows)
        {
            float mpp = _paintSpan / Res; // metres per pixel
            float half = _paintSpan / 2f;
            int end = Mathf.Min(Res, _paintRow + rows);
            for (int y = _paintRow; y < end; y++)
            for (int x = 0; x < Res; x++)
            {
                float ex = -half + (x + 0.5f) * mpp, nz = -half + (y + 0.5f) * mpp;
                _px[y * Res + x] = Shade(ex, nz, mpp);
            }
            _paintRow = end;
            if (_paintRow >= Res)
            {
                _tex.SetPixels(_px);
                _tex.Apply(false);
                _dir = _paintDir;
                _east = _paintEast;
                _north = _paintNorth;
                _span = _paintSpan;
                _paintRow = -1;
            }
        }

        static readonly Color Deep = new Color(0.08f, 0.24f, 0.45f), Shallow = new Color(0.2f, 0.45f, 0.7f);
        static readonly Color Low = new Color(0.45f, 0.53f, 0.3f), Mid = new Color(0.66f, 0.62f, 0.42f), High = new Color(0.62f, 0.56f, 0.48f);

        Vector3 DirAt(float east, float north) => (_paintDir * Planet.Radius + _paintEast * east + _paintNorth * north).normalized;

        Color Shade(float ex, float nz, float mpp)
        {
            var d = DirAt(ex, nz);
            if (Vector3.Dot(d, _planet.FarmDir) > 0.9999f)
            {
                var farm = FarmColour(d);
                if (farm.a > 0f) return farm;
            }
            float h = _planet.Height(d);
            EarthData.LatLon(d, out float lat, out float lon);
            float wl = Planet.WaterLevel(d);
            if (h < wl)
                return Color.Lerp(Shallow, Deep, Mathf.Clamp01((wl - h) / 60f));
            Color c;
            if (EarthData.IceAt(lat, lon)) c = new Color(0.93f, 0.95f, 0.98f);
            else
            {
                float t = Mathf.Clamp01(h / 120f);
                c = t < 0.5f ? Color.Lerp(Low, Mid, t * 2f) : Color.Lerp(Mid, High, (t - 0.5f) * 2f);
                if (EarthData.BiomeAt(lat, lon) == EarthData.Biome.Forest) c = Color.Lerp(c, new Color(0.2f, 0.4f, 0.2f), 0.5f);
            }
            // Hill shading: light from the north-west.
            float s = Mathf.Max(1f, mpp);
            float slope = (_planet.Height(DirAt(ex - s, nz + s)) - _planet.Height(DirAt(ex + s, nz - s))) / (2f * s);
            float k = Mathf.Clamp(1f + slope * 1.6f, 0.6f, 1.3f);
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        Color FarmColour(Vector3 d)
        {
            // The farm, drawn from the same rectangles the level is built from (farm frame: x east, z north).
            var f = _planet.Farm;
            var local = _planet.transform.InverseTransformPoint(f.position);
            var off = (d * Planet.Radius - local.normalized * Planet.Radius);
            Planet.TangentAt(_planet.FarmDir, out var e, out var n);
            float x = Vector3.Dot(off, e), z = Vector3.Dot(off, n);
            var p = new Vector2(x, z);
            if (GreyboxLevel.PondRect.Contains(p)) return Shallow;
            if (GreyboxLevel.PuddleRect.Contains(p)) return Shallow;
            if (new Rect(5.5f, 0.5f, 4f, 4f).Contains(p)) return new Color(0.45f, 0.45f, 0.47f);        // rocket pad
            if (new Rect(-2f, -4f, 9f, 9f).Contains(p)) return new Color(0.78f, 0.77f, 0.74f);          // apron
            if (new Rect(-24.5f, 8.5f, 7f, 9f).Contains(p)) return new Color(0.6f, 0.18f, 0.14f);       // barn (approx.)
            if ((p - new Vector2(-14.5f, 17f)).magnitude < 1.5f) return new Color(0.6f, 0.6f, 0.62f);   // silo
            float fence = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (Mathf.Abs(fence - 28f) < 0.6f) return new Color(0.4f, 0.3f, 0.2f);                      // fence line
            if (fence < Planet.FarmHalf) return new Color(0.66f, 0.6f, 0.4f);                           // farm ground
            return new Color(0, 0, 0, 0);
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (_sim == null || _sim.Run != RunState.Running || _span <= 0f) return;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
                _label.normal.textColor = Color.white;
                _title = new GUIStyle(_label) { fontSize = 18, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(_label) { fontSize = 12, fontStyle = FontStyle.Italic };
                _small.normal.textColor = new Color(1f, 1f, 0.9f);
            }
            if (_open) FullMap();
            else Minimap();
        }

        void Minimap()
        {
            const float size = 180f;
            var r = new Rect(Screen.width - size - 14f, 64f, size, size);
            GUI.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), GUIContent.none);
            DrawArea(r, MiniRange);
            GUI.Label(new Rect(r.x, r.y + 2, r.width, 18), "N", _label);
            GUI.Label(new Rect(r.x, r.yMax + 2, r.width, 18), "M: map", _label);
        }

        void FullMap()
        {
            float size = Mathf.Min(Screen.width, Screen.height) - 120f;
            var r = new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f + 10f, size, size);
            if (Zooms[_zoom] <= 0f) { float w = Mathf.Min(Screen.width - 80f, size * 2f); r = new Rect((Screen.width - w) / 2f, (Screen.height - w / 2f) / 2f + 10f, w, w / 2f); }
            GUI.Box(new Rect(r.x - 6, r.y - 36, r.width + 12, r.height + 70), GUIContent.none);
            bool world = Zooms[_zoom] <= 0f;
            if (world) WorldMap(ref r);
            else DrawArea(r, Zooms[_zoom]);
            GUI.Label(new Rect(r.x, r.y - 32, r.width, 26), world ? "EARTH" : "MAP", _title);
            if (!world) GUI.Label(new Rect(r.x, r.y + 2, r.width, 18), "N", _label);
            EarthData.LatLon(PlayerDir, out float lat, out float lon);
            float km = Mathf.Acos(Mathf.Clamp(Vector3.Dot(PlayerDir, _planet.FarmDir), -1f, 1f)) * Planet.Radius / 1000f;
            string where = $"{Mathf.Abs(lat):0.00}°{(lat >= 0 ? "N" : "S")} {Mathf.Abs(lon):0.00}°{(lon >= 0 ? "E" : "W")}   ·   {km:0.0} km from the farm";
            string scale = world ? "whole Earth" : Zooms[_zoom] >= 1000f ? $"{Zooms[_zoom] / 1000f:0.#} km across" : $"{Zooms[_zoom]:0} m across";
            GUI.Label(new Rect(r.x - 100, r.yMax + 4, r.width + 200, 22), $"{where}   ·   {scale}   ·   +/- zoom   ·   M close", _label);
            if (CityReplica.Count > 0)
            {
                string next = _visit >= CityReplica.Count ? "the farm" : CityReplica.Title(_visit);
                string here = _visit == 0 ? "" : $"{CityReplica.Title(_visit - 1)}   ·   ";
                GUI.Label(new Rect(r.x - 100, r.y - 54, r.width + 200, 20), $"{here}T: jump to {next}   ·   Shift+T: back", _small);
            }
        }

        /// <summary>The whole Earth (equirectangular) with the farm and the player.</summary>
        void WorldMap(ref Rect r)
        {
            if (_world != null) GUI.DrawTexture(r, _world);
            EarthData.LatLon(_planet.FarmDir, out float flat, out float flon);
            var fp = new Vector2(r.x + (flon + 180f) / 360f * r.width, r.y + (90f - flat) / 180f * r.height);
            GUI.DrawTexture(new Rect(fp.x - 6, fp.y - 6, 12, 12), _dot);
            EarthData.LatLon(PlayerDir, out float lat, out float lon);
            var pp = new Vector2(r.x + (lon + 180f) / 360f * r.width, r.y + (90f - lat) / 180f * r.height);
            // The famous places: gold dots, named once you have found them.
            for (int i = 0; i < Landmarks.All.Count; i++)
            {
                var l = Landmarks.All[i];
                var lp = new Vector2(r.x + (l.Lon + 180f) / 360f * r.width, r.y + (90f - l.Lat) / 180f * r.height);
                bool found = LandmarkView.Found.Contains(i);
                Star(lp, found ? 10f : 7f);
                if (found) GUI.Label(new Rect(lp.x - 70, lp.y + 4, 140, 18), l.Name, _small);
            }
            DrawArrow(pp);
            GUI.Label(new Rect(r.x, r.y + 4, r.width, 18), $"{LandmarkView.Found.Count} of {Landmarks.All.Count} wonders of the world found", _label);
        }

        void Star(Vector2 c, float size)
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.2f);
            GUI.DrawTexture(new Rect(c.x - size / 2f, c.y - size / 2f, size, size), _star);
            GUI.color = old;
        }

        /// <summary>Player heading in degrees clockwise from north, on the planet.</summary>
        float Heading()
        {
            var dir = PlayerDir;
            Planet.TangentAt(dir, out var e, out var n);
            var f = _planet.transform.InverseTransformDirection(_player.forward);
            return Mathf.Atan2(Vector3.Dot(f, e), Vector3.Dot(f, n)) * Mathf.Rad2Deg;
        }

        void DrawArrow(Vector2 c)
        {
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(Heading(), c);
            GUI.DrawTexture(new Rect(c.x - 10, c.y - 10, 20, 20), _arrow);
            GUI.matrix = old;
        }

        /// <summary>Draws <paramref name="metres"/> of map, centred on the player, into screen rect r.</summary>
        void DrawArea(Rect r, float metres)
        {
            var here = Offset(PlayerDir, _dir, _east, _north);
            // Texture coordinates of the window (v runs north, GUI y runs down: DrawTextureWithTexCoords handles it).
            float u0 = 0.5f + (here.x - metres / 2f) / _span;
            float v0 = 0.5f + (here.y - metres / 2f) / _span;
            float w = metres / _span;
            GUI.DrawTextureWithTexCoords(r, _tex, new Rect(u0, v0, w, w));

            // The farm marker, if it is in view.
            var farm = Offset(_planet.FarmDir, _dir, _east, _north);
            var home = WorldToScreen(farm, here, r, metres);
            if (r.Contains(home))
            {
                GUI.DrawTexture(new Rect(home.x - 6, home.y - 6, 12, 12), _dot);
                if (metres > MiniRange) GUI.Label(new Rect(home.x - 40, home.y + 6, 80, 18), "Farm", _label);
            }

            // Famous places and town names in view.
            var pd = PlayerDir;
            float cosView = Mathf.Cos(metres / Planet.Radius);
            for (int i = 0; i < Landmarks.All.Count; i++)
            {
                var l = Landmarks.All[i];
                if (Vector3.Dot(l.Dir, pd) < cosView) continue;
                var s = WorldToScreen(Offset(l.Dir, _dir, _east, _north), here, r, metres);
                if (!r.Contains(s)) continue;
                Star(s, 14f);
                GUI.Label(new Rect(s.x - 80, s.y + 6, 160, 18), l.Name, _label);
            }
            if (metres > MiniRange)
            {
                float minPop = metres >= 3000f ? 300000f : metres >= 1500f ? 50000f : 0f;
                var all = Places.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p.Radius <= 0f || p.Pop < minPop || Vector3.Dot(p.Dir, pd) < cosView) continue; // radius 0: swallowed by a rebuilt city
                    var s = WorldToScreen(Offset(p.Dir, _dir, _east, _north), here, r, metres);
                    if (!r.Contains(s) || (s - home).magnitude < 20f) continue;
                    GUI.Label(new Rect(s.x - 70, s.y - 9, 140, 18), p.Name, _small);
                }
            }

            // The rebuilt cities, each named once at its middle (the towns it covers are not named).
            for (int i = 0; i < CityReplica.Count; i++)
            {
                var cd = CityReplica.Centre(i);
                if (Vector3.Dot(cd, pd) < cosView) continue;
                var s = WorldToScreen(Offset(cd, _dir, _east, _north), here, r, metres);
                if (r.Contains(s)) GUI.Label(new Rect(s.x - 90, s.y - 10, 180, 20), CityReplica.Title(i), _label);
            }

            // The player arrow, turned to the facing direction.
            DrawArrow(r.center);
        }

        static Vector2 WorldToScreen(Vector2 world, Vector2 here, Rect r, float metres)
        {
            float px = (world.x - here.x) / metres * r.width;
            float py = (world.y - here.y) / metres * r.height;
            return new Vector2(r.center.x + px, r.center.y - py);
        }

        static Texture2D Arrow(int n)
        {
            // A white arrowhead with a dark outline, pointing up (north) before rotation.
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fy = 1f - y / (float)(n - 1);           // 0 at the tip (top of the GUI rect), 1 at the base
                float half = fy * 0.5f;
                float dx = Mathf.Abs(x / (float)(n - 1) - 0.5f);
                bool inside = fy > 0.05f && dx < half * 0.9f && fy < 0.95f;
                bool edge = !inside && fy > 0f && dx < half * 0.9f + 0.06f && fy < 1f;
                px[y * n + x] = inside ? new Color(1f, 0.45f, 0.05f, 1f) : edge ? new Color(0.1f, 0.1f, 0.1f, 1f) : new Color(0, 0, 0, 0);
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        static Texture2D Dot(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - (n - 1) / 2f, y - (n - 1) / 2f).magnitude / (n / 2f);
                px[y * n + x] = d < 0.6f ? new Color(0.1f, 0.62f, 0.6f, 1f) : d < 0.95f ? Color.white : new Color(0, 0, 0, 0);
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        static Texture2D StarTex(int n)
        {
            // A white five-pointed star with a dark edge (tinted gold when drawn).
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var v = new Vector2(x - (n - 1) / 2f, y - (n - 1) / 2f) / (n / 2f);
                float a = Mathf.Atan2(v.y, v.x) + Mathf.PI / 2f;
                float k = Mathf.Repeat(a / (Mathf.PI * 2f / 5f), 1f);
                float edge = Mathf.Lerp(0.45f, 1f, Mathf.Abs(k - 0.5f) * 2f);
                float d = v.magnitude;
                px[y * n + x] = d < edge * 0.85f ? Color.white : d < edge ? new Color(0.15f, 0.1f, 0f, 1f) : new Color(0, 0, 0, 0);
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        void OnDestroy()
        {
            if (_star != null) Destroy(_star);
            if (_tex != null) Destroy(_tex);
            if (_arrow != null) Destroy(_arrow);
            if (_dot != null) Destroy(_dot);
        }
    }
}
