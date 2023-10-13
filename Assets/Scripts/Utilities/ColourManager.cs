using UI.ScriptableObjects;
using UnityEngine;
using UnityEngine.Serialization;

namespace Utilities
{
    public class ColourManager : Singleton<ColourManager>
    {

        [SerializeField] ColorData teamColours;
        public ColorData TeamColours
        {
            get => teamColours;
            set => teamColours = value;
        }
        
        public ColorData GetTeamColours => TeamColours;

    }
}
