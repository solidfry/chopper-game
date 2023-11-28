using Enums;
using UnityEngine;

namespace Effects
{
    public class LightFlicker : MonoBehaviour
    {
// Properties
        public float startValue = 0.0f; // start
        public float amplitude = 1.0f; // amplitude of the wave
        public float phase = 0.0f; // start point inside on wave cycle
        public float frequency = 0.5f; // cycle frequency per second

        private Light _lightToEffect;
// Keep a copy of the original color
        private Color originalColor;
    
        [SerializeField] AnimationFunctions animationFunction;
 
// Store the original color
        void Start (){
            _lightToEffect = GetComponent<Light>();
            originalColor = _lightToEffect.color;
        }
 
        void Update (){
            _lightToEffect.color = originalColor * (EvalWave());
        }
 
        float EvalWave (){
            float x = (Time.time + phase)*frequency;
            float y;
 
            x = x - Mathf.Floor(x); // normalized value (0..1)
 
            switch (animationFunction)
            {
                case AnimationFunctions.Sin:
                    y = Mathf.Sin(x*2*Mathf.PI);
                    break;
                case AnimationFunctions.Cos:
                    y = Mathf.Cos(x*2*Mathf.PI);
                    break;
                case AnimationFunctions.Tan:
                    y = Mathf.Tan(x*2*Mathf.PI);
                    break;
                case AnimationFunctions.Triangle:
                    if (x < 0.5f)
                        y = 4.0f * x - 1.0f;
                    else
                        y = -4.0f * x + 3.0f;  
                    break;
                case AnimationFunctions.Square:
                    if (x < 0.5f)
                        y = 1.0f;
                    else
                        y = -1.0f;  
                    break;
                case AnimationFunctions.Sawtooth:
                    y = x;
                    break;
                case AnimationFunctions.InvertedSawtooth:
                    y = 1.0f - x;
                    break;
                case AnimationFunctions.Noise:
                    y = 1 - (Random.value*2);
                    break;
                default:
                    y = 1.0f;
                    break;
            }
        
            return (y * amplitude) + startValue;     
        }
    }
}