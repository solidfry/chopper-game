using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Hangar
{
    /// <summary>
    /// Feature lines of a mesh for the hangar's wireframe look: the edges where two faces meet at more than an angle
    /// (creases), plus open edges. Flat-shaded meshes split their vertices per face, so vertices are joined by position
    /// first. Each line is pushed out a little along its faces' normals, so it draws over the solid fill without
    /// flicker. Pure; the editor bakes the results into a <see cref="WireframeLibrary"/> (kit meshes are not readable
    /// in a build).
    /// </summary>
    public static class WireframeMesh
    {
        /// <param name="creaseDegrees">Faces that meet at a bigger angle get a line.</param>
        /// <param name="push">Metres the lines move off the surface.</param>
        public static (Vector3[] Points, int[] Lines) Build(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles,
            float creaseDegrees, float push)
        {
            // Join split vertices: the same position (to 0.1 mm) is one corner.
            var corner = new int[vertices.Count];
            var corners = new Dictionary<Vector3Int, int>();
            var positions = new List<Vector3>();
            for (var i = 0; i < vertices.Count; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 10000f);
                if (!corners.TryGetValue(key, out var index))
                {
                    index = positions.Count;
                    corners.Add(key, index);
                    positions.Add(vertices[i]);
                }
                corner[i] = index;
            }

            // Each edge: the normals of the first two faces on it.
            var edges = new Dictionary<(int, int), (Vector3 First, Vector3 Second, int Faces)>();
            for (var t = 0; t + 2 < triangles.Count; t += 3)
            {
                var a = corner[triangles[t]];
                var b = corner[triangles[t + 1]];
                var c = corner[triangles[t + 2]];
                if (a == b || b == c || a == c) continue;
                var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).normalized;
                AddFace(edges, a, b, normal);
                AddFace(edges, b, c, normal);
                AddFace(edges, c, a, normal);
            }

            var minDot = Mathf.Cos(creaseDegrees * Mathf.Deg2Rad);
            var points = new List<Vector3>();
            var lines = new List<int>();
            foreach (var ((a, b), (first, second, faces)) in edges)
            {
                if (faces == 2 && Vector3.Dot(first, second) >= minDot) continue;
                var offset = (faces == 2 ? (first + second).normalized : first) * push;
                lines.Add(points.Count);
                points.Add(positions[a] + offset);
                lines.Add(points.Count);
                points.Add(positions[b] + offset);
            }
            return (points.ToArray(), lines.ToArray());
        }

        private static void AddFace(Dictionary<(int, int), (Vector3 First, Vector3 Second, int Faces)> edges, int a, int b, Vector3 normal)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!edges.TryGetValue(key, out var edge)) edges[key] = (normal, Vector3.zero, 1);
            else if (edge.Faces == 1) edges[key] = (edge.First, normal, 2);
            // A third face on one edge (non-manifold): keep the first two.
        }
    }
}
