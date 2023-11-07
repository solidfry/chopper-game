using UI.ScriptableObjects;
using UnityEngine;

namespace Utilities
{
    public class ColourManager : SingletonPersistent<ColourManager>
    {

        [SerializeField] ColorData uiColours;
        [SerializeField] ColorData teamColours;
        public ColorData TeamColours
        {
            get => teamColours;
            set => teamColours = value;
        }
        
        public ColorData UIColours
        {
            get => uiColours;
            set => uiColours = value;
        }
        
        public ColorData GetTeamColours => TeamColours;
        
        public ColorData GetUIColours => UIColours;
        


    }
}
