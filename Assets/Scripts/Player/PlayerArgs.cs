using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Player
{
    [Serializable]
    public struct PlayerArgs
    {
        public Transform transform;
        public Rigidbody rigidbody;
        public InputManager inputManager;
        public MovementController movementController;

        public PlayerArgs(Transform _transform, Rigidbody _rigidbody, InputManager _inputManager, MovementController _movementController)
        {
            transform = _transform;
            rigidbody = _rigidbody;
            inputManager = _inputManager;
            movementController = _movementController;
        }
      
    }
}