using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Hud
{
    /// <summary>
    /// A segmented arc meter drawn with the vector API (boost tank, hull). It fills the element: the arc sits on a circle
    /// as wide as the element. Angles are in degrees, 0 = right, clockwise (screen y down); a negative sweep runs
    /// anticlockwise. Colours come from USS (<c>--arc-fill</c>, <c>--arc-track</c>), so states are plain USS classes.
    /// </summary>
    [UxmlElement]
    public partial class ArcGauge : VisualElement
    {
        private static readonly CustomStyleProperty<Color> FillProperty = new("--arc-fill");
        private static readonly CustomStyleProperty<Color> TrackProperty = new("--arc-track");

        private float _value = 1f;
        private Color _fill = new(0.22f, 1f, 0.53f);
        private Color _track = new(0.22f, 1f, 0.53f, 0.15f);

        /// <summary>0..1.</summary>
        [UxmlAttribute, CreateProperty]
        public float value
        {
            get => _value;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(_value, value)) return;
                _value = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute] public float startAngle { get; set; } = 145f;
        [UxmlAttribute] public float sweep { get; set; } = 70f;
        [UxmlAttribute] public int segments { get; set; } = 14;
        [UxmlAttribute, Tooltip("Degrees between segments.")] public float gap { get; set; } = 1.2f;
        [UxmlAttribute] public float thickness { get; set; } = 6f;

        public ArcGauge()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(FillProperty, out var fill)) _fill = fill;
            if (evt.customStyle.TryGetValue(TrackProperty, out var track)) _track = track;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f - thickness * 0.5f;
            if (radius <= 0f || segments <= 0) return;

            var painter = context.painter2D;
            painter.lineWidth = thickness;
            painter.lineCap = LineCap.Butt;
            var centre = rect.center;
            var step = sweep / segments;
            var direction = sweep >= 0f ? ArcDirection.Clockwise : ArcDirection.CounterClockwise;
            var halfGap = Mathf.Sign(step) * gap * 0.5f;
            // A segment is lit when the value reaches its middle, so a full tank lights them all.
            var lit = Mathf.RoundToInt(_value * segments);

            for (var i = 0; i < segments; i++)
            {
                var from = startAngle + step * i + halfGap;
                var to = startAngle + step * (i + 1) - halfGap;
                painter.strokeColor = i < lit ? _fill : _track;
                painter.BeginPath();
                painter.Arc(centre, radius, Angle.Degrees(from), Angle.Degrees(to), direction);
                painter.Stroke();
            }
        }
    }
}
