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

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var w = rect.width;
            var h = rect.height;
            if (w < 2f || h < 2f) return;
            var painter = context.painter2D;
            var time = Time.unscaledTime;

            // Tint wash.
            painter.fillColor = new Color(tint.r, tint.g, tint.b, 0.08f);
            painter.BeginPath();
            AddRect(painter, new Rect(0f, 0f, w, h));
            painter.Fill();

            // Scanlines every 3 px.
            painter.fillColor = new Color(0f, 0f, 0f, 0.32f);
            for (var y = 0f; y < h; y += 3f)
            {
                painter.BeginPath();
                AddRect(painter, new Rect(0f, y, w, 1f));
                painter.Fill();
            }

            // Rolling band.
            var band = Mathf.Repeat(time * 0.35f, 1.4f) * h - h * 0.2f;
            for (var i = 0; i < 8; i++)
            {
                painter.fillColor = new Color(tint.r, tint.g, tint.b, 0.025f * (8 - i));
                painter.BeginPath();
                AddRect(painter, new Rect(0f, band + i * 4f, w, 4f));
                painter.Fill();
            }

            // Edge shade (a cheap vignette: four soft bars).
            for (var i = 0; i < 5; i++)
            {
                var a = 0.09f * (5 - i);
                var d = i * 4f;
                painter.fillColor = new Color(0f, 0f, 0f, a);
                painter.BeginPath();
                AddRect(painter, new Rect(d, d, w - 2f * d, 4f));
                AddRect(painter, new Rect(d, h - d - 4f, w - 2f * d, 4f));
                AddRect(painter, new Rect(d, d + 4f, 4f, h - 2f * d - 8f));
                AddRect(painter, new Rect(w - d - 4f, d + 4f, 4f, h - 2f * d - 8f));
                painter.Fill();
            }

            if (time >= _burstUntil) return;
            // Glitch burst: displaced bright bars and blocks, fading out.
            var strength = (_burstUntil - time) / BurstSeconds;
            var frame = Mathf.FloorToInt(time * 30f);
            for (var i = 0; i < 9; i++)
            {
                var r = Rand(_burstSeed, frame, i);
                var y = r * h;
                var bar = 2f + 10f * Rand(_burstSeed, frame, i + 40);
                var shift = (Rand(_burstSeed, frame, i + 80) - 0.5f) * w * 0.3f;
                painter.fillColor = new Color(tint.r, tint.g, tint.b, 0.45f * strength);
                painter.BeginPath();
                AddRect(painter, new Rect(Mathf.Max(0f, shift), y, w * (0.4f + 0.6f * r), bar));
                painter.Fill();
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

        /// <summary>A closed rectangle subpath (Painter2D has no rectangle call).</summary>
        private static void AddRect(Painter2D painter, Rect r)
        {
            painter.MoveTo(new Vector2(r.xMin, r.yMin));
            painter.LineTo(new Vector2(r.xMax, r.yMin));
            painter.LineTo(new Vector2(r.xMax, r.yMax));
            painter.LineTo(new Vector2(r.xMin, r.yMax));
            painter.ClosePath();
        }
    }
}
