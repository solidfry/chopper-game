using Events;
using Unity.Netcode;
using UnityEngine;

namespace Cameras
{
    public class DisableMainCameraOnClient : NetworkBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private AudioListener mainAudioListener;

        private void Start()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
        
            if(mainAudioListener == null)
                mainAudioListener = mainCamera.GetComponent<AudioListener>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsClient && IsLocalPlayer || IsHost)
            {
                EnableMainCamera();
                GameEvents.OnDisableMainCameraEvent += DisableMainCamera;
            }
        }
    
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (IsClient && IsLocalPlayer || IsHost)
            {
                GameEvents.OnDisableMainCameraEvent -= DisableMainCamera;
            }
        }

        private void DisableMainCamera()
        {
            mainCamera.depth = -1;
            mainCamera.enabled = false;
            mainAudioListener.enabled = false;
        }

        private void EnableMainCamera()
        {
            mainCamera.depth = 0;
            mainCamera.enabled = true;
            mainAudioListener.enabled = true;
        }

   
    }
}
