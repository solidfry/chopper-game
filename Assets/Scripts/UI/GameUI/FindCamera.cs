using UnityEngine;

namespace UI.GameUI
{
    public class FindCamera : MonoBehaviour
    {
        Camera cam;
        Canvas canvas;

        private void Start()
        {
            if(TryGetComponent(out Canvas c))
            {
                canvas = c;
                if (c.renderMode != RenderMode.ScreenSpaceCamera) return;

                cam = Camera.main;
                canvas.worldCamera = cam;
            }
        }
    }
}
