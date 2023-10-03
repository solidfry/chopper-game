using System;
using Cinemachine;
using UnityEngine;

namespace Player
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