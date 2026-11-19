using System.Collections.Generic;
using Unity.Mathematics;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Where the thicker fog sits over a lava line: one point every <c>spacing</c> metres along the polyline, the first
    /// at its start and the last at its end. Pure, so it is tested without Buto (the masks themselves:
    /// <c>ButoFogSystem</c>).
    /// </summary>
    public static class LavaFogPlacement
    {
        public static void Points(IReadOnlyList<float3> line, float spacing, List<float3> into)
        {
            if (line == null || line.Count == 0) return;
            into.Add(line[0]);
            if (line.Count == 1) return;
            spacing = math.max(1f, spacing);
            var carried = 0f;
            for (var i = 1; i < line.Count; i++)
            {
                var from = line[i - 1];
                var length = math.distance(from, line[i]);
                if (length <= 1e-4f) continue;
                var dir = (line[i] - from) / length;
                var at = spacing - carried;
                while (at <= length)
                {
                    into.Add(from + dir * at);
                    at += spacing;
                }
                carried = length - (at - spacing);
            }
            // Always end on the end point (unless the last step already sits there).
            var end = line[line.Count - 1];
            if (math.distance(into[into.Count - 1], end) > spacing * 0.25f) into.Add(end);
        }
    }
}
