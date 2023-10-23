using UI.ScriptableObjects;
using UnityEngine;

namespace Utilities
{
    public class ColourManager : SingletonPersistent<ColourManager>
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
