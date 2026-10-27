namespace Chopper.UI
{
    /// <summary>
    /// Fixed-width digits for changing numbers (timer, speed, altitude, hull, distances). The theme font (Ethnocentric)
    /// has proportional digits ("1" is a third as wide as "0"), so changing numbers would shift the HUD. The rich text
    /// <c>mspace</c> tag gives every character the same advance.
    /// </summary>
    public static class TabularText
    {
        /// <summary>Advance per character, in em of the label's font size. Fits the widest Ethnocentric digit.</summary>
        private const string Open = "<mspace=0.82em>";
        private const string Close = "</mspace>";

        public static string Wrap(string text) => string.IsNullOrEmpty(text) ? text : Open + text + Close;
    }
}
