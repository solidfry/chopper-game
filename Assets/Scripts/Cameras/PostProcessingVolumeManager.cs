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
    }
    
    [ContextMenu("OutOfBounds")]
    void OutOfBounds()
    {
        _mainVolumeTween.PlayBackwards();
        _outOfBoundsVolumeTween.PlayForward();
    }
    
}
