using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace UI.Hud
{
    [RequireComponent(typeof(Rigidbody))]
    public class OutputHudValues : MonoBehaviour
    {
        [SerializeField] LayerMask ignorePlayers;
        [SerializeField] float altitudeCheckRange;
        Rigidbody _rb;
        [SerializeField] [ReadOnly] float speed, speedKmh, altitudeInMetres;
        int _frameCount = 0;  // Frames counter
        public event Action<int> OnSpeedChanged;
        public event Action<int> OnAltitudeChanged;
        
        private void Start() => _rb = GetComponent<Rigidbody>();

        // Update is called once per frame
        void LateUpdate()
        {
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
                _frameCount = 0; // Reset frame counter
            }
        }

        private void UpdateAltitude()
        {
            // altitudeInMetres = Physics.Raycast(_rb.position, Vector3.down * altitudeCheckRange, out var hit)
            //     ? hit.distance
            //     : 0;
            
            // Raycast to the ground
            if (Physics.Raycast(new Ray(_rb.position, Vector3.down), out RaycastHit hit, altitudeCheckRange, ~ignorePlayers))
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
            speed = Vector3.Dot(_rb.velocity, transform.forward);
            speed = Mathf.Max(0, speed);
            speedKmh = speed * 3600 / 1000;
        }
        
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(_rb.position, Vector3.down * altitudeCheckRange);
        }
    }
}
