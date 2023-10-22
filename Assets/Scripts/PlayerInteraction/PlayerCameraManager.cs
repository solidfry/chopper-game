using System;
using Cinemachine;
using UnityEngine;

namespace PlayerInteraction
{
    [Serializable]
    public class PlayerCameraManager
    {
        [SerializeField] Camera playerCamera;
        [field:SerializeField] public CinemachineVirtualCamera Vcam { get; private set; }
        
        public void Initialize(Camera camera, CinemachineVirtualCamera vcam, int priority)
        {
            playerCamera = camera;
            Vcam = vcam;
            SetPriority(priority);
        }
        
        public void SetPriority(int priority) => Vcam.Priority = priority;
        
        public Camera GetCamera() => playerCamera;
    }
}