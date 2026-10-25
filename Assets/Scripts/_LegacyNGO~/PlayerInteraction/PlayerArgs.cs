using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayerInteraction
{
    [Serializable]
    public struct PlayerArgs
    {
        public Vector3 position;
        public Vector3 rotation;
        // public Rigidbody rigidbody;
        // [FormerlySerializedAs("rawInputManager")] [FormerlySerializedAs("inputManager")] public InputController inputController;
        // public MovementController movementController;

        public PlayerArgs(Vector3 _position, Vector3 _rotation)
        {
            position = _position;
            rotation = _rotation;
            // rigidbody = _rigidbody;
        }
      
    }
}