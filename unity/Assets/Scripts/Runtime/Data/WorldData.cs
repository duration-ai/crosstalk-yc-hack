using System;

namespace Dogpatch.Data
{
    // Mirrors tools/DataPrep/Models.cs. Names are camelCase to match dogpatch.json; JsonUtility silently drops mismatches,
    // so keep names and types identical. Optional numbers carry a *Known flag instead of null.
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
        public double south;
        public double north;
        public double west;
        public double east;
        public string attribution;
        public Road[] roads;
        public Polyline[] rails;
        public Building[] buildings;
        public Polygon[] parks;
        public Polygon[] land;
        public float[] trees;
        // The world's outline when it isn't the minX..maxZ box (DataPrep --soma): x,z pairs, counter-clockwise.
        public float[] edge;
        public TreeRecord[] treeRecords;
        public Prop[] props;
        public Landmark[] landmarks;
        public Bridge[] bridges;
        public Viaduct[] viaducts;
        public Platform[] platforms;
        public Polygon[] water;
        public Polyline[] coastline;
    }

    [Serializable]
    public class Road
    {
        public string name;
        public string kind;
        public float width;
        public bool oneway;
        public float[] pts;
        public long osmId;
        public int lanes;
        public int lanesForward;
        public int lanesBackward;
        public string turnLanes;
        public string turnLanesForward;
        public string turnLanesBackward;
        public string maxspeed;
        public bool bridge;
        public bool tunnel;
        public int layer;
        public string surface;
        public string sidewalk;
        public string cycleway;
        public int[] markIdx;
        public string[] markKind;
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
        public string type;
        public string typeRule;
        public string use;
        public string useCode;
        public string useDef;
        public int yearBuilt;
        public float stories;
        public int units;
        public string parcel;
        public string zoning;
        public string street;
        public string landUseRaw;
        public float areaM2;
        public string osmName;
        public long osmId;
        public string[] osmTags;
        public bool cottage;
    }

    [Serializable]
    public class Polygon
    {
        public string kind;
        public float[] pts;
    }

    [Serializable]
    public class TreeRecord
    {
        public string id;
        public float x;
        public float z;
        public string species;
        public string common;
        public string latin;
        public float dbh;
        public bool dbhKnown;
        public string plotSize;
        public string source;
        public string[] tags;
    }

    [Serializable]
    public class Prop
    {
        public string id;
        public string altId;
        public string kind;
        public string name;
        public float x;
        public float z;
        public float heading;
        public bool headingKnown;
        public string headingSource;
        public string source;
        public float distToRoad;
        public string nearestRoad;
        public string[] tags;
    }

    [Serializable]
    public class Landmark
    {
        public string kind;
        public string name;
        public float x;
        public float z;
        public double lat;
        public double lon;
        public float heading;
        public bool headingKnown;
        public string footprintId;
        public string[] footprintIds;
        public string osmId;
        public float heightM;
        public bool heightKnown;
        public float[] pts;
        public bool closed;
        public string notes;
    }

    [Serializable]
    public class Bridge
    {
        public long osmId;
        public string kind;
        public string name;
        public int layer;
        public float width;
        public bool closed;
        public string[] tags;
        public float[] pts;
    }

    [Serializable]
    public class Viaduct
    {
        public long osmId;
        public string name;
        public string kind;
        public int lanes;
        public float width;
        public int layer;
        public bool bridge;
        public bool oneway;
        public string maxspeed;
        public string @ref;
        public float[] pts;
    }

    [Serializable]
    public class Platform
    {
        public long osmId;
        public string name;
        public string @operator;
        public string network;
        public string kind;
        public bool closed;
        public string[] tags;
        public float[] pts;
    }
}
