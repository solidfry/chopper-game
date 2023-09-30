using UnityEngine;

namespace UI.Hud
{
    public class Reticle : MonoBehaviour
    {
        [SerializeField] GameObject objectToFollow;
        private Camera cam;
        private RectTransform rectTransform; 
        
        private void Start()
        {
            cam = Camera.main;
            rectTransform = GetComponent<RectTransform>(); 
        }
        
        private void Update()
        {
            if(cam != null)
                UpdateReticle(objectToFollow.transform.position);
        }

        private void UpdateReticle(Vector3 obj)
        {
            var screenPoint = WorldpointToScreenpoint(obj);
            rectTransform.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y); 
        }
        
        private Vector3 WorldpointToScreenpoint(Vector3 worldPoint)
        {
            var screenW = Screen.width / 2;
            var screenH = Screen.height / 2;
            var screenPoint = cam.WorldToScreenPoint(worldPoint);
            return new Vector3(screenPoint.x - screenW, screenPoint.y - screenH, screenPoint.z); // consider screen center as origin
        }
    }
}