using UnityEngine;

namespace RosDen.Wardrobe {
    /// <summary>
    /// The file a generated mesh is kept in (Ros' Wardrobe's clearance pass): binary even in a project that keeps its
    /// assets as text, so a garment's mesh with every shape key stays small and quick to write. The mesh is in it.
    /// </summary>
    [PreferBinarySerialization]
    public class WardrobeMeshStore : ScriptableObject {
        public Mesh mesh;
    }
}
