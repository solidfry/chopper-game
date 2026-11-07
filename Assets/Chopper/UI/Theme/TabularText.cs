namespace Chopper.UI
{
    /// <summary>
    /// Fixed-width digits for changing numbers (timer, speed, altitude, hull, distances). The theme font (Karnivore)
    /// has proportional digits ("1" is about 0.35 em, the others 0.6-0.65 em), so changing numbers would shift the HUD. The rich text
    /// <c>mspace</c> tag gives every character the same advance.
    /// </summary>
    public static class TabularText
    {
        /// <summary>Advance per character, in em of the label's font size. Fits the widest Karnivore digit (Bold: 0.648 em).</summary>
        private const string Open = "<mspace=0.66em>";
        private const string Close = "</mspace>";

        public static string Wrap(string text) => string.IsNullOrEmpty(text) ? text : Open + text + Close;

        /// <summary>
        /// Only the last word, when it is a number: "REPAIR LOCKED 2.4", "AEGIS 12". The fixed advance overrides
        /// letter-spacing, so words stay out of it; only the changing digits need it.
        /// </summary>
        public static string WrapLastNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var start = text.LastIndexOf(' ') + 1;
            if (start >= text.Length || !char.IsDigit(text[start])) return text;
            return text.Substring(0, start) + Open + text.Substring(start) + Close;
        }
    }
}
