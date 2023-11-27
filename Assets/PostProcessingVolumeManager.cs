using DG.Tweening;
using Events;
using UnityEngine;
using UnityEngine.Rendering;

public class PostProcessingVolumeManager : MonoBehaviour
{
    [SerializeField] private Volume mainVolume;
    [SerializeField] private Volume outOfBoundsVolume;
    
    [SerializeField] Ease easing;
    [SerializeField] float duration = 0.5f;


    private Tween _mainVolumeTween;
    private Tween _outOfBoundsVolumeTween;

    private void Start()
    {
        _mainVolumeTween =  DOTween.To( 
                () 
                    => mainVolume.weight, (x) => mainVolume.weight = x, 1, duration)
            .SetEase(easing).SetAutoKill(false).Pause();

        _outOfBoundsVolumeTween = DOTween.To(
            ()
                => outOfBoundsVolume.weight, (x) => outOfBoundsVolume.weight = x, 1, duration)
            .SetEase(easing).SetAutoKill(false).Pause();
    }

    private void OnEnable()
    {
       GameEvents.OnPlayerInBoundsEvent += ResetToMain;
       GameEvents.OnPlayerOutOfBoundsEvent += OutOfBounds;
    }

    private void OnDisable()
    {
        GameEvents.OnPlayerInBoundsEvent -= ResetToMain;
        GameEvents.OnPlayerOutOfBoundsEvent -= OutOfBounds;
    }
    
    [ContextMenu("ResetToMain")]
    void ResetToMain()
    {
        _mainVolumeTween.PlayForward();
        _outOfBoundsVolumeTween.PlayBackwards();
        // mainVolume.weight = Lerp(mainVolume.weight, 1);
        // outOfBoundsVolume.weight = Lerp(outOfBoundsVolume.weight, 0);
    }
    
    [ContextMenu("OutOfBounds")]
    void OutOfBounds()
    {
        _mainVolumeTween.PlayBackwards();
        _outOfBoundsVolumeTween.PlayForward();
        // mainVolume.weight = Lerp(mainVolume.weight, 0);
        // outOfBoundsVolume.weight = Lerp(outOfBoundsVolume.weight, 1);
    }

    // float Lerp(float value, float valueToTranslateTo)
    // {
    //     
    //     // I want to add a curve to this 
    //     return Mathf.Lerp(value, valueToTranslateTo, duration * Time.deltaTime);
    // }
    
}
