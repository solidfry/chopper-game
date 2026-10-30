using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Cockpit
{
    /// <summary>
    /// A straight segmented meter (hull, boost) for the cockpit systems page, drawn with the vector API: the straight
    /// cousin of <see cref="Hud.ArcGauge"/>. Colours come from USS (<c>--bar-fill</c>, <c>--bar-track</c>), so states
    /// are plain USS classes.
    /// </summary>
    [UxmlElement]
    public partial class SegmentBar : VisualElement
    {
        private static readonly CustomStyleProperty<Color> FillProperty = new("--bar-fill");
        private static readonly CustomStyleProperty<Color> TrackProperty = new("--bar-track");

        private float _value = 1f;
        private Color _fill = new(0f, 1f, 0.58f);
        private Color _track = new(0f, 1f, 0.58f, 0.15f);

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

        [UxmlAttribute] public int segments { get; set; } = 10;
        [UxmlAttribute, Tooltip("Pixels between segments.")] public float gap { get; set; } = 5f;

        public SegmentBar()
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
            if (segments <= 0 || rect.width <= 0f || rect.height <= 0f) return;
            var width = (rect.width - gap * (segments - 1)) / segments;
            if (width <= 0f) return;
            var painter = context.painter2D;
            // A segment is lit when the value reaches its middle, so a full bar lights them all.
            var lit = Mathf.RoundToInt(_value * segments);
            for (var i = 0; i < segments; i++)
            {
                var x = rect.xMin + i * (width + gap);
                painter.fillColor = i < lit ? _fill : _track;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, rect.yMin));
                painter.LineTo(new Vector2(x + width, rect.yMin));
                painter.LineTo(new Vector2(x + width, rect.yMax));
                painter.LineTo(new Vector2(x, rect.yMax));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
