
public class ColourData : ScriptableObject
{

    public void SetColour(int index, Color color)
    {
        colours[index] = color;
    }

    [Serializable]
    public class ColourSlot
    {
        public Color colour;
        public string colorName;
    }
}