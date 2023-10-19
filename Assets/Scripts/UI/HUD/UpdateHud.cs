using PlayerInteraction;
using UI.HUD;
using UnityEngine;
using UnityEngine.Events;

namespace UI.Hud
{
    public class UpdateHud : MonoBehaviour
    {
        [SerializeField] OutputHudValues outputHudValues;
        [SerializeField] IntUI speedUI = new();
        [SerializeField] IntUI altitudeUI = new();
        
        [Space]
        [Header("Stabiliser Events")]
        [SerializeField] UnityEvent onStabiliserActive = new();
        [SerializeField] UnityEvent onStabiliserInactive = new();
        void Start()
        {
            if (outputHudValues is null) return; 
        }
        
        private void OnEnable()
        {
            outputHudValues.OnAltitudeChanged += altitudeUI.UpdateText;
            outputHudValues.OnSpeedChanged += speedUI.UpdateText;
            outputHudValues.OnStabiliserActive += StabiliserActive;
        }
        
        private void OnDisable()
        {
            outputHudValues.OnAltitudeChanged -= altitudeUI.UpdateText;
            outputHudValues.OnSpeedChanged -= speedUI.UpdateText;
            outputHudValues.OnStabiliserActive -= StabiliserActive;
        }

        private void StabiliserActive(bool isActive)
        {
            if (!isActive)
                onStabiliserInactive.Invoke();
            else 
                onStabiliserActive.Invoke();
        }

       
    }
}
