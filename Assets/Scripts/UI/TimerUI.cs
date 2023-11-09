using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Utilities;

namespace UI
{
    public class TimerUI : MonoBehaviour
    {
        [SerializeField] bool runTimer;
        [Header("Text Settings")]
        [SerializeField] TMP_Text timerText;
        [SerializeField][Range(0,1)] float monoSpace = 0.8f;
        
        [Header("Background Settings")]
        [SerializeField] Image backgroundImage;
        
        [Header("Color Settings")]
        [SerializeField] Color defaultTextColor;
        [SerializeField] Color defaultBackgroundColor;
        
        [FormerlySerializedAs("animationDuration")]
        [Header("Animation Settings")]
        [SerializeField] float animationInOutDuration = 1f;
        [SerializeField] float colorSwapDuration = .5f;
        [SerializeField] Ease easing;
        
        private float _currentTimeRemaining;
        RectTransform _rectTransform;
        CanvasGroup _canvasGroup;
        private float _height;
        
        private Sequence HideTimerSequence => DOTween.Sequence()
            .Append(transform.DOMoveY(-_height, animationInOutDuration))
            .Join(_canvasGroup.DOFade(0, animationInOutDuration));
        private Sequence ShowTimerSequence => DOTween.Sequence()
            .Append(transform.DOMoveY(0, animationInOutDuration))
            .Join(_canvasGroup.DOFade(1, animationInOutDuration));

        private Tween _bgColorSequence;
        private Tween _textColorSequence;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _height = _rectTransform.rect.height;
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public void StartTimer()
        {
            runTimer = true;
        }
        
        void FormatTimer(float time)
        {
            int minutes = Mathf.FloorToInt(time / 60F);
            int seconds = Mathf.FloorToInt(time - minutes * 60);
            timerText.text = $"<mspace={monoSpace}em>{minutes:00}</mspace>:<mspace={monoSpace}em>{seconds:00}</mspace>";
        }

        public void SetTimer(float time)
        {
            _currentTimeRemaining = time;
            FormatTimer(_currentTimeRemaining);
        }
        
        public void SetColors(string textColor, string backgroundColor)
        {
            SetTextColor(textColor);
            SetBackgroundColor(backgroundColor);
        }
        
        void SetTextColor(string color)
        { 
            Color c = ColourManager.Instance ? ColourManager.Instance.UIColours.GetColourByName(color) : defaultTextColor;
            _textColorSequence = timerText.DOColor(c, colorSwapDuration).SetEase(easing);
            _textColorSequence.Play();
        }

        void SetBackgroundColor(string color)
        {
            Color c = ColourManager.Instance ? ColourManager.Instance.UIColours.GetColourByName(color) : defaultBackgroundColor;
            _bgColorSequence = backgroundImage.DOColor(c, colorSwapDuration).SetEase(easing);
            _bgColorSequence.Play();
        }

        [ContextMenu("Hide Timer")]
        public void HideTimer() => HideTimerSequence.Play().SetEase(easing);

        [ContextMenu("Show Timer")]
        public void ShowTimer() => ShowTimerSequence.Play().SetEase(easing);

        private void OnDestroy()
        {
            _textColorSequence.Kill();
            _bgColorSequence.Kill();
        }
    }
}