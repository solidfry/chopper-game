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
            // TODO: Need a way to enable and disable this more effectively at the correct time
            mainAudioListener.enabled = false;
        }
    }
}
