using System;

namespace Dogpatch.Data
{
    [Serializable]
    public class WorldData
    {
        public string name;
        public double originLat;
        public double originLon;
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;
        public string attribution;
        public Road[] roads;
        public Polyline[] rails;
        public Building[] buildings;
        public Polygon[] parks;
        public Polygon[] land;
        public float[] trees;
    }

    [Serializable]
    public class Road
    {
        public string name;
        public string kind;
        public float width;
        public bool oneway;
        public float[] pts;
    }

    [Serializable]
    public class Polyline
    {
        public string kind;
        public float[] pts;
    }

    [Serializable]
    public class Building
    {
        public string id;
        public float height;
        public string heightSource;
        public string kind;
        public float[] pts;
    }

    [Serializable]
    public class Polygon
    {
        public string kind;
        public float[] pts;
    }
}
