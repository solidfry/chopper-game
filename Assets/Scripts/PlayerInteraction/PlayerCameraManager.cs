using System;
using Cinemachine;
using UnityEngine;

namespace PlayerInteraction
{
    [Serializable]
    public class PlayerCameraManager
    {
        [field:SerializeField] public CinemachineVirtualCamera Vcam { get; private set; }
        
        public void Initialize(CinemachineVirtualCamera vcam, int priority)
        {
            Vcam = vcam;
            SetPriority(priority);
        }
        
        public void SetPriority(int priority) => Vcam.Priority = priority;
    }
}