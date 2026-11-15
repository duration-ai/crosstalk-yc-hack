using System;

namespace Dogpatch.Data
{
    // Mirrors tools/DataPrep/Freeway.cs (freeway.json, written by DataPrep --freeway). Every motorway and
    // motorway_link way in the bbox with its OSM node ids; clip points at the bbox edge have node id 0.
    [Serializable]
    public class FreewayData
    {
        public string source;
        public string attribution;
        public FreewayWay[] ways;
    }

    [Serializable]
    public class FreewayWay
    {
        public long osmId;
        public string name;
        public string @ref;
        public string kind;
        public int lanes;
        public bool oneway;
        public string maxspeed;
        public int layer;
        public bool bridge;
        public string destination;
        public long[] nodes;
        public float[] pts;
    }
}
