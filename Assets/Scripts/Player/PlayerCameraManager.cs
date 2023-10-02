using System;
using Cinemachine;

namespace Player
{
    [Serializable]
    public class PlayerCameraManager
    {
        public CinemachineVirtualCamera Vcam { get; private set; }
        
        public void Initialize(CinemachineVirtualCamera vcam, int priority)
        {
            Vcam = vcam;
            SetPriority(priority);
        }
        
        public void SetPriority(int priority) => Vcam.Priority = priority;
    }
}