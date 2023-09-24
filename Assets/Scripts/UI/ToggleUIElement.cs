// using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UI
{
    public class ToggleUIElement : MonoBehaviour
    {
        [SerializeField] GameObject uiPrefab;
        [SerializeField] InputAction button;
        // [SerializeField] private float animationDuration = .2f;
        // [SerializeField] private float scaleFactor = 1.1f;
        // private Tween _fadeIn;
        // private Tween _fadeOut;
        // private Tween _scale;
        // private Tween _resetScale;
        // private Tween _fadeAndScale;

        private void OnEnable()
        {
            button.Enable();
            button.performed += Toggle;
        }

        private void OnDisable()
        {
            button.Disable();
            button.performed -= Toggle;
            // DOTween.KillAll();
        }

        void Toggle(InputAction.CallbackContext context)
        {
            if (uiPrefab.activeInHierarchy)
            {
                // _fadeIn = uiPrefab.GetComponent<CanvasGroup>().DOFade(0, animationDuration).SetUpdate(true);
                // _scale = uiPrefab.transform.DOScale(Vector3.one / scaleFactor, animationDuration).SetUpdate(true);
                // _fadeAndScale = DOTween.Sequence().Append(_fadeIn).Join(_scale).SetUpdate(true);
                // _fadeAndScale.OnComplete(() => { uiPrefab.SetActive(false); });
            }
            else
            {
                // uiPrefab.transform.localScale = Vector3.one / scaleFactor;
                // uiPrefab.GetComponent<CanvasGroup>().DOFade(1, animationDuration).SetUpdate(true);
                // _fadeOut = uiPrefab.transform.DOScale(Vector3.one, animationDuration).SetUpdate(true);
                // uiPrefab.SetActive(true);
            }
        }

    }
}