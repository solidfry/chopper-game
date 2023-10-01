using UnityEngine;

namespace UI.Hud
{
    public class UpdateHud : MonoBehaviour
    {
        [SerializeField] OutputHudValues outputHudValues;
        [SerializeField] IntUI speedUI = new();
        [SerializeField] IntUI altitudeUI = new();
        void Start()
        {
            if (outputHudValues is null) return; 
        }
        
        private void OnEnable()
        {
            outputHudValues.OnAltitudeChanged += altitudeUI.UpdateText;
            outputHudValues.OnSpeedChanged += speedUI.UpdateText;
        }
    
        private void OnDisable()
        {
            outputHudValues.OnAltitudeChanged -= altitudeUI.UpdateText;
            outputHudValues.OnSpeedChanged -= speedUI.UpdateText;
        }
    }
}
