using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI
{
    /// <summary>
    /// Terminal entry for a message pill (owner, 19 Nov 2026): the box opens like a scan line (USS class
    /// <c>terminal-reveal</c> / <c>terminal-reveal--open</c>), then the text types in with a block cursor on the next
    /// character, the cursor blinks twice and goes. The untyped rest is drawn transparent (rich text), so the pill has
    /// its full size from the start and does not grow while it types. Runs on the element's scheduler, no Update.
    /// Call <see cref="Play"/> when the pill shows (or its text changes) and <see cref="Stop"/> when it hides.
    /// </summary>
    public sealed class TerminalReveal
    {
        private const string RevealClass = "terminal-reveal";
        private const string OpenClass = "terminal-reveal--open";
        /// <summary>While typing: no text shadow (it would show the transparent rest); the glow comes on at the end.</summary>
        private const string TypingClass = "terminal-reveal--typing";
        private const float OpenSeconds = 0.12f;
        private const float CursorSeconds = 0.5f;

        private readonly VisualElement _box;
        private readonly Label _label;
        private readonly float _charsPerSecond;
        private readonly string _cursorColour;
        private IVisualElementScheduledItem _tick;
        private string _text = "";
        private float _start;

        /// <param name="box">The element that opens (the pill, or the label itself).</param>
        /// <param name="label">The label that types.</param>
        /// <param name="cursorColour">Rich text colour of the block cursor, e.g. "#FF4246".</param>
        public TerminalReveal(VisualElement box, Label label, string cursorColour, float charsPerSecond = 55f)
        {
            _box = box;
            _label = label;
            _cursorColour = cursorColour;
            _charsPerSecond = Mathf.Max(1f, charsPerSecond);
            _box?.AddToClassList(RevealClass);
            if (_label != null) _label.enableRichText = true;
        }

        /// <summary>The text last given to <see cref="Play"/> (as given).</summary>
        public string Source { get; private set; } = "";

        /// <summary>Between <see cref="Play"/> and <see cref="Stop"/>.</summary>
        public bool Playing { get; private set; }

        public void Play(string text)
        {
            if (_box == null || _label == null) return;
            Source = text ?? "";
            Playing = true;
            _text = Escape(Source);
            _start = Time.unscaledTime;
            _box.RemoveFromClassList(OpenClass);
            _label.AddToClassList(TypingClass);
            _label.text = Hidden(_text);
            // Next frame, so the scale transition runs from the closed state.
            _box.schedule.Execute(() => _box.AddToClassList(OpenClass)).StartingIn(16);
            _tick ??= _label.schedule.Execute(Step).Every(16);
            _tick.Resume();
        }

        public void Stop()
        {
            Playing = false;
            _tick?.Pause();
            _box?.RemoveFromClassList(OpenClass);
            _label?.RemoveFromClassList(TypingClass);
        }

        private void Step()
        {
            _label.text = Frame(_text, Time.unscaledTime - _start, _charsPerSecond, _cursorColour, out var done);
            if (!done) return;
            _label.RemoveFromClassList(TypingClass);
            _tick.Pause();
        }

        /// <summary>
        /// The label text <paramref name="elapsed"/> seconds after <see cref="Play"/>: nothing while the box opens, then
        /// the typed part, a block cursor on the next character and the rest transparent; then a trailing cursor that
        /// blinks twice; then the plain text (<paramref name="done"/>).
        /// </summary>
        public static string Frame(string text, float elapsed, float charsPerSecond, string cursorColour, out bool done)
        {
            done = false;
            var typing = elapsed - OpenSeconds;
            if (typing < 0f) return Hidden(text);
            var count = (int)(typing * charsPerSecond);
            if (count < text.Length)
                return text.Substring(0, count) + Cursor(text.Substring(count, 1), cursorColour) + Hidden(text.Substring(count + 1));
            var after = typing - text.Length / charsPerSecond;
            if (after < CursorSeconds * 2f)
            {
                // A trailing cursor blinks twice (a transparent space when off, so the width stays).
                var on = (int)(after / (CursorSeconds * 0.5f)) % 2 == 0;
                return text + (on ? Cursor(" ", cursorColour) : "<alpha=#00> </alpha>");
            }
            done = true;
            return text;
        }

        private static string Cursor(string character, string colour) => $"<mark={colour}CC>{character}</mark>";

        private static string Hidden(string text) => string.IsNullOrEmpty(text) ? "" : $"<alpha=#00>{text}</alpha>";

        /// <summary>Message text is plain: angle brackets in a name must not start a tag.</summary>
        private static string Escape(string text) => text.Replace('<', '(').Replace('>', ')');
    }
}
