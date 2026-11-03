using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Hangar
{
    /// <summary>
    /// Baked wireframe line meshes (<see cref="WireframeMesh"/>) for every chassis and module model, as sub-assets.
    /// Setup step 7 adds the missing ones and bakes again the ones whose model changed (<see cref="Entry.SourceStamp"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Hangar/Wireframe Library", fileName = "WireframeLibrary")]
    public sealed class WireframeLibrary : ScriptableObject, IValidatedAsset
    {
        [Serializable]
        public struct Entry
        {
            public Mesh Source;
            public Mesh Wire;
            /// <summary>
            /// Shape of the source when it was baked (<see cref="Stamp"/>). A kit reimport keeps the same mesh asset with
            /// a new shape: a different stamp makes Setup step 7 bake the lines again.
            /// </summary>
            public int SourceStamp;
        }

        /// <summary>Vertex and triangle counts and the bounds: changes when the model changes.</summary>
        public static int Stamp(Mesh mesh)
        {
            if (mesh == null) return 0;
            var bounds = mesh.bounds;
            return HashCode.Combine(mesh.vertexCount, mesh.triangles.Length,
                Mathf.RoundToInt(bounds.center.x * 1000f), Mathf.RoundToInt(bounds.center.y * 1000f), Mathf.RoundToInt(bounds.center.z * 1000f),
                Mathf.RoundToInt(bounds.size.x * 1000f), Mathf.RoundToInt(bounds.size.y * 1000f), Mathf.RoundToInt(bounds.size.z * 1000f));
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<Mesh, Mesh> _lookup;

        public IReadOnlyList<Entry> Entries => entries;

        public bool TryGet(Mesh source, out Mesh wire)
        {
            wire = null;
            if (source == null) return false;
            if (_lookup == null)
            {
                _lookup = new Dictionary<Mesh, Mesh>();
                foreach (var entry in entries)
                    if (entry.Source != null && entry.Wire != null) _lookup[entry.Source] = entry.Wire;
            }
            return _lookup.TryGetValue(source, out wire);
        }

        public void CollectProblems(List<string> problems)
        {
            foreach (var entry in entries)
                if (entry.Source == null || entry.Wire == null) problems.Add($"{name}: an entry has no source or wire mesh. Press '7. Build hangar'.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetEntries(Entry[] value)
        {
            entries = value;
            _lookup = null;
        }

        private void OnValidate() => _lookup = null;
    }
}
