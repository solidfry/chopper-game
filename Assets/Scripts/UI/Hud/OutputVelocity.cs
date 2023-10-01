using System;
using UnityEngine;

namespace UI.Hud
{
    [RequireComponent(typeof(Rigidbody))]
    public class OutputVelocity : MonoBehaviour
    {
        Rigidbody rb;
        [SerializeField] [ReadOnly] float speed, speedKmh;
        int frameCount = 0;  // Frames counter
        public event Action<float> OnSpeedChanged;
        private void Start()
        {
            rb = GetComponent<Rigidbody>();
        }

        // Update is called once per frame
        void LateUpdate()
        {
            speed = Vector3.Dot(rb.velocity, transform.forward);
            speed = Mathf.Max(0, speed);
        
            speedKmh = speed * 3600 / 1000;

            frameCount++;  // Increment frame counter

            if (frameCount >= 60)  // Check if 60 frames have passed
            {
                // Log the speeds
                Debug.Log("Speed in m/s: " + speed);
                Debug.Log("Speed in km/h: " + speedKmh);
                OnSpeedChanged?.Invoke(speedKmh);
                frameCount = 0;  // Reset frame counter
            }
        }
    }
}