using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Home
{
    /// <summary>
    /// Codec-call screen over a portrait: fine scanlines, a bright band that rolls down, a dark vignette edge, and a
    /// short glitch burst (<see cref="Burst"/>: offset bars and noise) when the portrait changes. Tinted with
    /// <see cref="tint"/> (the pilot's accent). Put it on top of the portrait; nothing is picked.
    /// </summary>
    [UxmlElement]
    public partial class CodecScan : VisualElement
    {
        private const float BurstSeconds = 0.35f;

        [UxmlAttribute, CreateProperty]
        public Color tint { get; set; } = new(0f, 1f, 0.58f);

        private float _burstUntil;
        private int _burstSeed;
        private readonly IVisualElementScheduledItem _tick;

        public CodecScan()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            _tick = schedule.Execute(MarkDirtyRepaint).Every(33);
            RegisterCallback<DetachFromPanelEvent>(_ => _tick.Pause());
            RegisterCallback<AttachToPanelEvent>(_ => _tick.Resume());
        }

        /// <summary>Starts a glitch burst (portrait changed).</summary>
        public void Burst()
        {
            _burstUntil = Time.unscaledTime + BurstSeconds;
            _burstSeed++;
        }

        private readonly System.Collections.Generic.List<(Rect Rect, Color Colour)> _quads = new();

        // Plain quads, not Painter2D: a large painter fill can tessellate to half a rectangle (a diagonal wedge).
        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var w = rect.width;
            var h = rect.height;
            if (w < 2f || h < 2f) return;
            var time = Time.unscaledTime;
            _quads.Clear();

            // Tint wash.
            _quads.Add((new Rect(0f, 0f, w, h), new Color(tint.r, tint.g, tint.b, 0.08f)));

            // Scanlines every 3 px.
            var line = new Color(0f, 0f, 0f, 0.32f);
            for (var y = 0f; y < h; y += 3f) _quads.Add((new Rect(0f, y, w, 1f), line));

            // Rolling band.
            var band = Mathf.Repeat(time * 0.35f, 1.4f) * h - h * 0.2f;
            for (var i = 0; i < 8; i++)
                _quads.Add((new Rect(0f, band + i * 4f, w, 4f), new Color(tint.r, tint.g, tint.b, 0.025f * (8 - i))));

            // Edge shade (a cheap vignette: four soft bars per step).
            for (var i = 0; i < 5; i++)
            {
                var shade = new Color(0f, 0f, 0f, 0.09f * (5 - i));
                var d = i * 4f;
                _quads.Add((new Rect(d, d, w - 2f * d, 4f), shade));
                _quads.Add((new Rect(d, h - d - 4f, w - 2f * d, 4f), shade));
                _quads.Add((new Rect(d, d + 4f, 4f, h - 2f * d - 8f), shade));
                _quads.Add((new Rect(w - d - 4f, d + 4f, 4f, h - 2f * d - 8f), shade));
            }

            if (time < _burstUntil)
            {
                // Glitch burst: displaced bright bars, fading out.
                var strength = (_burstUntil - time) / BurstSeconds;
                var frame = Mathf.FloorToInt(time * 30f);
                for (var i = 0; i < 9; i++)
                {
                    var r = Rand(_burstSeed, frame, i);
                    var bar = 2f + 10f * Rand(_burstSeed, frame, i + 40);
                    var shift = (Rand(_burstSeed, frame, i + 80) - 0.5f) * w * 0.3f;
                    _quads.Add((new Rect(Mathf.Max(0f, shift), r * h, w * (0.4f + 0.6f * r), bar),
                        new Color(tint.r, tint.g, tint.b, 0.45f * strength)));
                }
            }

            // Cut to the element: a parent may not clip (a render texture portrait does not show under overflow: hidden).
            for (var i = _quads.Count - 1; i >= 0; i--)
            {
                var (r, c) = _quads[i];
                var xMin = Mathf.Max(0f, r.xMin);
                var yMin = Mathf.Max(0f, r.yMin);
                var xMax = Mathf.Min(w, r.xMax);
                var yMax = Mathf.Min(h, r.yMax);
                if (xMax <= xMin || yMax <= yMin) _quads.RemoveAt(i);
                else _quads[i] = (Rect.MinMaxRect(xMin, yMin, xMax, yMax), c);
            }
            Emit(context, _quads);
        }

        private static void Emit(MeshGenerationContext context, System.Collections.Generic.List<(Rect Rect, Color Colour)> quads)
        {
            if (quads.Count == 0) return;
            var mesh = context.Allocate(quads.Count * 4, quads.Count * 6);
            for (var i = 0; i < quads.Count; i++)
            {
                var (r, c) = quads[i];
                Color32 colour = c;
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = colour });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = colour });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = colour });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = colour });
                var b = (ushort)(i * 4);
                mesh.SetNextIndex(b);
                mesh.SetNextIndex((ushort)(b + 1));
                mesh.SetNextIndex((ushort)(b + 2));
                mesh.SetNextIndex(b);
                mesh.SetNextIndex((ushort)(b + 2));
                mesh.SetNextIndex((ushort)(b + 3));
            }
        }
        private static float Rand(int a, int b, int c)
        {
            unchecked
            {
                var x = (uint)(a * 374761393 + b * 668265263 + c * 2246822519u);
                x = (x ^ (x >> 13)) * 1274126177u;
                return ((x ^ (x >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
