using System;

namespace Dogpatch.Data
{
    [Serializable]
    public class ModelCatalog
    {
        public string source;
        public ModelEntry[] models;
    }

    [Serializable]
    public class ModelEntry
    {
        public string id;
        public string role;
        public string title;
        public float sizeX;
        public float sizeY;
        public float sizeZ;
        public int triangles;
    }
}
