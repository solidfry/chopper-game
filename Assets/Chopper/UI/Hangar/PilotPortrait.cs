using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Hangar
{
    /// <summary>
    /// A pilot portrait drawn with the vector API until there is art (plan section 5b, step 5): a helmeted pilot from
    /// the shoulders up in the hangar's neon wireframe look (dark fill, accent lines, a bright visor). The shape comes
    /// from the pilot id, so each pilot keeps the same face: helmet (round, angular, crested), visor (band, slit, twin
    /// lenses, T), and extras (antenna, breather, cheek vents, stripe). Fills the element; keeps a 5:7 shape inside it.
    /// </summary>
    [UxmlElement]
    public partial class PilotPortrait : VisualElement
    {
        private int _seed = 1;
        private Color _accent = new(0f, 1f, 0.58f);

        public PilotPortrait()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Set(int seed, Color accent)
        {
            if (seed == _seed && accent == _accent) return;
            _seed = seed;
            _accent = accent;
            MarkDirtyRepaint();
        }

        private Rect _box;

        /// <summary>Normalised (0..1, y down) to element space inside the 5:7 box.</summary>
        private Vector2 P(float x, float y) => new(_box.x + x * _box.width, _box.y + y * _box.height);

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 4f || rect.height < 4f) return;
            var width = Mathf.Min(rect.width, rect.height * 5f / 7f);
            var height = width * 7f / 5f;
            _box = new Rect(rect.x + (rect.width - width) * 0.5f, rect.y + (rect.height - height) * 0.5f, width, height);

            var h = Hash(_seed);
            var helmet = (int)(h % 3);
            var visor = (int)((h >> 4) % 4);
            var antenna = ((h >> 8) & 1) != 0;
            var breather = ((h >> 9) & 1) != 0;
            var vents = ((h >> 10) & 1) != 0;
            var stripe = ((h >> 11) & 1) != 0;

            var painter = context.painter2D;
            var line = _accent;
            var dim = new Color(_accent.r, _accent.g, _accent.b, 0.45f);
            var fill = new Color(_accent.r * 0.12f, _accent.g * 0.12f, _accent.b * 0.12f, 0.95f);
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.lineWidth = Mathf.Max(1.5f, width * 0.012f);

            // Shoulders and collar.
            Shape(painter, fill, dim, P(0.02f, 1f), P(0.1f, 0.8f), P(0.32f, 0.72f), P(0.68f, 0.72f), P(0.9f, 0.8f), P(0.98f, 1f));
            Shape(painter, fill, dim, P(0.36f, 0.74f), P(0.4f, 0.64f), P(0.6f, 0.64f), P(0.64f, 0.74f));
            Line(painter, dim, P(0.5f, 0.74f), P(0.5f, 1f));
            Line(painter, dim, P(0.2f, 0.86f), P(0.36f, 0.8f));
            Line(painter, dim, P(0.8f, 0.86f), P(0.64f, 0.8f));

            // Helmet.
            switch (helmet)
            {
                case 0: // round
                    painter.fillColor = fill;
                    painter.strokeColor = line;
                    painter.BeginPath();
                    painter.MoveTo(P(0.24f, 0.62f));
                    painter.BezierCurveTo(P(0.14f, 0.2f), P(0.86f, 0.2f), P(0.76f, 0.62f));
                    painter.LineTo(P(0.66f, 0.68f));
                    painter.LineTo(P(0.34f, 0.68f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                    break;
                case 1: // angular
                    Shape(painter, fill, line, P(0.24f, 0.62f), P(0.22f, 0.36f), P(0.34f, 0.2f), P(0.66f, 0.2f), P(0.78f, 0.36f),
                        P(0.76f, 0.62f), P(0.64f, 0.69f), P(0.36f, 0.69f));
                    break;
                default: // crested
                    Shape(painter, fill, line, P(0.25f, 0.62f), P(0.23f, 0.34f), P(0.42f, 0.22f), P(0.5f, 0.1f), P(0.58f, 0.22f),
                        P(0.77f, 0.34f), P(0.75f, 0.62f), P(0.63f, 0.69f), P(0.37f, 0.69f));
                    Line(painter, dim, P(0.5f, 0.12f), P(0.5f, 0.32f));
                    break;
            }

            // Visor: a bright core over a soft glow.
            var glow = new Color(_accent.r, _accent.g, _accent.b, 0.25f);
            var bright = Color.Lerp(_accent, Color.white, 0.35f);
            switch (visor)
            {
                case 0: // wide band
                    Shape(painter, glow, null, P(0.25f, 0.4f), P(0.75f, 0.4f), P(0.72f, 0.52f), P(0.28f, 0.52f));
                    Shape(painter, bright, null, P(0.28f, 0.43f), P(0.72f, 0.43f), P(0.7f, 0.49f), P(0.3f, 0.49f));
                    break;
                case 1: // slit
                    Shape(painter, glow, null, P(0.27f, 0.43f), P(0.73f, 0.43f), P(0.73f, 0.49f), P(0.27f, 0.49f));
                    Shape(painter, bright, null, P(0.29f, 0.452f), P(0.71f, 0.452f), P(0.71f, 0.468f), P(0.29f, 0.468f));
                    break;
                case 2: // twin lenses
                    Circle(painter, glow, P(0.39f, 0.46f), width * 0.09f);
                    Circle(painter, glow, P(0.61f, 0.46f), width * 0.09f);
                    Circle(painter, bright, P(0.39f, 0.46f), width * 0.045f);
                    Circle(painter, bright, P(0.61f, 0.46f), width * 0.045f);
                    break;
                default: // T visor
                    Shape(painter, glow, null, P(0.27f, 0.4f), P(0.73f, 0.4f), P(0.73f, 0.46f), P(0.54f, 0.46f), P(0.54f, 0.6f),
                        P(0.46f, 0.6f), P(0.46f, 0.46f), P(0.27f, 0.46f));
                    Shape(painter, bright, null, P(0.3f, 0.42f), P(0.7f, 0.42f), P(0.7f, 0.44f), P(0.52f, 0.44f), P(0.52f, 0.58f),
                        P(0.48f, 0.58f), P(0.48f, 0.44f), P(0.3f, 0.44f));
                    break;
            }

            if (stripe) Line(painter, line, P(0.32f, 0.26f), P(0.3f, 0.36f));
            if (vents)
                for (var i = 0; i < 3; i++)
                {
                    var y = 0.53f + i * 0.03f;
                    Line(painter, dim, P(0.27f, y), P(0.33f, y));
                    Line(painter, dim, P(0.67f, y), P(0.73f, y));
                }
            if (breather)
            {
                Shape(painter, fill, line, P(0.42f, 0.58f), P(0.58f, 0.58f), P(0.56f, 0.68f), P(0.44f, 0.68f));
                for (var i = 0; i < 3; i++) Line(painter, dim, P(0.45f + i * 0.05f, 0.6f), P(0.45f + i * 0.05f, 0.66f));
            }
            if (antenna)
            {
                Line(painter, line, P(0.76f, 0.4f), P(0.86f, 0.12f));
                Circle(painter, bright, P(0.86f, 0.12f), width * 0.015f);
            }
        }

        private static void Shape(Painter2D painter, Color fill, Color? stroke, params Vector2[] points)
        {
            painter.fillColor = fill;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.ClosePath();
            painter.Fill();
            if (stroke == null) return;
            painter.strokeColor = stroke.Value;
            painter.Stroke();
        }

        private static void Line(Painter2D painter, Color colour, Vector2 from, Vector2 to)
        {
            painter.strokeColor = colour;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private static void Circle(Painter2D painter, Color colour, Vector2 centre, float radius)
        {
            painter.fillColor = colour;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        private static uint Hash(int seed)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return h;
            }
        }
    }
}
