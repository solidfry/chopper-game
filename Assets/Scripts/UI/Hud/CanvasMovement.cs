using UnityEngine;

namespace UI.GameUI
{
    public class CanvasMovement : MonoBehaviour
    {
        [SerializeField] private GameObject helicopter; // Reference to the helicopter GameObject
        private RectTransform canvasRectTransform;
        private Vector2 originalPosition;
        [SerializeField] private float maxPixelMovement = 24f; // Maximum movement in pixels

        void Start()
        {
            canvasRectTransform = GetComponent<RectTransform>();
            originalPosition = canvasRectTransform.anchoredPosition;
        }

        void Update()
        {
            ApplyRotationOffset();
        }

        private void ApplyRotationOffset()
        {
            if (helicopter == null) return;

            // Get the helicopter's rotation around the Y-axis
            float rotationY = helicopter.transform.eulerAngles.y;

            // Translate this rotation into an offset for the canvas
            float offset = Mathf.Sin(Mathf.Deg2Rad * rotationY) * maxPixelMovement;

            // Update the canvas' position based on this offset
            Vector2 newPosition = originalPosition - new Vector2(offset, offset);
            canvasRectTransform.anchoredPosition = newPosition;
        }
    }
}