using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace UI.HUD
{
    [Serializable]
    public class Reticle
    {
        [SerializeField] GameObject objectToFollow;
        [SerializeField] private Camera cam;
        [SerializeField ]private RectTransform reticleObject; 
        
        public void OnStart(Camera camera) => cam = camera;

        public void OnUpdate()
        {
            if(cam != null)
                UpdateReticle(objectToFollow.transform.position);
        }

        private void UpdateReticle(Vector3 obj)
        {
            var screenPoint = WorldPointToScreenPoint(obj);
            reticleObject.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y); 
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