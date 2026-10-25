using Events;
using Unity.Netcode;
using UnityEngine;

namespace Cameras
{
    public class DisableMainCameraOnClient : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private AudioListener mainAudioListener;

        private void Awake()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
        
            if(mainAudioListener == null)
                mainAudioListener = mainCamera.GetComponent<AudioListener>();
            
            EnableMainCamera();
        }
        
        private void DisableMainCamera()
        {
            mainCamera.depth = -1;
            mainCamera.enabled = false;
            mainAudioListener.enabled = false;
            Debug.Log("Main cam disabled");
        }

        private void EnableMainCamera()
        {
            mainCamera.depth = 0;
            mainCamera.enabled = true;
            mainAudioListener.enabled = true;
        }
   
    }
}
