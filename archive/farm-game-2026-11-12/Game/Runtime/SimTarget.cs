using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>What a collider stands for in the simulation, so the interaction ray can build a request.</summary>
    public enum TargetKind
    {
        None,
        Water,        // Id = source id
        Entity,       // Id = entity id (crate, tank, pump, kit, fitting)
        Socket,       // Id = node id, Socket = intake/outlet/inlet/link, Index
        Handle,       // Id = pump id (hand pump crank)
        MotorLever,   // Id = pump id
        HoseEnd,      // Id = hose id, Index = end
        CableEnd,     // Id = pump id
        PostSocket,   // Index = post socket
        LaunchLever,
        OrderLever,
        StagingSpot,
    }

    /// <summary>Tags a collider with its simulation meaning. Presentation only; holds no authoritative state.</summary>
    public sealed class SimTarget : MonoBehaviour
    {
        public TargetKind Kind;
        public int Id;
        public string Socket = "";
        public int Index;

        public static SimTarget Add(GameObject g, TargetKind kind, int id = 0, string socket = "", int index = 0)
        {
            var t = g.GetComponent<SimTarget>();
            if (t == null) t = g.AddComponent<SimTarget>();
            t.Kind = kind; t.Id = id; t.Socket = socket ?? ""; t.Index = index;
            return t;
        }
    }
}
