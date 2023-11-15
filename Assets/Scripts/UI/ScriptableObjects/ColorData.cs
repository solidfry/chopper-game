using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UI.ScriptableObjects
{
    [CreateAssetMenu(fileName = "ColorData", menuName = "ColorData", order = 1)]
    public class ColorData : ScriptableObject
    {
        [field: SerializeField] public List<ColourSlot> Colours { get; private set; }

        public Color GetColourByName(string color)
        {
            ColourSlot colour = Colours.Find(x => x.ColorName == color);
            Color newColour = new Color(colour.Color.r, colour.Color.g, colour.Color.b, colour.Color.a);
            return newColour;
        }

        public Color GetAccessibleColourByName(string color) => Colours.Find(x => x.ColorName == name).AccessibleColor;

        public Color GetColourByIndex(int index) => Colours[index].Color;
        public Color GetAccessibleColourByIndex(int index) => Colours[index].AccessibleColor;

        [Serializable]
        public class ColourSlot
        {
            [field: SerializeField] public string ColorName { get; private set; }
            [field: SerializeField] public Color Color { get; private set; }
            [field: SerializeField] public Color AccessibleColor { get; private set; }
        }

    }
}