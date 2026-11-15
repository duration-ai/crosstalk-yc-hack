using UnityEngine;

namespace Dogpatch.Nav
{
    public enum NavKind { Waypoint, Race, Mission, Ask }

    // Something the direction arrow and the minimap route lead to. Set NavService.Instance.Target
    // to one to override the automatic objective (the race route, else the nearest mission start),
    // and back to null to clear it.
    public interface INavTarget
    {
        Vector3 Position { get; }
        NavKind Kind { get; }
    }

    public sealed class NavPoint : INavTarget
    {
        public Vector3 Position { get; set; }
        public NavKind Kind { get; set; }

        public NavPoint(Vector3 position, NavKind kind = NavKind.Waypoint)
        {
            Position = position;
            Kind = kind;
        }
    }
}
