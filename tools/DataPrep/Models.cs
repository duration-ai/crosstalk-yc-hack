namespace DataPrep;

// JSON shape of unity/Assets/Resources/World/dogpatch.json (camelCase). Mirrored by unity/Assets/Scripts/Runtime/Data/WorldData.cs.
// JsonUtility cannot read null floats or dictionaries, so optional numbers come with a *Known flag and tags are "k=v" strings.
class WorldData
{
    public string Name { get; set; } public double OriginLat { get; set; } public double OriginLon { get; set; }
    public float MinX { get; set; } public float MaxX { get; set; } public float MinZ { get; set; } public float MaxZ { get; set; }
    public double South { get; set; } public double North { get; set; } public double West { get; set; } public double East { get; set; }
    public string Attribution { get; set; }
    public Road[] Roads { get; set; } public Polyline[] Rails { get; set; } public Building[] Buildings { get; set; }
    public Polygon[] Parks { get; set; } public Polygon[] Land { get; set; } public float[] Trees { get; set; }
    public TreeRecord[] TreeRecords { get; set; } public Prop[] Props { get; set; } public Landmark[] Landmarks { get; set; }
    public Bridge[] Bridges { get; set; } public Viaduct[] Viaducts { get; set; } public Platform[] Platforms { get; set; }
    public Polygon[] Water { get; set; } public Polyline[] Coastline { get; set; }
}

class Road
{
    public string Name { get; set; } public string Kind { get; set; } public float Width { get; set; } public bool Oneway { get; set; } public float[] Pts { get; set; }
    // added fields (0 / "" = unknown)
    public long OsmId { get; set; }
    public int Lanes { get; set; } public int LanesForward { get; set; } public int LanesBackward { get; set; }
    public string TurnLanes { get; set; } public string TurnLanesForward { get; set; } public string TurnLanesBackward { get; set; }
    public string Maxspeed { get; set; }
    public bool Bridge { get; set; } public bool Tunnel { get; set; } public int Layer { get; set; }
    public string Surface { get; set; } public string Sidewalk { get; set; } public string Cycleway { get; set; }
    public int[] MarkIdx { get; set; }        // indices into pts (vertex number, not float offset) of tagged nodes on this way
    public string[] MarkKind { get; set; }    // parallel to markIdx: crossing | traffic_signal | stop_sign | give_way | level_crossing
}

class Polyline { public string Kind { get; set; } public float[] Pts { get; set; } }
class Polygon { public string Kind { get; set; } public float[] Pts { get; set; } }

class Building
{
    public string Id { get; set; } public float Height { get; set; } public string HeightSource { get; set; } public string Kind { get; set; } public float[] Pts { get; set; }
    public string Type { get; set; }          // derived, see BuildingTypes.Classify
    public string TypeRule { get; set; }      // which rule fired
    public string Use { get; set; }           // DataSF Land Use category: RESIDENT MIXRES PDR CIE MIPS RETAIL MED VISITOR PARKING OPENSPACE VACANT, or UNKNOWN
    public string UseCode { get; set; }       // Assessor use_code (SRES MRES IND MISC COMR COMM COMO COMH GOVT ...)
    public string UseDef { get; set; }
    public int YearBuilt { get; set; }        // 0 unknown
    public float Stories { get; set; }        // 0 unknown
    public int Units { get; set; }            // 0 unknown
    public string Parcel { get; set; }        // blklot
    public string Zoning { get; set; }
    public string Street { get; set; }        // parcel street name (upper case, no type)
    public string LandUseRaw { get; set; }    // raw land-use inputs: resunits=2;retail=300;...
    public float AreaM2 { get; set; }
    public string OsmName { get; set; }
    public long OsmId { get; set; }
    public string[] OsmTags { get; set; }
    public bool Cottage { get; set; }         // type victorian, <=2 storeys, <=2 units
}

class TreeRecord
{
    public string Id { get; set; } public float X { get; set; } public float Z { get; set; }
    public string Species { get; set; }       // "Common (Latin)"
    public string Common { get; set; } public string Latin { get; set; }
    public float Dbh { get; set; } public bool DbhKnown { get; set; }   // inches
    public string PlotSize { get; set; }
    public string Source { get; set; }        // datasf | osm
    public string[] Tags { get; set; }
}

class Prop
{
    public string Id { get; set; } public string AltId { get; set; } public string Kind { get; set; } public string Name { get; set; }
    public float X { get; set; } public float Z { get; set; }
    public float Heading { get; set; } public bool HeadingKnown { get; set; } public string HeadingSource { get; set; }  // degrees clockwise from north (+z), the way the prop faces
    public string Source { get; set; }        // datasf | osm
    public float DistToRoad { get; set; } public string NearestRoad { get; set; }
    public string[] Tags { get; set; }
}

class Landmark
{
    public string Kind { get; set; } public string Name { get; set; }
    public float X { get; set; } public float Z { get; set; } public double Lat { get; set; } public double Lon { get; set; }
    public float Heading { get; set; } public bool HeadingKnown { get; set; }
    public string FootprintId { get; set; } public string[] FootprintIds { get; set; }
    public string OsmId { get; set; }
    public float HeightM { get; set; } public bool HeightKnown { get; set; }
    public float[] Pts { get; set; } public bool Closed { get; set; }
    public string Notes { get; set; }
}

class Bridge { public long OsmId { get; set; } public string Kind { get; set; } public string Name { get; set; } public int Layer { get; set; } public float Width { get; set; } public bool Closed { get; set; } public string[] Tags { get; set; } public float[] Pts { get; set; } }
class Viaduct
{
    public long OsmId { get; set; } public string Name { get; set; } public string Kind { get; set; } public int Lanes { get; set; } public float Width { get; set; }
    public int Layer { get; set; } public bool Bridge { get; set; } public bool Oneway { get; set; } public string Maxspeed { get; set; } public string Ref { get; set; } public float[] Pts { get; set; }
}
class Platform { public long OsmId { get; set; } public string Name { get; set; } public string Operator { get; set; } public string Network { get; set; } public string Kind { get; set; } public bool Closed { get; set; } public string[] Tags { get; set; } public float[] Pts { get; set; } }

class ModelCatalog { public string Source { get; set; } public ModelEntry[] Models { get; set; } }
class ModelEntry { public string Id { get; set; } public string Role { get; set; } public string Title { get; set; } public float SizeX { get; set; } public float SizeY { get; set; } public float SizeZ { get; set; } public int Triangles { get; set; } }
