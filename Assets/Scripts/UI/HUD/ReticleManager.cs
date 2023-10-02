using UnityEngine;

namespace UI.HUD
{
    public class ReticleManager : MonoBehaviour
    {
        [SerializeField] GameObject objectToFollow;
        [SerializeField] private Camera cam;
        private RectTransform _rectTransform; 
        
        private void Start()
        {
            _rectTransform = GetComponent<RectTransform>(); 
        }
        
        private void Update()
        {
            if(cam != null)
                UpdateReticle(objectToFollow.transform.position);
        }

        private void UpdateReticle(Vector3 obj)
        {
            var screenPoint = WorldPointToScreenPoint(obj);
            _rectTransform.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y); 
        }
        
        private Vector3 WorldPointToScreenPoint(Vector3 worldPoint)
        {
            var screenW = Screen.width / 2;
            var screenH = Screen.height / 2;
            var screenPoint = cam.WorldToScreenPoint(worldPoint);
            return new Vector3(screenPoint.x - screenW, screenPoint.y - screenH, screenPoint.z); // consider screen center as origin
        }
    }
}