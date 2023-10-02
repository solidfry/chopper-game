using UnityEngine;

namespace UI.HUD
{
    public class CanvasMovement : MonoBehaviour
    {
        [SerializeField] private GameObject helicopter; // Reference to the helicopter GameObject
        [SerializeField] private float maxPixelMovement = 24f; // Maximum movement in pixels
        private RectTransform _canvasRectTransform;
        private Vector2 _originalPosition;

        void Start()
        {
            if (helicopter == null) return;

            _canvasRectTransform = GetComponent<RectTransform>();
            _originalPosition = _canvasRectTransform.anchoredPosition;
        }

        void Update()
        {
            if (helicopter == null) return;

            ApplyRotationOffset();
        }

        private void ApplyRotationOffset()
        {
            // Get the helicopter's rotation around the Y-axis
            float rotationY = helicopter.transform.eulerAngles.y;

            // Translate this rotation into an offset for the canvas
            float offset = Mathf.Sin(Mathf.Deg2Rad * rotationY) * maxPixelMovement;

            // Update the canvas' position based on this offset
            Vector2 newPosition = _originalPosition - new Vector2(offset, offset);
            _canvasRectTransform.anchoredPosition = newPosition;
        }
    }
}