using DG.Tweening;
using TMPro;
using UI.ScriptableObjects;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class TimerUI : MonoBehaviour
    {
        
        public bool RunTimer { get; private set; }
        [Header("Text Settings")]
        [SerializeField] TMP_Text timerText;
        [SerializeField][Range(0,1)] float monoSpace = 0.8f;
        
        [Header("Background Settings")]
        [SerializeField] Image backgroundImage;
        
        [Header("Color Settings")]
        [SerializeField] Color defaultTextColor;
        [SerializeField] Color defaultBackgroundColor;
        
        [Header("Animation Settings")]
        [SerializeField] float animationInOutDuration = 1f;
        [SerializeField] float colorSwapDuration = .5f;
        [SerializeField] Ease easing;
        
        [Header("Canvas Settings")]
        [SerializeField] RectTransform rectTransform;
        [SerializeField] CanvasGroup canvasGroup;
        private float _currentTimeRemaining;
        private float _height;

        private int minutes;
        private int seconds;
        
        private Sequence HideTimerSequence => DOTween.Sequence()
            .Append(transform.DOMoveY(-_height, animationInOutDuration))
            .Join(canvasGroup.DOFade(0, animationInOutDuration).From(1));
        private Sequence ShowTimerSequence => DOTween.Sequence()
            .Append(transform.DOMoveY(0, animationInOutDuration).From(-_height))
            .Join(canvasGroup.DOFade(1, animationInOutDuration).From(0));

        private Tween _bgColorSequence;
        private Tween _textColorSequence;

        private void Start()
        {
            rectTransform = GetComponent<RectTransform>();
            _height = rectTransform.rect.height;
            canvasGroup = GetComponent<CanvasGroup>();
        }

        public void StartTimerUI() => RunTimer = true;

        public void StopTimerUI() => RunTimer = false;

        void FormatTimer(float time)
        {
            minutes = Mathf.FloorToInt(time / 60F);
            seconds = Mathf.FloorToInt(time - minutes * 60);
            timerText.text = $"<mspace={monoSpace}em>{minutes:00}</mspace>:<mspace={monoSpace}em>{seconds:00}</mspace>";
        }

        public void SetTimer(float time)
        {
            _currentTimeRemaining = time;
            FormatTimer(_currentTimeRemaining);
        }
        
        public void SetColors(ColourData textColor = null)
        {
            SetTextColor(textColor);
            SetBackgroundColor(textColor);
        }
        
        void SetTextColor(ColourData color = null)
        { 
            if(color == null)
            {
                timerText.color = defaultTextColor;
                return;
            }
            
            _textColorSequence = timerText.DOColor(color.Color, colorSwapDuration).SetEase(easing);
            _textColorSequence.Play();
        }

        void SetBackgroundColor(ColourData color = null)
        {
            if(color == null)
            {
                backgroundImage.color = defaultBackgroundColor;
                return;
            }

            _bgColorSequence = backgroundImage.DOColor(color.ShadeColor, colorSwapDuration).SetEase(easing);
            _bgColorSequence.Play();
        }

        [ContextMenu("Hide Timer")]
        public void Hide() => HideTimerSequence.Play().SetEase(easing);

        [ContextMenu("Show Timer")]
        public void Show() => ShowTimerSequence.Play().SetEase(easing);

        private void OnDestroy()
        {
            _textColorSequence.Kill();
            _bgColorSequence.Kill();
        }
    }
}