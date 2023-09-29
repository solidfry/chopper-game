using System;
using System.Collections;
using System.Collections.Generic;
using Events;
using UnityEngine;

public class SendCamera : MonoBehaviour
{
    Camera CameraToSend => GetComponent<Camera>();
    private void OnEnable()
    {
        GameEvents.onSendPlayerEvent += SendCameraToPlayerHud;
    }

    private void OnDisable()
    {
        GameEvents.onSendPlayerEvent -= SendCameraToPlayerHud;
    }

    private void SendCameraToPlayerHud(Transform player)
    {
        if (CameraToSend == null) return;

        GameEvents.onSendCameraEvent?.Invoke(CameraToSend);
    }
}
