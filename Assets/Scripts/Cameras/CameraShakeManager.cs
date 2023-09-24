using System.Collections;
using Cinemachine;
using Enums;
using Events;
using UnityEngine;

namespace Cameras
{
    public class CameraShakeManager : MonoBehaviour
    {
        [SerializeField] CinemachineVirtualCamera cam;
        [SerializeField] private CinemachineBasicMultiChannelPerlin noise;
        
        private void Start()
        {
            cam = FindObjectOfType<CinemachineVirtualCamera>();
            noise = cam.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
            SetCameraValues(0, 0, noise);
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
                    // Debug.Log("Very Low");
                    break;
                case Strength.Low:
                    SetCameraValues(.6f, 10f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    // Debug.Log("Low");
                    break;
                case Strength.Medium:
                    SetCameraValues(1.4f, 40f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    Debug.Log("Medium");
                    break;
                case Strength.High:
                    SetCameraValues(1.8f, 60f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    // Debug.Log("High");
                    break;
                case Strength.VeryHigh:
                    SetCameraValues(2f, 100f, noise);
                    StartCoroutine(ResetCamera(lengthInSeconds, noise));
                    // Debug.Log("Very High");
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
            _noise.m_AmplitudeGain = 0;
            _noise.m_FrequencyGain = 0;
        }
        


    }
}