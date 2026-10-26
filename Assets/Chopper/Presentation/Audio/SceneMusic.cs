using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>Plays a music track when the scene starts (replaces the old <c>MusicRequester</c>).</summary>
    public sealed class SceneMusic : MonoBehaviour
    {
        [SerializeField] private AudioClip track;
        [SerializeField] private float fadeSeconds = 2f;

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void SetTrack(AudioClip clip) => track = clip;

        private void Start()
        {
            var host = FindFirstObjectByType<AudioHost>();
            if (host != null) host.Audio.PlayMusic(track, fadeSeconds);
        }
    }
}
