using UnityEngine;
using UnityEngine.Serialization;

namespace Player
{
    public class ChopperController : MonoBehaviour
    {
        [SerializeField] GameObject rotor;
        [SerializeField] private Rigidbody rb;
        [SerializeField] float rotorForce = 1000f;
        [SerializeField] private float yawTorque = 500f;
        [SerializeField] private float pitchTorque = 1000f;
        [SerializeField] private float rollTorque = 1000f;
        [SerializeField] private float thrustForce = 2f;

        [SerializeField] [Range(0,1)] private float upwardThrustVectorOffset = 0.5f;
        Vector3 thrustVector;
        // [SerializeField] private float constantThrust = 35f;
        private Rigidbody rotorRb;
        private InputManager inputManager;
        private Transform tr;
        private float velocityY;
        [SerializeField] private float currentSpeed;
        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            inputManager = GetComponent<InputManager>();
            tr = GetComponent<Transform>();
        }

        private void Update()
        {
            rotor.transform.RotateAround(rotor.transform.position, rotor.transform.up, rotorForce * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            currentSpeed = rb.velocity.magnitude;
        }

        public void HandleThrust()
        {
            thrustVector = (tr.up + tr.forward) * upwardThrustVectorOffset;

            if (inputManager.thrust > 0.1f)
            {
                rb.AddForce(thrustVector * (thrustForce * inputManager.thrust));
            }
            else if (inputManager.thrust < -0.1f)
            {

                rb.AddForce(Vector3.up * (thrustForce * inputManager.thrust));
            }
        }

        public void HandleYaw()
        {
            Vector3 yawAxis = new Vector3(0, inputManager.yaw * yawTorque, 0);
            rb.AddRelativeTorque(yawAxis);
        }


        public void HandleRoll()
        {
            Vector3 rollAxis = new Vector3(0, 0, -inputManager.roll * rollTorque);
            rb.AddRelativeTorque(rollAxis);
        }
        
        public void HandlePitch()
        {
            velocityY = rb.velocity.y;
            Vector3 pitchAxis = new Vector3(inputManager.pitch * pitchTorque, 0, 0);
            rb.AddRelativeTorque(pitchAxis);
        }

        private void OnDrawGizmos()
        {
            Color color;
            color = Color.green;
            // local up
            DrawHelperAtCenter(this.transform.up, Color.magenta, 2f);
         
            color.g -= 0.5f;
            // global up
            DrawHelperAtCenter(Vector3.up, color, 1f);
         
            color = Color.blue;
            // local forward
            DrawHelperAtCenter(this.transform.forward, color, 2f);
         
            color.b -= 0.5f;
            // global forward
            DrawHelperAtCenter(Vector3.forward, color, 1f);
         
            color = Color.red;
            // local right
            DrawHelperAtCenter(this.transform.right, color, 2f);
         
            color.r -= 0.5f;
            // global right
            DrawHelperAtCenter(Vector3.right, color, 1f);
        }
        private void DrawHelperAtCenter(Vector3 direction, Color color, float scale)
        {
            Gizmos.color = color;
            Vector3 destination = transform.position + direction * scale;
            Gizmos.DrawLine(transform.position, destination);
        }
    }
}
