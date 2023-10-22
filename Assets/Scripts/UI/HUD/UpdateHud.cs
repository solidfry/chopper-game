using UI.HUD;
using UnityEngine;
using UnityEngine.Events;

namespace UI.Hud
{
    public class UpdateHud : MonoBehaviour
    {
        [SerializeField] OutputHudValues outputHudValues;
        
        [Header("Reticle UI")]
        [SerializeField] Reticle reticle = new();
        
        [Header("Text UI Elements")]
        [SerializeField] IntUI speedUI = new();
        [SerializeField] IntUI altitudeUI = new();
        
        [Header("Stabiliser UI Events")]
        [SerializeField] UnityEvent onStabiliserActive = new();
        [SerializeField] UnityEvent onStabiliserInactive = new();
        
        
        void Start()
        {
            if (outputHudValues is null) return; 
            
            reticle.OnStart(GetComponent<Canvas>().worldCamera);
        }
        
        void Update()
        {
            if (outputHudValues is null) return; 
            
            reticle.OnUpdate();
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
                onStabiliserInactive?.Invoke();
            else 
                onStabiliserActive?.Invoke();
        }

       
    }
}
