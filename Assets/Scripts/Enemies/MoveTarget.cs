using UnityEngine;
using DG.Tweening;
using Enums;

namespace Enemies
{
    public class MoveTarget : MonoBehaviour
    {
        [SerializeField] private MovementPlane movementPlane;
        [SerializeField] private float duration = 1f;
        [SerializeField] private Ease ease = Ease.InOutSine;
        [SerializeField] private float distance = 1f;

        private void OnDisable()
        {
            transform.DOKill();
        }

        private void Start()
        {
            DoMovement();
        }

        void DoMovement()
        {
            switch (movementPlane)
            {
                case MovementPlane.X:
                    transform.DOMoveX( distance, duration).SetRelative().SetLoops(-1, LoopType.Yoyo).SetEase(ease);
                    break;
                case MovementPlane.Y:
                    transform.DOMoveY( distance, duration).SetRelative().SetLoops(-1, LoopType.Yoyo).SetEase(ease);
                    break;
                case MovementPlane.Z:
                    transform.DOMoveZ( distance, duration).SetRelative().SetLoops(-1, LoopType.Yoyo).SetEase(ease);
                    break;
            }
        }
        
       
    }
}
