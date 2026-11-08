using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// The navigation map of one level: a 3D grid over the arena bounds. Each cell keeps its clearance: 0 = solid (a
    /// collider within half a cell diagonal, or air that cannot be reached from the sky, e.g. inside a building), else the
    /// distance in cells to the nearest solid cell (1 = next to a wall). Air units use every free cell; ground units the
    /// floor cells (free, solid below). Baked at map load by <see cref="NavGridBakeSystem"/>; read-only after that.
    /// </summary>
    public struct NavGridBlob
    {
        /// <summary>Corner of cell (0, 0, 0) (its minimum, not its centre).</summary>
        public float3 Origin;
        public float CellSize;
        public int3 Size;
        public BlobArray<byte> Clearance;
        /// <summary>
        /// Street level per column (x + Size.x * z): the lowest static surface within two columns, so a roof does not
        /// count as the ground. Paths prefer to stay near it (low flight between buildings).
        /// </summary>
        public BlobArray<float> Floor;

        public int Count => Size.x * Size.y * Size.z;

        public bool Contains(int3 cell) => math.all(cell >= 0) && math.all(cell < Size);

        public int Index(int3 cell) => cell.x + Size.x * (cell.z + Size.z * cell.y);

        public int3 CellOf(int index)
        {
            var layer = Size.x * Size.z;
            var y = index / layer;
            var rest = index - y * layer;
            var z = rest / Size.x;
            return new int3(rest - z * Size.x, y, z);
        }

        public int3 CellAt(float3 position) => (int3)math.floor((position - Origin) / CellSize);

        public float3 Centre(int3 cell) => Origin + ((float3)cell + 0.5f) * CellSize;

        /// <summary>Street level under a position (clamped to the grid).</summary>
        public float FloorAt(float3 position)
        {
            var cell = math.clamp(CellAt(position), 0, Size - 1);
            return Floor[cell.x + Size.x * cell.z];
        }

        /// <summary>Clearance in cells at a cell; outside the grid counts as solid.</summary>
        public byte ClearanceAt(int3 cell) => Contains(cell) ? Clearance[Index(cell)] : (byte)0;

        public byte ClearanceAt(float3 position) => ClearanceAt(CellAt(position));

        /// <summary>A free cell with solid directly below (ground units drive on it).</summary>
        public bool IsFloor(int3 cell) => ClearanceAt(cell) > 0 && (cell.y == 0 || ClearanceAt(cell - new int3(0, 1, 0)) == 0);

        /// <summary>
        /// True when every point on the segment (sampled each half cell) has at least <paramref name="minClearance"/>.
        /// </summary>
        public bool LineClear(float3 from, float3 to, byte minClearance)
        {
            var offset = to - from;
            var length = math.length(offset);
            var steps = (int)math.ceil(length / (CellSize * 0.5f));
            for (var i = 0; i <= steps; i++)
            {
                var point = from + offset * (steps == 0 ? 0f : (float)i / steps);
                if (ClearanceAt(point) < minClearance) return false;
            }
            return true;
        }
    }

    /// <summary>The level's navigation grid (singleton). The blob belongs to <see cref="NavGridBakeSystem"/>.</summary>
    public struct NavGrid : IComponentData
    {
        public BlobAssetReference<NavGridBlob> Blob;
    }
}
