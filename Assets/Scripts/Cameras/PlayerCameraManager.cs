using System.Collections.Generic;
using Cinemachine;
using Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cameras
{
    public class PlayerCameraManager : MonoBehaviour
    {
        [SerializeField] private List<CinemachineVirtualCamera> cameras;
        [SerializeField] InputAction cameraSwitch;
    
        [SerializeField] int currentCamera = 0;

        private void Start()
        {
            
        }

        private void OnEnable()
        {
            cameraSwitch.Enable();
            cameraSwitch.performed += SwitchCamera;
            GameEvents.OnTogglePlayerControlsEvent += ToggleControls;
        }
        
        private void OnDisable()
        {
            cameraSwitch.Disable();
            cameraSwitch.performed -= SwitchCamera;
            GameEvents.OnTogglePlayerControlsEvent -= ToggleControls;
        }

        private void ToggleControls(bool enable)
        {
            switch (enable)
            {
                case true:
                    cameraSwitch.Enable();
                    break;
                case false:
                    cameraSwitch.Disable();
                    break;
            }
        }
    
    
        private void SwitchCamera(InputAction.CallbackContext obj)
        { 
            if(cameras == null || cameras.Count == 0) return;
        
            currentCamera++;
        
            if (currentCamera >= cameras.Count)
                currentCamera = 0;
        
            foreach (var c in cameras)
            {
                c.Priority = 10;
                if(c == cameras[currentCamera])
                    c.Priority = 11;
            }
        }
        
        public void DisableAllCameras()
        {
            foreach (var c in cameras)
            {
               c.gameObject.SetActive(false);
            }
        }

    
    
    }
}
