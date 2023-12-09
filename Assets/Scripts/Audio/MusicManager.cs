using System.Linq;
using DG.Tweening;
using Enums;
using Events;
using UnityEngine;

namespace Audio
{
    public class MusicManager : SingletonPersistent<MusicManager>
    {
        [SerializeField] [ReadOnly] private AudioSource[] audioSources;
        [SerializeField] private AudioSource currentSource;
        [SerializeField] private AudioClip currentClip;

        [SerializeField]
        private float fadeDuration = 1f;
        [Range(0, 1)] [SerializeField]
        private float maxVolume = 0.1f;

        private const float VOL_MIN = 0f;

        public override void Awake()
        {
            base.Awake();
            audioSources = GetComponents<AudioSource>();
            if (audioSources is { Length: > 0 })
            {
                currentSource = audioSources[0];
            }
        }

        private void OnEnable()
        {
            GameEvents.OnMusicChangedEvent += ChangeMusic;
            GameEvents.OnMusicStoppedEvent += StopMusic;
            GameEvents.OnMusicStartedEvent += StartMusic;
        }

        private void OnDisable()
        {
            GameEvents.OnMusicChangedEvent -= ChangeMusic;
            GameEvents.OnMusicStoppedEvent -= StopMusic;
            GameEvents.OnMusicStartedEvent -= StartMusic;
        }
        
        private void CrossFade(AudioClip clip, float duration = 1f)
        {
            AudioSource activeSource = audioSources.FirstOrDefault(source => source.isPlaying);
            if (activeSource != null)
            {
                activeSource.DOFade(VOL_MIN, duration).OnComplete(() =>
                {
                    activeSource.Stop();
                    activeSource.clip = clip;
                    activeSource.Play();
                    activeSource.DOFade(maxVolume, duration);
                    currentSource = activeSource;
                });
            }
        }

        private void StartMusic()
        {
            if (currentSource == null) return;
            currentSource.Play();
            currentSource.DOFade(maxVolume, fadeDuration);
        }

        private void StopMusic()
        {
            currentSource.DOFade(VOL_MIN, fadeDuration).OnComplete(() =>
            {
                currentSource.Stop();
            });
        }

        private void ChangeMusic(AudioClip clip, TrackPlayMode playMode = TrackPlayMode.PlayOnce)
        {
            if (currentClip == clip || clip == null) return;

            if (!CheckAnySourcePlaying())
            {
                SetCurrentClipAndPlay(clip);
                SetPlayMode(playMode);
            }
            else
            {
                CrossFade(clip, fadeDuration);
                SetPlayMode(playMode);
            }
        }

        private void SetPlayMode(TrackPlayMode playMode)
        {
            currentSource.loop = playMode == TrackPlayMode.Loop;
        }

        private void SetCurrentClipAndPlay(AudioClip clip)
        {
            currentClip = clip;
            currentSource.clip = clip;
            currentSource.volume = VOL_MIN;
            currentSource.Play();
            currentSource.DOFade(maxVolume, fadeDuration);
        }

        private bool CheckAnySourcePlaying() => audioSources.Any(source => source.isPlaying);
    }
}
