using System;
using UnityEngine;

namespace Player
{
    [Serializable]
    public struct PlayerArgs
    {
        public Transform transform;
        public Rigidbody rigidbody;
        public InputManager inputManager;
        public ChopperController chopperController;
    }
}