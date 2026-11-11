using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Home
{
    /// <summary>
    /// Animated home screen backdrop, drawn with the vector API: a neon grid floor that scrolls towards you, a striped
    /// sun on the horizon, two layers of wireframe towers with blinking windows, and a slow scan band. All in the
    /// theme green (the sun in <see cref="sunColor"/>). Repaints about 30 times a second; nothing is picked.
    /// </summary>
    [UxmlElement]
    public partial class HomeBackdrop : VisualElement
    {
        private static readonly Color Green = new(0f, 1f, 0.58f);
        private const float Horizon = 0.6f;
        private const int Towers = 26;

        [UxmlAttribute, CreateProperty, Tooltip("Sun outline colour.")]
        public Color sunColor { get; set; } = new(1f, 0.26f, 0.54f);

        [UxmlAttribute, Tooltip("Grid scroll speed (grid rows per second).")]
        public float speed { get; set; } = 0.35f;

        private readonly IVisualElementScheduledItem _tick;

        public HomeBackdrop()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            _tick = schedule.Execute(MarkDirtyRepaint).Every(33);
            RegisterCallback<DetachFromPanelEvent>(_ => _tick.Pause());
            RegisterCallback<AttachToPanelEvent>(_ => _tick.Resume());
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var w = rect.width;
            var h = rect.height;
            if (w < 2f || h < 2f) return;
            var time = Time.unscaledTime;
            var horizon = h * Horizon;
            var painter = context.painter2D;
            painter.lineJoin = LineJoin.Miter;

            DrawSun(painter, w * 0.5f, horizon, Mathf.Min(w, h) * 0.24f, time);
            DrawTowers(painter, w, horizon, time, far: true);
            DrawTowers(painter, w, horizon, time, far: false);
            DrawGrid(painter, w, h, horizon, time);
            DrawScanBand(painter, w, h, time);
        }

        /// <summary>Half a circle on the horizon, cut by gaps that widen towards the bottom.</summary>
        private void DrawSun(Painter2D painter, float cx, float horizon, float radius, float time)
        {
            painter.lineWidth = 1.5f;
            painter.strokeColor = new Color(sunColor.r, sunColor.g, sunColor.b, 0.55f);
            painter.BeginPath();
            painter.Arc(new Vector2(cx, horizon), radius, Angle.Degrees(180f), Angle.Degrees(360f));
            painter.Stroke();

            // Stripes: horizontal chords, drifting slowly down.
            var drift = time * 6f % 14f;
            for (var y = horizon - radius + 8f + drift; y < horizon; y += 14f)
            {
                var dy = horizon - y;
                var half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
                var t = 1f - dy / radius;
                painter.strokeColor = new Color(sunColor.r, sunColor.g, sunColor.b, 0.12f + 0.3f * t);
                painter.lineWidth = 1f + 2f * t;
                painter.BeginPath();
                painter.MoveTo(new Vector2(cx - half, y));
                painter.LineTo(new Vector2(cx + half, y));
                painter.Stroke();
            }
        }

        /// <summary>Tower outlines on the horizon; the near layer is taller and brighter, windows blink.</summary>
        private static void DrawTowers(Painter2D painter, float w, float horizon, float time, bool far)
        {
            var seed = far ? 11 : 47;
            var alpha = far ? 0.16f : 0.34f;
            var maxHeight = horizon * (far ? 0.38f : 0.58f);
            var count = far ? Towers + 8 : Towers;
            var step = w / count;
            painter.lineWidth = 1f;
            for (var i = 0; i < count; i++)
            {
                var r1 = Hash(seed, i, 1);
                var r2 = Hash(seed, i, 2);
                // Leave the sun mostly clear: towers near the middle are short.
                var centre = Mathf.Abs((i + 0.5f) / count - 0.5f) * 2f;
                var height = maxHeight * (0.25f + 0.75f * r1) * Mathf.Lerp(0.35f, 1f, centre);
                var width = step * (0.55f + 0.4f * r2);
                var x = i * step + (step - width) * 0.5f;
                var top = horizon - height;

                painter.strokeColor = new Color(Green.r, Green.g, Green.b, alpha);
                painter.fillColor = new Color(0f, 0.04f, 0.02f, far ? 0.6f : 0.85f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, horizon));
                painter.LineTo(new Vector2(x, top));
                // Some towers get a stepped crown or a mast.
                if (r2 > 0.6f)
                {
                    painter.LineTo(new Vector2(x + width * 0.25f, top));
                    painter.LineTo(new Vector2(x + width * 0.25f, top - height * 0.08f));
                    painter.LineTo(new Vector2(x + width * 0.75f, top - height * 0.08f));
                    painter.LineTo(new Vector2(x + width * 0.75f, top));
                }
                painter.LineTo(new Vector2(x + width, top));
                painter.LineTo(new Vector2(x + width, horizon));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();

                if (r1 > 0.7f && !far)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x + width * 0.5f, top));
                    painter.LineTo(new Vector2(x + width * 0.5f, top - height * 0.18f));
                    painter.Stroke();
                    // Aircraft warning light.
                    var blink = Mathf.Repeat(time * 0.8f + r2, 1f) < 0.12f ? 0.9f : 0.2f;
                    painter.fillColor = new Color(1f, 0.26f, 0.27f, blink);
                    painter.BeginPath();
                    painter.Arc(new Vector2(x + width * 0.5f, top - height * 0.18f), 2f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Fill();
                }

                // Windows: a few lit dots per tower, each blinking on its own slow cycle.
                if (far) continue;
                var rows = Mathf.FloorToInt(height / 14f);
                for (var row = 1; row < rows; row++)
                for (var col = 0; col < 3; col++)
                {
                    var r = Hash(seed + row, i, col);
                    if (r < 0.72f) continue;
                    var on = Mathf.Repeat(time * 0.07f + r * 7f, 1f) < 0.8f;
                    if (!on) continue;
                    painter.fillColor = new Color(Green.r, Green.g, Green.b, 0.25f + 0.35f * r);
                    painter.BeginPath();
                    AddRect(painter, new Rect(x + width * (0.2f + 0.25f * col), horizon - row * 14f, 2f, 3f));
                    painter.Fill();
                }
            }

            // Horizon line.
            painter.strokeColor = new Color(Green.r, Green.g, Green.b, far ? 0f : 0.7f);
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, horizon));
            painter.LineTo(new Vector2(w, horizon));
            painter.Stroke();
        }

        /// <summary>Perspective grid: lines fan out from the vanishing point; rows scroll towards the viewer.</summary>
        private void DrawGrid(Painter2D painter, float w, float h, float horizon, float time)
        {
            var depth = h - horizon;
            var cx = w * 0.5f;
            painter.lineWidth = 1f;
            const int fan = 22;
            for (var i = -fan; i <= fan; i++)
            {
                var spread = i / (float)fan;
                painter.strokeColor = new Color(Green.r, Green.g, Green.b, 0.28f - 0.12f * Mathf.Abs(spread));
                painter.BeginPath();
                painter.MoveTo(new Vector2(cx + spread * w * 0.08f, horizon));
                painter.LineTo(new Vector2(cx + spread * w * 2.2f, h));
                painter.Stroke();
            }

            // Rows at depth z = 1..N; y = horizon + depth / z. Phase moves them towards the viewer.
            var phase = Mathf.Repeat(time * speed, 1f);
            for (var k = 0; k < 18; k++)
            {
                var z = 18f - k - phase;
                if (z < 1f) continue;
                var y = horizon + depth / z;
                var near = 1f / z;
                painter.strokeColor = new Color(Green.r, Green.g, Green.b, 0.08f + 0.4f * near);
                painter.lineWidth = 1f + near;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(w, y));
                painter.Stroke();
            }
        }

        /// <summary>A soft band sliding down the screen every few seconds (a CRT refresh).</summary>
        private static void DrawScanBand(Painter2D painter, float w, float h, float time)
        {
            var y = Mathf.Repeat(time * 0.18f, 1.3f) * h - h * 0.15f;
            for (var i = 0; i < 6; i++)
            {
                painter.fillColor = new Color(Green.r, Green.g, Green.b, 0.018f * (6 - i));
                painter.BeginPath();
                AddRect(painter, new Rect(0f, y + i * 6f, w, 6f));
                painter.Fill();
            }
        }

        /// <summary>0..1, stable for the same three integers.</summary>
        private static float Hash(int a, int b, int c)
        {
            unchecked
            {
                var x = (uint)(a * 73856093 ^ b * 19349663 ^ c * 83492791);
                x ^= x >> 13;
                x *= 0x5bd1e995;
                x ^= x >> 15;
                return (x & 0xFFFFFF) / (float)0xFFFFFF;
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
