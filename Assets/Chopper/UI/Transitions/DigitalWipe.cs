using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Transitions
{
    /// <summary>
    /// The scene change "digitise" effect: the screen breaks into blocks. A glowing front of green and cyan blocks runs
    /// across the screen (mostly left to right, broken up by noise); behind it the blocks are solid dark. On reveal the
    /// front runs again and the blocks behind it clear. Pink and cyan glitch bars jump while it runs, and faint scan
    /// lines show while fully covered. One mesh per frame (no child elements, no textures).
    /// </summary>
    [UxmlElement]
    public partial class DigitalWipe : VisualElement
    {
        private struct Quad
        {
            public Rect Rect;
            public Color32 Colour;
        }

        private readonly List<Quad> _quads = new();

        /// <summary>0 = clear, 1 = covered.</summary>
        public float Cover { get; private set; }
        public bool Revealing { get; private set; }
        public int Seed { get; private set; }
        public float Time { get; private set; }

        [UxmlAttribute] public int blockSize { get; set; } = 30;
        [UxmlAttribute] public float edgeWidth { get; set; } = 0.14f;
        [UxmlAttribute] public float sweep { get; set; } = 0.45f;
        /// <summary>Degrees: 0 = left to right, 90 = top to bottom.</summary>
        public float sweepAngle { get; set; }
        public float frontFlickerRate { get; set; } = 24f;
        public int glitchBars { get; set; } = 4;
        public float glitchStrength { get; set; } = 0.55f;
        public float scanLines { get; set; } = 0.035f;
        public Color background { get; set; } = new(0.012f, 0.035f, 0.02f, 1f);
        public Color edge { get; set; } = new(0f, 1f, 0.58f, 1f);
        public Color accent { get; set; } = new(0f, 0.94f, 1f, 1f);
        public Color glitch { get; set; } = new(1f, 0.26f, 0.54f, 1f);

        public DigitalWipe()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Set(float cover, bool revealing, int seed, float time)
        {
            Cover = Mathf.Clamp01(cover);
            Revealing = revealing;
            Seed = seed;
            Time = time;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 1f || rect.height < 1f || Cover <= 0f) return;
            _quads.Clear();

            if (Cover >= 0.999f)
            {
                Add(rect, background);
                AddScanLines(rect);
            }
            else
            {
                AddBlocks(rect);
            }
            AddGlitchBars(rect);
            Flush(context);
        }

        /// <summary>
        /// Each block has a threshold (noise, pulled towards its x position by <see cref="sweep"/>). The front moves from
        /// 0 to 1 + edge width; a block is in the front while front - threshold is between 0 and the edge width.
        /// </summary>
        private void AddBlocks(Rect rect)
        {
            var size = Mathf.Max(4f, blockSize);
            var columns = Mathf.CeilToInt(rect.width / size);
            var rows = Mathf.CeilToInt(rect.height / size);
            var width = Mathf.Max(0.01f, edgeWidth);
            var progress = Revealing ? 1f - Cover : Cover;
            var front = progress * (1f + width);
            var flicker = (int)(Time * frontFlickerRate);
            var cos = Mathf.Cos(sweepAngle * Mathf.Deg2Rad);
            var sin = Mathf.Sin(sweepAngle * Mathf.Deg2Rad);
            var reach = Mathf.Max(0.001f, Mathf.Abs(cos) + Mathf.Abs(sin));

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var noise = Hash(column, row, Seed);
                    var x = columns > 1 ? column / (float)(columns - 1) - 0.5f : 0f;
                    var y = rows > 1 ? row / (float)(rows - 1) - 0.5f : 0f;
                    var along = (x * cos + y * sin) / reach + 0.5f;
                    var threshold = Mathf.Lerp(noise, along, sweep);
                    var local = (front - threshold) / width;

                    // Covering: solid behind the front. Revealing: solid ahead of it (not yet cleared).
                    var solid = Revealing ? local <= 0f : local >= 1f;
                    var inFront = local > 0f && local < 1f;
                    var cell = new Rect(rect.x + column * size, rect.y + row * size, size, size);
                    if (solid)
                    {
                        Add(cell, background);
                        continue;
                    }
                    if (!inFront) continue;

                    // The front: dark block with a bright inset that dims as it passes.
                    Add(cell, background);
                    var pick = Hash(column, row, Seed + flicker);
                    var colour = pick > 0.82f ? accent : edge;
                    var glow = (1f - local) * (0.55f + 0.45f * pick);
                    colour.a = Mathf.Clamp01(glow);
                    var inset = size * (pick > 0.5f ? 0.12f : 0.22f);
                    Add(new Rect(cell.x + inset, cell.y + inset, size - 2f * inset, size - 2f * inset), colour);
                }
            }
        }

        /// <summary>Thin lines every 4 px over the covered screen.</summary>
        private void AddScanLines(Rect rect)
        {
            if (scanLines <= 0f) return;
            var colour = edge;
            colour.a = scanLines;
            for (var y = rect.y; y < rect.yMax; y += 4f) Add(new Rect(rect.x, y, rect.width, 1f), colour);
        }

        /// <summary>A few bars that jump about 14 times a second; strongest in the middle of the transition.</summary>
        private void AddGlitchBars(Rect rect)
        {
            var strength = Cover >= 0.999f ? 0.35f : Mathf.Sin(Cover * Mathf.PI);
            if (strength <= 0.05f) return;
            var step = (int)(Time * 14f);
            for (var i = 0; i < glitchBars; i++)
            {
                var y = Hash(i, step, Seed) * rect.height;
                var height = 1f + Hash(i, step + 7, Seed) * 5f;
                var x = Hash(i + 11, step, Seed) * rect.width * 0.6f;
                var length = rect.width * (0.15f + 0.5f * Hash(i + 3, step, Seed));
                var colour = (i & 1) == 0 ? glitch : accent;
                colour.a = glitchStrength * strength;
                Add(new Rect(rect.x + x, rect.y + y, length, height), colour);
            }
        }

        private void Add(Rect rect, Color colour) => _quads.Add(new Quad { Rect = rect, Colour = colour });

        private void Flush(MeshGenerationContext context)
        {
            // UI Toolkit meshes take at most 65535 vertices: draw in chunks.
            const int maxQuads = 16000;
            for (var start = 0; start < _quads.Count; start += maxQuads)
            {
                var count = Mathf.Min(maxQuads, _quads.Count - start);
                var mesh = context.Allocate(count * 4, count * 6);
                for (var i = 0; i < count; i++)
                {
                    var quad = _quads[start + i];
                    var r = quad.Rect;
                    var c = quad.Colour;
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = c });
                    var v = (ushort)(i * 4);
                    // Clockwise (UI Toolkit culls the other winding).
                    mesh.SetNextIndex(v);
                    mesh.SetNextIndex((ushort)(v + 1));
                    mesh.SetNextIndex((ushort)(v + 2));
                    mesh.SetNextIndex(v);
                    mesh.SetNextIndex((ushort)(v + 2));
                    mesh.SetNextIndex((ushort)(v + 3));
                }
            }
        }

        /// <summary>0..1 from three integers (stable per block and seed).</summary>
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
