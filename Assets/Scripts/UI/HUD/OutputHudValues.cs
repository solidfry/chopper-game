using System;
using PlayerInteraction;
using PlayerInteraction.Networking;
using UnityEngine;
using Utilities;

namespace UI.Hud
{
    [RequireComponent(typeof(Rigidbody))]
    public class OutputHudValues : MonoBehaviour
    {
        [SerializeField] LayerMask altitudeRaycastIgnore;
        [SerializeField] float altitudeCheckRange;
        Rigidbody _rigidbody;
        [SerializeField] [ReadOnly] float speed, speedKmh, altitudeInMetres;
        [SerializeField] int _frameCount = 0;
        VehicleStabiliser _stabiliser;
        
        bool _isInitialised;
        
        public event Action<int> OnSpeedChanged;
        public event Action<int> OnAltitudeChanged;
        public event Action<bool> OnStabiliserActive;


        public void Initialise()
        {
            Debug.Log("HudValues Init");
            _rigidbody = GetComponent<Rigidbody>();
            enabled = true;
            _isInitialised = true;
            _stabiliser = GetComponent<PlayerManager>().MovementController.GetStabiliser();

        }

        // Update is called once per frame
        void LateUpdate()
        {
            if (!_isInitialised) return;
            UpdateSpeed();
            UpdateAltitude();
            DelayEvents();
        }

        private void DelayEvents()
        {
            _frameCount++; // Increment frame counter

            if (_frameCount >= 60) // Check if 60 frames have passed
            {
                OnSpeedChanged?.Invoke(Mathf.FloorToInt(speedKmh));
                OnAltitudeChanged?.Invoke(Mathf.FloorToInt(altitudeInMetres));
                OnStabiliserActive?.Invoke(_stabiliser.IsActive);
                _frameCount = 0; // Reset frame counter
            }
        }

        private void UpdateAltitude()
        {    
            // Raycast to the ground
            if (Physics.Raycast(new Ray(_rigidbody.position, Vector3.down), out RaycastHit hit, altitudeCheckRange, ~altitudeRaycastIgnore))
            {
                altitudeInMetres = hit.distance;
            }
            else
            {
                altitudeInMetres = 0;
            }
        }

        private void UpdateSpeed()
        {
            speed = _rigidbody.velocity.magnitude;
            speed = Mathf.Max(0, speed);
            speedKmh = Speed.MetersPerSecondToKilometersPerHour(speed);
        }
        
        // private void OnDrawGizmos()
        // {
        //     Gizmos.color = Color.red;
        //     Gizmos.DrawRay(_rb.position, Vector3.down * altitudeCheckRange);
        // }
    }
}
