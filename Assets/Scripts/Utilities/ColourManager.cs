using UI.ScriptableObjects;
using UnityEngine;

namespace Utilities
{
    public class ColourManager : SingletonPersistent<ColourManager>
    {

        [SerializeField] ColourDataList uiColours;
        [SerializeField] ColourDataList teamColours;
        public ColourDataList TeamColours
        {
            get => teamColours;
            set => teamColours = value;
        }
        
        public ColourDataList UIColours
        {
            get => uiColours;
            set => uiColours = value;
        }
        
        public ColourDataList GetTeamColours => TeamColours;
        
        public ColourDataList GetUIColours => UIColours;
        


    }
}
