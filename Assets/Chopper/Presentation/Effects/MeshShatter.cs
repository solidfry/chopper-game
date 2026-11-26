using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Cuts a mesh into chunks for the wreck break-up (<see cref="WreckDebris"/>): each triangle goes to the nearest of
    /// a few seed points (Voronoi cells), so the edges are jagged like torn metal. Seeds are fixed per mesh (same break
    /// every time, cached). Keeps every sub-mesh, so each chunk takes the same materials. The mesh must be readable
    /// (Read/Write on); otherwise there is no cut and the caller flies the whole mesh.
    /// </summary>
    public sealed class MeshShatter
    {
        // Keyed by the mesh's content, not the object: in a build the subscene's hull mesh can be a different object
        // from the prefab's, and the cut is made ahead (Prewarm, at scene load) from the prefab.
        private readonly Dictionary<(string, int, Vector3, int), (Mesh Mesh, Vector3 Centre)[]> _cache = new();

        private static (string, int, Vector3, int) Key(Mesh mesh, int chunks) => (mesh.name, mesh.vertexCount, mesh.bounds.size, chunks);

        public (Mesh Mesh, Vector3 Centre)[] Cut(Mesh mesh, int chunks)
        {
            if (mesh == null || !mesh.isReadable || chunks < 2) return null;
            if (_cache.TryGetValue(Key(mesh, chunks), out var cached)) return cached;

            var positions = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv = mesh.uv;
            var uv2 = mesh.uv2;
            var colours = mesh.colors;
            var seeds = Seeds(mesh.bounds, chunks, mesh.name.GetHashCode());

            var builders = new Builder[chunks];
            for (var c = 0; c < chunks; c++) builders[c] = new Builder(mesh.subMeshCount);
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                for (var t = 0; t + 2 < triangles.Length; t += 3)
                {
                    var centre = (positions[triangles[t]] + positions[triangles[t + 1]] + positions[triangles[t + 2]]) / 3f;
                    var cell = Nearest(seeds, centre);
                    for (var k = 0; k < 3; k++) builders[cell].Add(sub, triangles[t + k]);
                }
            }

            var result = new List<(Mesh, Vector3)>(chunks);
            foreach (var builder in builders)
            {
                if (builder.Map.Count < 3) continue;
                var part = new Mesh { name = mesh.name + " chunk" };
                if (builder.Map.Count > 65000) part.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                // Each chunk is centred on itself, so it spins round its own middle; Centre puts it back in place.
                var vertices = Pick(positions, builder.Source);
                var box = new Bounds(vertices[0], Vector3.zero);
                foreach (var vertex in vertices) box.Encapsulate(vertex);
                for (var v = 0; v < vertices.Count; v++) vertices[v] -= box.center;
                part.SetVertices(vertices);
                if (normals.Length == positions.Length) part.SetNormals(Pick(normals, builder.Source));
                if (tangents.Length == positions.Length) part.SetTangents(Pick(tangents, builder.Source));
                if (uv.Length == positions.Length) part.SetUVs(0, Pick(uv, builder.Source));
                if (uv2.Length == positions.Length) part.SetUVs(1, Pick(uv2, builder.Source));
                if (colours.Length == positions.Length) part.SetColors(Pick(colours, builder.Source));
                part.subMeshCount = mesh.subMeshCount;
                for (var sub = 0; sub < mesh.subMeshCount; sub++) part.SetTriangles(builder.Triangles[sub], sub);
                part.RecalculateBounds();
                result.Add((part, box.center));
            }
            var array = result.ToArray();
            _cache[Key(mesh, chunks)] = array;
            return array;
        }

        /// <summary>Seed points spread through the bounds: one per slice along the length, jittered across.</summary>
        public static Vector3[] Seeds(Bounds bounds, int count, int seed)
        {
            var random = new System.Random(seed);
            var seeds = new Vector3[count];
            var min = bounds.min;
            var size = bounds.size;
            // The longest axis is cut into slices, so nose, cabin, boom and tail come apart.
            var axis = size.z >= size.x && size.z >= size.y ? 2 : size.x >= size.y ? 0 : 1;
            for (var i = 0; i < count; i++)
            {
                var p = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                p[axis] = (i + 0.2f + 0.6f * (float)random.NextDouble()) / count;
                seeds[i] = min + Vector3.Scale(p, size);
            }
            return seeds;
        }

        public static int Nearest(Vector3[] seeds, Vector3 point)
        {
            var best = 0;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < seeds.Length; i++)
            {
                var distance = (seeds[i] - point).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        public void Clear()
        {
            foreach (var parts in _cache.Values)
            foreach (var (part, _) in parts)
                Object.Destroy(part);
            _cache.Clear();
        }

        private static List<T> Pick<T>(T[] source, List<int> indices)
        {
            var list = new List<T>(indices.Count);
            foreach (var index in indices) list.Add(source[index]);
            return list;
        }

        private sealed class Builder
        {
            public readonly Dictionary<int, int> Map = new();
            public readonly List<int> Source = new();
            public readonly List<int>[] Triangles;

            public Builder(int subMeshes)
            {
                Triangles = new List<int>[subMeshes];
                for (var i = 0; i < subMeshes; i++) Triangles[i] = new List<int>();
            }

            public void Add(int sub, int vertex)
            {
                if (!Map.TryGetValue(vertex, out var local))
                {
                    local = Source.Count;
                    Map[vertex] = local;
                    Source.Add(vertex);
                }
                Triangles[sub].Add(local);
            }
        }
    }
}
