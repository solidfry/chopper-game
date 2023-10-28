using Unity.Netcode;
using UnityEngine;

public class DisableMainCameraOnClient : NetworkBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private AudioListener mainAudioListener;

    private void Start()
    {
        mainCamera = Camera.main;
        mainAudioListener = GetComponent<AudioListener>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsClient || IsHost)
        {
            mainCamera.enabled = false;
            mainAudioListener.enabled = false;
        }
    }
}
