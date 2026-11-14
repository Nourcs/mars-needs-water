# World audit

Walks every road, railway and river on the planet with the game's own code (no Unity needed) and lists
anything broken: lines below the ground or in the air, hard turns, steps, overlaps, crossings at different
heights, rivers with no water showing, lines across airports or the farm.

Run (needs the .NET 8 SDK), from this folder:

    dotnet build -c Release
    dotnet run -c Release --no-build -- ../../../MarsNeedsWater/Assets/Game report.txt

About 70 seconds. `report.txt` lists each kind of issue with the worst 25 places (latitude, longitude, size).
Look closer at one spot: `dotnet run -c Release --no-build -- ../../../MarsNeedsWater/Assets/Game x probe <lat> <lon>`.

The ground noise here is a stand-in for Unity's, so heights differ by a few centimetres from the game;
the test checks the rules, not the exact numbers.
