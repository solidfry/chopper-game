using UnityEngine;

namespace UI.HUD
{
    public class WorldPlane : MonoBehaviour
    {
        public Transform helicopterTransform; // Reference to the helicopter's transform
        public RectTransform lineRectTransform; // Reference to the UI line's RectTransform

        private Vector3 _initialPosition; // Store the initial position of the UI line
        private float _canvasHeight; // The height of the canvas

        // Start is called before the first frame update
        void Start()
        {
            _initialPosition = lineRectTransform.localPosition;
            _canvasHeight = lineRectTransform.GetComponentInParent<Canvas>().pixelRect.height;
        }

        // Update is called once per frame
        void Update()
        {
            // Get the helicopter's rotation in Euler angles
            Vector3 eulerRotation = helicopterTransform.rotation.eulerAngles;
            
            float roll = eulerRotation.z > 180 ? eulerRotation.z - 360 : eulerRotation.z;
            
            lineRectTransform.localRotation = Quaternion.Euler(0, 0, -roll);
        }
    }
}