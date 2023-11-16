using System.Collections.Generic;
using UnityEngine;

namespace UI.ScriptableObjects
{
    [CreateAssetMenu(fileName = "ColourDataList", menuName = "Colours/ColourDataList", order = 1)]
    public class ColourDataList : ScriptableObject
    {
        [field: SerializeField] public List<ColourData> Colours { get; private set; }

        public Color GetColourByName(string color)
        {
            ColourData colour = Colours.Find(x => x.name == color);
            Color newColour = new Color(colour.Color.r, colour.Color.g, colour.Color.b, colour.Color.a);
            return newColour;
        }
        
        public Color GetShadeColourByName(string color)
        {
            ColourData colour = Colours.Find(x => x.name == color);
            Color newColour = new Color(colour.ShadeColor.r, colour.ShadeColor.g, colour.ShadeColor.b, colour.ShadeColor.a);
            return newColour;
        }

        public Color GetAccessibleColourByName(string color) => Colours.Find(x => x.name == name).ShadeColor;

        public Color GetColourByIndex(int index) => Colours[index].Color;
        public Color GetAccessibleColourByIndex(int index) => Colours[index].ShadeColor;

    }
}