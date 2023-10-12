using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace PlayerInteraction
{
    [Serializable]
    public struct PlayerArgs
    {
        public Transform transform;
        public Rigidbody rigidbody;
        [FormerlySerializedAs("rawInputManager")] [FormerlySerializedAs("inputManager")] public InputController inputController;
        public MovementController movementController;

        public PlayerArgs(Transform _transform, Rigidbody _rigidbody, InputController inputController, MovementController _movementController)
        {
            transform = _transform;
            rigidbody = _rigidbody;
            this.inputController = inputController;
            movementController = _movementController;
        }
      
    }
}