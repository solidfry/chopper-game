using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// The HUD as if it is drawn on the inside of a sphere round the pilot: it gets bigger towards the screen edge, so
    /// numbers fan out and straight lines bow. The top and bottom screen edges (at the middle) stay on the panel edges, so
    /// the panel always covers the screen; the panel's corners go past the screen (see <see cref="Inset"/>).
    /// <c>HudLook.shader</c> has the same formula (Frag): change them together.
    /// </summary>
    public static class HudSphere
    {
        /// <summary>
        /// Screen point to the panel point that the look pass draws there. Use it to put a HUD element on a world point.
        /// Both in viewport space, (0,0) to (1,1). The map is the same up and down, so y can go either way.
        /// </summary>
        /// <param name="radians">Angle from the screen centre to the top edge. 0 = flat.</param>
        /// <param name="aspect">Width / height.</param>
        public static Vector2 ToHud(Vector2 viewport, float radians, float aspect)
        {
            if (radians <= 0.001f) return viewport;
            var point = new Vector2((viewport.x - 0.5f) * 2f * aspect, (viewport.y - 0.5f) * 2f);
            var radius = point.magnitude;
            var slope = Mathf.Tan(radians);
            var scale = radius > 0.0001f ? Mathf.Atan(radius * slope) / (radians * radius) : slope / radians;
            return new Vector2(point.x * scale / aspect * 0.5f + 0.5f, point.y * scale * 0.5f + 0.5f);
        }

        /// <summary>The part of the panel, on each side, that the screen corners cut off. Keep corner text inside this.</summary>
        public static Vector2 Inset(float radians, float aspect)
        {
            var corner = ToHud(Vector2.one, radians, aspect);
            return new Vector2(1f - corner.x, 1f - corner.y);
        }
    }
}
