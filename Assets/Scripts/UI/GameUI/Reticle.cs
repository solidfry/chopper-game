using UnityEngine;

namespace UI.GameUI
{
    public class Reticle : MonoBehaviour
    {
        [SerializeField] GameObject objectToFollow;
        private Camera cam;
        
        private void Start()
        {
            cam = Camera.main;
        }
        

        private void Update()
        {
            if(cam != null)
                UpdateReticle(objectToFollow.transform.position);
        }

        private void UpdateReticle(Vector3 obj)
        {
            var screenPoint = WorldpointToScreenpoint(obj);
            transform.position = new Vector3(screenPoint.x, screenPoint.y, 0);
        }
        
        private Vector3 WorldpointToScreenpoint(Vector3 worldPoint)
        {
            var screenPoint = cam.WorldToScreenPoint(worldPoint);
            screenPoint.z = 0;
            return screenPoint;
        }
        
    }
}
