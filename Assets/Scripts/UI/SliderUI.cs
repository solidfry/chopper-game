using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class SliderUI : MonoBehaviour
    {

        [SerializeField] private Slider slider;
        [SerializeField] private float updateSpeedSeconds = 0.5f;
        
        Coroutine _sliderAnimationCoroutine;
        private float oldValue;
        private float deltaTime;

        private void Awake()
        {
            if (slider == null)
            {
                slider = GetComponentInChildren<Slider>();
            }

        }
        private void Start()
        {
            deltaTime = Time.deltaTime;
        }

        protected void ChangeSlider(float normalisedValue)
        {
            if (Math.Abs(normalisedValue - oldValue) < Mathf.Epsilon)
            {
                return;
            }

            oldValue = normalisedValue;
            _sliderAnimationCoroutine = StartCoroutine(AnimateSlider(normalisedValue));
        }

        private IEnumerator AnimateSlider(float normalisedValue)
        {
            float preChangedPercent = slider.value;
            float normalisedValueFloat = normalisedValue;
            float elapsed = 0f;
            while (elapsed < updateSpeedSeconds)
            {
                //elapsed += Time.deltaTime; (Had to change this - slow mo messed with it)
                elapsed += deltaTime;
                slider.value = Mathf.Lerp(preChangedPercent, normalisedValueFloat, elapsed / updateSpeedSeconds);
                yield return null;
            }
            slider.value = normalisedValueFloat;
        }
    }
}
