using UnityEngine;

public class WorldPlane : MonoBehaviour
{
    public Transform helicopterTransform; // Reference to the helicopter's transform
    public RectTransform lineRectTransform; // Reference to the UI line's RectTransform

    private Vector3 initialPosition; // Store the initial position of the UI line
    private float canvasHeight; // The height of the canvas

    // Start is called before the first frame update
    void Start()
    {
        initialPosition = lineRectTransform.localPosition;
        canvasHeight = lineRectTransform.GetComponentInParent<Canvas>().pixelRect.height;
    }

    // Update is called once per frame
    void Update()
    {
        // Get the helicopter's rotation in Euler angles
        Vector3 eulerRotation = helicopterTransform.rotation.eulerAngles;

        // For the pitch, keep values between -180 and 180
        float pitch = eulerRotation.x > 180 ? eulerRotation.x - 360 : eulerRotation.x;

        // For the roll, keep values between -180 and 180
        float roll = eulerRotation.z > 180 ? eulerRotation.z - 360 : eulerRotation.z;

        // Calculate the new position based on the pitch
        float newPositionY = initialPosition.y + pitch * (canvasHeight / 180f);

        // Update the UI line's position
        lineRectTransform.localPosition = new Vector3(initialPosition.x, newPositionY, initialPosition.z);

        // Update the UI line's rotation to counter the roll
        lineRectTransform.localRotation = Quaternion.Euler(0, 0, -roll);
    }
}