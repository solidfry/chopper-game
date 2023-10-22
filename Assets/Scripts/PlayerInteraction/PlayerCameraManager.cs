using System;
using Cinemachine;
using UnityEngine;

namespace PlayerInteraction
{
    [Serializable]
    public class PlayerCameraManager
    {
        Camera _playerCamera;
        public CinemachineVirtualCamera Vcam { get; private set; }
        
        public void Initialize(Camera camera, CinemachineVirtualCamera vcam, int priority)
        {
            _playerCamera = camera;
            Vcam = vcam;
            SetPriority(priority);
        }
        
        public void SetPriority(int priority) => Vcam.Priority = priority;
        
        public Camera GetCamera() => _playerCamera;
    }
}