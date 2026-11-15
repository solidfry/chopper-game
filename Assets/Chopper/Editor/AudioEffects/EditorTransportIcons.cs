using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Chopper.Editor.AudioEffects
{
    public enum TransportIcon
    {
        Play,
        Pause,
        Stop,
        Next,
        Previous,
        First,
        Loop
    }

    /// <summary>
    /// Transport icons built on Unity's own builtin editor icons, so the player looks
    /// native and follows the active skin (light/dark).
    ///
    /// Only icon names that are known to exist are requested. Calling
    /// <c>EditorGUIUtility.IconContent</c> with an unknown name logs a warning, and the
    /// set of builtin icons is not queryable at runtime, so the candidates are a fixed,
    /// verified list per icon. If none resolve, a vector glyph is drawn instead.
    /// </summary>
    public static class EditorTransportIcons
    {
        static readonly Dictionary<TransportIcon, string> s_Builtin =
            new Dictionary<TransportIcon, string>
            {
                { TransportIcon.Play,     "PlayButton" },
                { TransportIcon.Pause,    "PauseButton" },
                { TransportIcon.Stop,     "StepButton" },
                { TransportIcon.Next,     "animation.nextkey" },
                { TransportIcon.Previous, "animation.prevkey" },
                { TransportIcon.First,    "animation.firstkey" },
                { TransportIcon.Loop,     "Loop" },
            };

        static readonly Dictionary<TransportIcon, GUIContent> s_Cache =
            new Dictionary<TransportIcon, GUIContent>();

        /// <summary>Builtin GUIContent for the icon, or null when unavailable.</summary>
        public static GUIContent GetContent(TransportIcon icon)
        {
            if (s_Cache.TryGetValue(icon, out var cached)) return cached;

            GUIContent resolved = null;
            if (s_Builtin.TryGetValue(icon, out var name))
            {
                var c = EditorGUIUtility.IconContent(name);
                if (c != null && c.image != null) resolved = c;
            }

            s_Cache[icon] = resolved;
            return resolved;
        }

        public static string Tooltip(TransportIcon icon)
        {
            switch (icon)
            {
                case TransportIcon.Play: return "Play";
                case TransportIcon.Pause: return "Pause";
                case TransportIcon.Stop: return "Stop";
                case TransportIcon.Next: return "Next clip";
                case TransportIcon.Previous: return "Previous clip";
                case TransportIcon.First: return "First clip";
                case TransportIcon.Loop: return "Loop";
                default: return string.Empty;
            }
        }

        /// <summary>Draws the icon centred inside <paramref name="rect"/>.</summary>
        public static void Draw(TransportIcon icon, Rect rect, Color tint, bool active = true)
        {
            Color color = active ? tint : new Color(tint.r, tint.g, tint.b, tint.a * 0.35f);
            var content = GetContent(icon);

            if (content != null)
            {
                var tex = content.image as Texture2D;
                Rect target = rect;

                // Fit inside the button preserving the icon's aspect (Loop is wide).
                if (tex != null && tex.width > 0 && tex.height > 0)
                {
                    float aspect = (float)tex.width / tex.height;
                    float h = Mathf.Min(rect.height * 0.68f, rect.width);
                    float w = h * aspect;
                    if (w > rect.width * 0.88f)
                    {
                        w = rect.width * 0.88f;
                        h = w / aspect;
                    }
                    target = new Rect(rect.center.x - w * 0.5f, rect.center.y - h * 0.5f, w, h);
                }

                Color old = GUI.color;
                GUI.color = color;
                GUI.DrawTexture(target, content.image, ScaleMode.ScaleToFit, true);
                GUI.color = old;
                return;
            }

            DrawVector(icon, rect, color);
        }

        // ── Vector fallback ────────────────────────────────────

        static void DrawVector(TransportIcon icon, Rect rect, Color c)
        {
            float s = Mathf.Min(rect.width, rect.height) * 0.58f;
            float x = rect.center.x - s * 0.5f;
            float y = rect.center.y - s * 0.5f;
            float bar = Mathf.Max(2f, s * 0.2f);

            switch (icon)
            {
                case TransportIcon.Play:
                    Triangle(x, y, s, c, right: true);
                    break;

                case TransportIcon.Pause:
                    RectBar(new Rect(x, y, bar * 1.4f, s), c);
                    RectBar(new Rect(x + s - bar * 1.4f, y, bar * 1.4f, s), c);
                    break;

                case TransportIcon.Stop:
                    RectBar(new Rect(x, y, s, s), c);
                    break;

                case TransportIcon.Next:
                    Triangle(x, y, s - bar * 1.6f, c, right: true);
                    RectBar(new Rect(x + s - bar, y, bar, s), c);
                    break;

                case TransportIcon.Previous:
                    Triangle(x + bar * 1.6f, y, s - bar * 1.6f, c, right: false);
                    RectBar(new Rect(x, y, bar, s), c);
                    break;

                case TransportIcon.First:
                    Triangle(x + bar * 1.6f, y, s - bar * 1.6f, c, right: false);
                    RectBar(new Rect(x, y, bar, s), c);
                    break;

                case TransportIcon.Loop:
                    RectBar(new Rect(x, y, s, bar), c);
                    RectBar(new Rect(x, y + s - bar, s, bar), c);
                    RectBar(new Rect(x, y, bar, s), c);
                    RectBar(new Rect(x + s - bar, y, bar, s), c);
                    break;
            }
        }

        static void Triangle(float x, float y, float s, Color c, bool right)
        {
            int rows = Mathf.Max(8, Mathf.RoundToInt(s));
            float rowH = s / rows;

            for (int r = 0; r < rows; r++)
            {
                float t = (r + 0.5f) / rows;
                float w = Mathf.Max(1f, s * 0.92f * (1f - Mathf.Abs(t * 2f - 1f)));
                float rx = right ? x : x + s - w;
                RectBar(new Rect(rx, y + r * rowH, w, rowH + 0.6f), c);
            }
        }

        static void RectBar(Rect r, Color c)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
