using System;
using System.Collections.Generic;
using Chopper.Presentation.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Cockpit
{
    /// <summary>
    /// Top-down radar for the cockpit tactical page, nose up: range rings, the contacts of the
    /// <see cref="WorldMarkerModel"/> by bearing and distance (choppers as diamonds, ground targets as squares, the lock
    /// target ringed; contacts out of range sit on the rim), and an inbound missile as a warning spoke. Colours come from
    /// USS (<c>--radar-line</c>, <c>--radar-air</c>, <c>--radar-ground</c>, <c>--radar-alert</c>).
    /// </summary>
    [UxmlElement]
    public partial class RadarScope : VisualElement
    {
        private static readonly CustomStyleProperty<Color> LineProperty = new("--radar-line");
        private static readonly CustomStyleProperty<Color> AirProperty = new("--radar-air");
        private static readonly CustomStyleProperty<Color> GroundProperty = new("--radar-ground");
        private static readonly CustomStyleProperty<Color> AlertProperty = new("--radar-alert");

        private readonly List<WorldMarker> _contacts = new();
        private Color _line = new(1f, 0.87f, 0.42f);
        private Color _air = new(1f, 0.26f, 0.27f);
        private Color _ground = new(1f, 0.87f, 0.42f);
        private Color _alert = new(1f, 0.26f, 0.27f);
        private bool _missile;
        private float _missileDegrees;

        [UxmlAttribute, Tooltip("Metres at the rim.")]
        public float range { get; set; } = 1000f;

        public RadarScope()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        public void SetContacts(ReadOnlySpan<WorldMarker> markers)
        {
            _contacts.Clear();
            foreach (var marker in markers) _contacts.Add(marker);
            MarkDirtyRepaint();
        }

        /// <param name="degrees">Where it comes from: 0 = ahead, 90 = right.</param>
        public void SetMissile(bool inbound, float degrees)
        {
            if (_missile == inbound && Mathf.Approximately(_missileDegrees, degrees)) return;
            _missile = inbound;
            _missileDegrees = degrees;
            MarkDirtyRepaint();
        }

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(LineProperty, out var line)) _line = line;
            if (evt.customStyle.TryGetValue(AirProperty, out var air)) _air = air;
            if (evt.customStyle.TryGetValue(GroundProperty, out var ground)) _ground = ground;
            if (evt.customStyle.TryGetValue(AlertProperty, out var alert)) _alert = alert;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f - 4f;
            if (radius <= 0f) return;
            var painter = context.painter2D;
            var centre = rect.center;
            var dim = new Color(_line.r, _line.g, _line.b, _line.a * 0.45f);

            painter.lineWidth = 2f;
            painter.strokeColor = _line;
            Circle(painter, centre, radius);
            painter.strokeColor = dim;
            Circle(painter, centre, radius * 0.5f);
            Line(painter, centre + new Vector2(-radius, 0f), centre + new Vector2(radius, 0f));
            Line(painter, centre + new Vector2(0f, -radius), centre + new Vector2(0f, radius));

            // Own chopper: a small arrow, nose up.
            painter.fillColor = _line;
            Triangle(painter, centre + new Vector2(0f, -12f), centre + new Vector2(8f, 8f), centre + new Vector2(-8f, 8f));

            foreach (var contact in _contacts)
            {
                var fraction = Mathf.Min(contact.Distance / Mathf.Max(1f, range), 1f);
                var point = centre + Direction(contact.BearingDegrees) * (radius * fraction);
                var air = contact.Kind == WorldMarkerKind.Enemy;
                var size = fraction >= 1f ? 5f : 9f;
                painter.fillColor = air ? _air : _ground;
                painter.BeginPath();
                if (air)
                {
                    painter.MoveTo(point + new Vector2(0f, -size));
                    painter.LineTo(point + new Vector2(size, 0f));
                    painter.LineTo(point + new Vector2(0f, size));
                    painter.LineTo(point + new Vector2(-size, 0f));
                }
                else
                {
                    painter.MoveTo(point + new Vector2(-size, -size) * 0.75f);
                    painter.LineTo(point + new Vector2(size, -size) * 0.75f);
                    painter.LineTo(point + new Vector2(size, size) * 0.75f);
                    painter.LineTo(point + new Vector2(-size, size) * 0.75f);
                }
                painter.ClosePath();
                painter.Fill();
                if (contact.IsLockTarget)
                {
                    painter.strokeColor = _alert;
                    painter.lineWidth = 3f;
                    Circle(painter, point, 17f);
                    painter.lineWidth = 2f;
                }
            }

            if (_missile)
            {
                var direction = Direction(_missileDegrees);
                painter.strokeColor = _alert;
                painter.lineWidth = 5f;
                Line(painter, centre + direction * (radius * 0.35f), centre + direction * radius);
                painter.fillColor = _alert;
                var side = new Vector2(-direction.y, direction.x);
                var tip = centre + direction * (radius * 0.3f);
                Triangle(painter, tip, tip + direction * 22f + side * 12f, tip + direction * 22f - side * 12f);
            }
        }

        /// <summary>Unit vector for a bearing: 0 = up (ahead), 90 = right.</summary>
        private static Vector2 Direction(float bearingDegrees)
        {
            var radians = bearingDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));
        }

        private static void Circle(Painter2D painter, Vector2 centre, float radius)
        {
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();
        }

        private static void Line(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private static void Triangle(Painter2D painter, Vector2 a, Vector2 b, Vector2 c)
        {
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(b);
            painter.LineTo(c);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
