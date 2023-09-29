using Events;
using UnityEngine;

namespace UI.GameUI
{
    public class FindCamera : MonoBehaviour
    {
        [SerializeField][ReadOnly] Camera cam;
        [SerializeField][ReadOnly] Canvas canvas;

        private void Awake()
        {
            if (TryGetComponent(out Canvas c))
            {
                canvas = c;
                if (c.renderMode != RenderMode.ScreenSpaceCamera) return;

                cam = GameObject.FindGameObjectWithTag("MainCamera").GetComponent<Camera>();
                canvas.worldCamera = cam;
            }
        }

    }
}
