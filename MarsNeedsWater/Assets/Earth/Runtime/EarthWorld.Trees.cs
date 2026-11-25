namespace MarsNeedsWater.Earth
{
    // Trees in the closest map squares (EarthTrees): handed over as squares are shown and dropped.
    public sealed partial class EarthWorld
    {
        EarthTrees trees;

        void StartTrees()
        {
            if (Voxel.On) return;       // voxel look: trees are blocks, built with each square (Voxel.Trees)
            trees = gameObject.AddComponent<EarthTrees>();
            trees.Init(this);
        }

        void TreesShown(Tile t)
        {
            if (trees != null && t.Data.TreeSpots != null && t.Data.TreeSpots.Count > 0) trees.Add(t.Key, t.Root.transform, t.Data.TreeSpots);
        }

        void TreesGone(long key)
        {
            if (trees != null) trees.Remove(key);
        }
    }
}
