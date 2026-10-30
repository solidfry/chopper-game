using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Cockpit
{
    /// <summary>
    /// Artificial horizon for the cockpit flight page, drawn with the vector API: ground fill and horizon line turned by
    /// the bank and moved by the pitch, a pitch ladder every 10°, a bank scale with a pointer, and a fixed aircraft mark.
    /// Colours come from USS (<c>--attitude-line</c>, <c>--attitude-ground</c>, <c>--attitude-mark</c>). Set
    /// <c>overflow: hidden</c> in USS, so the ground fill stays inside the element.
    /// </summary>
    [UxmlElement]
    public partial class AttitudeIndicator : VisualElement
    {
        private static readonly CustomStyleProperty<Color> LineProperty = new("--attitude-line");
        private static readonly CustomStyleProperty<Color> GroundProperty = new("--attitude-ground");
        private static readonly CustomStyleProperty<Color> MarkProperty = new("--attitude-mark");
        private static readonly float[] BankTicks = { -60f, -45f, -30f, -20f, -10f, 0f, 10f, 20f, 30f, 45f, 60f };

        private float _roll;
        private float _pitch;
        private Color _line = new(0f, 0.94f, 1f);
        private Color _ground = new(0f, 0.94f, 1f, 0.12f);
        private Color _mark = new(1f, 0.87f, 0.42f);

        /// <summary>Bank, degrees; positive = rolled left (the horizon turns clockwise).</summary>
        [UxmlAttribute, CreateProperty]
        public float roll
        {
            get => _roll;
            set
            {
                if (Mathf.Approximately(_roll, value)) return;
                _roll = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Nose above the horizon, degrees.</summary>
        [UxmlAttribute, CreateProperty]
        public float pitch
        {
            get => _pitch;
            set
            {
                if (Mathf.Approximately(_pitch, value)) return;
                _pitch = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute, Tooltip("Pitch degrees from the top to the bottom of the element.")]
        public float degreesVisible { get; set; } = 60f;

        public AttitudeIndicator()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(LineProperty, out var line)) _line = line;
            if (evt.customStyle.TryGetValue(GroundProperty, out var ground)) _ground = ground;
            if (evt.customStyle.TryGetValue(MarkProperty, out var mark)) _mark = mark;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var painter = context.painter2D;
            var centre = rect.center;
            var perDegree = rect.height / Mathf.Max(1f, degreesVisible);
            var far = rect.width + rect.height;

            // Screen y is down: a positive angle turns clockwise. Along = along the horizon, down = towards the ground.
            var angle = _roll * Mathf.Deg2Rad;
            var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var down = new Vector2(-along.y, along.x);
            var horizon = centre + down * (_pitch * perDegree);

            painter.fillColor = _ground;
            painter.BeginPath();
            painter.MoveTo(horizon - along * far);
            painter.LineTo(horizon + along * far);
            painter.LineTo(horizon + along * far + down * far);
            painter.LineTo(horizon - along * far + down * far);
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = _line;
            painter.lineCap = LineCap.Butt;
            painter.lineWidth = 3f;
            Line(painter, horizon - along * far, horizon + along * far);

            // Pitch ladder: long bars every 20°, short every 10°; end ticks point at the horizon.
            painter.lineWidth = 2f;
            for (var step = -6; step <= 6; step++)
            {
                if (step == 0) continue;
                var middle = centre + down * ((_pitch - step * 10f) * perDegree);
                var half = step % 2 == 0 ? rect.width * 0.17f : rect.width * 0.09f;
                var tick = down * (step > 0 ? 10f : -10f);
                for (var side = -1; side <= 1; side += 2)
                {
                    var inner = middle + along * (side * rect.width * 0.05f);
                    var outer = middle + along * (side * half);
                    Line(painter, inner, outer);
                    Line(painter, outer, outer + tick);
                }
            }

            // Bank scale (fixed) and pointer (turns with the bank).
            var radius = Mathf.Min(rect.width, rect.height) * 0.42f;
            foreach (var bank in BankTicks)
            {
                var direction = Direction(-90f + bank);
                var length = bank % 30f == 0f ? 18f : 10f;
                Line(painter, centre + direction * radius, centre + direction * (radius + length));
            }
            var pointer = Direction(-90f + _roll);
            var side90 = new Vector2(-pointer.y, pointer.x);
            var tip = centre + pointer * (radius - 2f);
            painter.fillColor = _line;
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(tip - pointer * 18f + side90 * 9f);
            painter.LineTo(tip - pointer * 18f - side90 * 9f);
            painter.ClosePath();
            painter.Fill();

            // Aircraft mark: fixed wings and a centre dot.
            painter.strokeColor = _mark;
            painter.lineWidth = 5f;
            var wing = rect.width * 0.2f;
            var gap = rect.width * 0.06f;
            Line(painter, centre + new Vector2(-wing, 0f), centre + new Vector2(-gap, 0f));
            Line(painter, centre + new Vector2(-gap, 0f), centre + new Vector2(-gap, 12f));
            Line(painter, centre + new Vector2(gap, 0f), centre + new Vector2(wing, 0f));
            Line(painter, centre + new Vector2(gap, 0f), centre + new Vector2(gap, 12f));
            painter.fillColor = _mark;
            painter.BeginPath();
            painter.Arc(centre, 5f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Fill();
        }

        /// <summary>Unit vector at a screen angle (degrees, 0 = right, clockwise).</summary>
        private static Vector2 Direction(float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static void Line(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }
    }
}
