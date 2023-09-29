using Events;
using UnityEngine;

public class FindPlayer : MonoBehaviour
{
    Cinemachine.CinemachineVirtualCamera vcam;
    private void Start()
    {
        vcam = GetComponent<Cinemachine.CinemachineVirtualCamera>();
    }

    private void OnEnable()
    {
        GameEvents.onSendPlayerEvent += SetCameraTarget;
    }

    private void OnDisable()
    {
        GameEvents.onSendPlayerEvent -= SetCameraTarget;
    }

    private void SetCameraTarget(Transform player)
    {
        vcam.LookAt = player;
        vcam.Follow = player;
    }
}
