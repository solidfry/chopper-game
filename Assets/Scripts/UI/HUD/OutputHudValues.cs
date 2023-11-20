using System;
using Interactions;
using PlayerInteraction;
using PlayerInteraction.Networking;
using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

namespace UI.Hud
{
    [RequireComponent(typeof(Rigidbody))]
    public class OutputHudValues : MonoBehaviour
    {
        [SerializeField] LayerMask altitudeRaycastIgnore;
        [SerializeField] float altitudeCheckRange;
        [field: SerializeField] public Rigidbody Rigidbody { get; private set; }
        [SerializeField] [ReadOnly] private float speed, speedKmh; 
        [field:SerializeField] public float AltitudeInMetres { get; private set; }
        [SerializeField] NetworkHealth health;
        VehicleStabiliser _stabiliser;
        
        int _frameCount = 0;
        bool _isInitialised;
        
        public event Action<int> OnSpeedChanged;
        public event Action<int> OnAltitudeChanged;
        public event Action<bool> OnStabiliserActive;
        public event Action<int> OnUpdateHealthEvent;
        
        public void Initialise()
        {
            Debug.Log("HudValues Init");
            Rigidbody = GetComponent<Rigidbody>();
            enabled = true;
            _isInitialised = true;
            _stabiliser = GetComponent<PlayerManager>().MovementController.GetStabiliser();
            
            if(health == null)
                health = GetComponent<NetworkHealth>();
            
            health.SendHealthEvent += SendHealth;
        }
        
        void LateUpdate()
        {
            if (!_isInitialised) return;
            UpdateSpeed();
            UpdateAltitude();
            DelayEvents();
        }
        
        void SendHealth (int healthValue)
        {
            OnUpdateHealthEvent?.Invoke(healthValue);
        }

        private void DelayEvents()
        {
            _frameCount++; // Increment frame counter

            if (_frameCount >= 60) // Check if 60 frames have passed
            {
                OnSpeedChanged?.Invoke(Mathf.FloorToInt(speedKmh));
                OnAltitudeChanged?.Invoke(Mathf.FloorToInt(AltitudeInMetres));
                OnStabiliserActive?.Invoke(_stabiliser.IsActive);
                _frameCount = 0; // Reset frame counter
            }
        }

        private void UpdateAltitude()
        {    
            // Raycast to the ground
            if (Physics.Raycast(new Ray(Rigidbody.position, Vector3.down), out RaycastHit hit, altitudeCheckRange, ~altitudeRaycastIgnore))
            {
                AltitudeInMetres = hit.distance;
            }
            else
            {
                AltitudeInMetres = 0;
            }
        }

        private void UpdateSpeed()
        {
            speed = Rigidbody.velocity.magnitude;
            speed = Mathf.Max(0, speed);
            speedKmh = Speed.MetersPerSecondToKilometersPerHour(speed);
        }

        private void OnDestroy()
        {
            if(health != null)
                health.SendHealthEvent -= SendHealth;
        }

        // private void OnDrawGizmos()
        // {
        //     Gizmos.color = Color.red;
        //     Gizmos.DrawRay(_rb.position, Vector3.down * altitudeCheckRange);
        // }
    }
}
