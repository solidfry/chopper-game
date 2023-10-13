using UI.ScriptableObjects;
using UnityEngine;
using UnityEngine.Serialization;

namespace Utilities
{
    public class ColourManager : Singleton<ColourManager>
    {

        [SerializeField] ColorData teamColours;

    }
}
