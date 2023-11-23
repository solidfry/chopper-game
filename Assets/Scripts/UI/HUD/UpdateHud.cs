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
        

        public void Initialise(bool isOwner)
        {
            IsOwner = isOwner;
            
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
        
        // public override void DrawShapes( Camera cam ) {
        //     if (IsOwner == false) return;
        //     if( cam != this.cam ) // only draw in the player camera
        //         return;
        //
        //     using( Draw.Command( cam ) ) {
        //         Draw.ZTest = CompareFunction.Always; // to make sure it draws on top of everything like a HUD
        //         Draw.Matrix = hudTransform.localToWorldMatrix; // draw it in the space of crosshairTransform
        //         Draw.BlendMode = ShapesBlendMode.Transparent;
        //         Draw.LineGeometry = LineGeometry.Flat2D;
        //         compass.DrawCompass( this.cam.transform.forward );
        //     }
        // }

        private void OnDestroy()
        {
            outputHudValues.OnAltitudeChanged -= altitudeUI.UpdateText;
            outputHudValues.OnSpeedChanged -= speedUI.UpdateText;
            outputHudValues.OnUpdateHealthEvent -= healthUI.UpdateText;
            outputHudValues.OnStabiliserActive -= StabiliserActive;
        }
    }
}
