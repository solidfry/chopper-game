using System.Collections;
using Cinemachine;
using Enums;
using Events;
using UnityEngine;

namespace Cameras
{
    public class CameraShakeManager : MonoBehaviour
    {
        [SerializeField] private CinemachineVirtualCamera cam;
        [SerializeField] private CinemachineBasicMultiChannelPerlin noise;
        
        float _defaultAmplitude, _defaultFrequency;
        
        private void Start()
        {
            CheckCamera();
            CheckNoise();
            
            SetDefaultNoiseValues();

            SetCameraValues(_defaultAmplitude, _defaultFrequency, noise);
        }

        private void OnEnable() => GameEvents.onScreenShakeEvent += Shake;
        
        private void OnDisable() => GameEvents.onScreenShakeEvent -= Shake;
        
        void Shake(Strength str = Strength.Medium, float lengthInSeconds = .2f)
        {
            if (noise == null)
                noise = cam.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        
            switch (str)
            {
                case Strength.VeryLow:
                    SetCameraValues(.2f, 5f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    break;
                case Strength.Low:
                    SetCameraValues(.6f, 10f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    break;
                case Strength.Medium:
                    SetCameraValues(1.4f, 40f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    break;
                case Strength.High:
                    SetCameraValues(1.8f, 60f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    break;
                case Strength.VeryHigh:
                    SetCameraValues(2f, 100f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    break;
                default:
                    break;
            }
        }
        
        void SetCameraValues(float amplitude, float frequency, CinemachineBasicMultiChannelPerlin _noise)
        {
            _noise.m_AmplitudeGain = amplitude;
            _noise.m_FrequencyGain = frequency;
        }
        
        IEnumerator ResetCamera(float lengthInSeconds, CinemachineBasicMultiChannelPerlin _noise)
        {
            yield return new WaitForSeconds(lengthInSeconds);
            _noise.m_AmplitudeGain = _defaultAmplitude;
            _noise.m_FrequencyGain = _defaultFrequency;
        }

        private void CheckNoise() => noise = noise == null ? cam.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>() : noise;

        private void CheckCamera() => cam = cam == null ? GetComponent<CinemachineVirtualCamera>() : cam;
        
        private void SetDefaultNoiseValues()
        {
            _defaultFrequency = noise.m_FrequencyGain;
            _defaultAmplitude = noise.m_AmplitudeGain;
        }
    }
}