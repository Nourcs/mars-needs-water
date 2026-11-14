# Mini Earth

A 1:5 replica of the real Earth, streamed live from Overture Maps release 2026-10-21.0.
Press Play in any scene; `EarthWorld` sets everything up.

- `Core/` has no Unity code. It reads Overture's PMTiles archives over the internet (only the squares near the
  player), decodes them, and builds the ground, buildings, roads, rail and trees for one square at a time.
- `Runtime/` shows those squares in Unity, moves the player (walk, car, fly) and draws the on-screen text.
- `Resources/Earth/places.txt` is the list of 174,401 towns and cities for "go to a place" (G), from Overture divisions.

Scale: 1 game metre = 5 real metres for all distances and for ground elevation. Buildings and trees are kept
taller than that (half height up to 12 m, a quarter above), and roads 1.5 times wider, so a person can walk among them.
Change `Geo.Height` and `TileBuilder.RoadScale` to alter this.

Downloaded squares are cached in the game's persistent data folder, so a place visited once loads from disk next time.

Credits owed in the game: Map data (c) OpenStreetMap contributors, Overture Maps Foundation (ODbL).
Elevation: Terrain Tiles (Mapzen), hosted by AWS Open Data; see https://github.com/tilezen/joerd/blob/master/docs/attribution.md
