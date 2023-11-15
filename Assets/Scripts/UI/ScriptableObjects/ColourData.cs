using UnityEngine;

namespace UI.ScriptableObjects
{
    [CreateAssetMenu(fileName = "ColourData", menuName = "Colours/ColourData", order = 0)]
    public class ColourData : ScriptableObject
    {
        [field: SerializeField] public Color Color { get; private set; }
        [field: SerializeField] public Color ShadeColor { get; private set; }
    }
}