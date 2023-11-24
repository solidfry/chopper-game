using Shapes;
using UI.HUD;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace UI.Hud
{
    [ExecuteAlways]
    public class UpdateHud : MonoBehaviour
    {
        public bool IsOwner { get; private set; }
        [SerializeField] OutputHudValues outputHudValues;
        [Header("Reticle UI")]
        [SerializeField] Reticle reticle = new();

        [Header("Setup")]
        [SerializeField] Camera cam;
        // [SerializeField] CompassShapes compass;

        [SerializeField] Transform hudTransform;

        [Header("Text UI Elements")]
        [SerializeField] IntUI speedUI = new();
        [SerializeField] IntUI altitudeUI = new();
        [SerializeField] IntUI healthUI = new();

        [Header("Stabiliser UI Events")]
        [SerializeField] UnityEvent onStabiliserActive = new();
        [SerializeField] UnityEvent onStabiliserInactive = new();


        public void Initialise()
        {
            if (Application.isPlaying == false)
                return;

            if (outputHudValues is null) return;


            cam = GetComponent<Canvas>().worldCamera;

            // if(compass is null)
            //     compass = GetComponent<CompassShapes>();

            reticle.OnStart(cam);

            outputHudValues.OnAltitudeChanged += altitudeUI.UpdateText;
            outputHudValues.OnSpeedChanged += speedUI.UpdateText;
            outputHudValues.OnUpdateHealthEvent += healthUI.UpdateText;
            outputHudValues.OnStabiliserActive += StabiliserActive;
            healthUI.UpdateText(100); // this needs to be updated in a better way
        }

        void Update()
        {
            if (outputHudValues is null) return;

            reticle.OnUpdate();
        }

        private void StabiliserActive(bool isActive)
        {
            if (!isActive)
                onStabiliserInactive?.Invoke();
            else
                onStabiliserActive?.Invoke();
        }


        private void OnDestroy()
        {
            outputHudValues.OnAltitudeChanged -= altitudeUI.UpdateText;
            outputHudValues.OnSpeedChanged -= speedUI.UpdateText;
            outputHudValues.OnUpdateHealthEvent -= healthUI.UpdateText;
            outputHudValues.OnStabiliserActive -= StabiliserActive;
        }
    }
}
