using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Hangar
{
    /// <summary>
    /// The hangar's callout lines: from each socket on screen to its slot card, with a small square on the socket.
    /// Drawn with the vector API; <see cref="HangarView"/> sets the points each frame. Colours come from USS
    /// (<c>--line-color</c>, <c>--line-highlight</c>), so states stay in the style sheet.
    /// </summary>
    [UxmlElement]
    public partial class CalloutLines : VisualElement
    {
        private static readonly CustomStyleProperty<Color> LineProperty = new("--line-color");
        private static readonly CustomStyleProperty<Color> HighlightProperty = new("--line-highlight");
        private const float DotSize = 6f;
        private const float LineWidth = 1f;

        private readonly List<(Vector2 From, Vector2 To, bool Highlight)> _lines = new();
        private Color _line = new(0f, 1f, 0.58f, 0.6f);
        private Color _highlight = new(1f, 0.26f, 0.54f);

        public CalloutLines()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        }

        public void ClearLines()
        {
            if (_lines.Count == 0) return;
            _lines.Clear();
            MarkDirtyRepaint();
        }

        /// <summary>Call <see cref="ClearLines"/>, then this per line, then <see cref="Commit"/>. Points in this element's space.</summary>
        public void AddLine(Vector2 socket, Vector2 card, bool highlight) => _lines.Add((socket, card, highlight));

        public void Commit() => MarkDirtyRepaint();

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(LineProperty, out var line)) _line = line;
            if (evt.customStyle.TryGetValue(HighlightProperty, out var highlight)) _highlight = highlight;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineWidth = LineWidth;
            foreach (var (from, to, highlight) in _lines)
            {
                var color = highlight ? _highlight : _line;
                painter.strokeColor = color;
                painter.BeginPath();
                painter.MoveTo(from);
                painter.LineTo(to);
                painter.Stroke();

                painter.fillColor = color;
                painter.BeginPath();
                painter.MoveTo(from + new Vector2(-DotSize, -DotSize) * 0.5f);
                painter.LineTo(from + new Vector2(DotSize, -DotSize) * 0.5f);
                painter.LineTo(from + new Vector2(DotSize, DotSize) * 0.5f);
                painter.LineTo(from + new Vector2(-DotSize, DotSize) * 0.5f);
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
