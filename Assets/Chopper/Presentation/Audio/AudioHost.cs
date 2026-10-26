using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Scene owner of the <see cref="IAudioService"/>. ECS presentation systems and UI find it in the scene.
    /// TODO: register <see cref="AudioService"/> in the root <c>LifetimeScope</c> (VContainer) so music carries on
    /// across scene loads.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioHost : MonoBehaviour
    {
        [SerializeField] private SoundLibrary library;
        [SerializeField, Tooltip("One-shot sounds that can play at once.")] private int voices = 32;

        private AudioService _service;

        public SoundLibrary Library => library;
        public IAudioService Audio => _service ??= new AudioService(library, transform, voices);

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void SetLibrary(SoundLibrary value) => library = value;

        // The mixer ignores SetFloat during Awake.
        private void Start() => ((AudioService)Audio).ApplySavedVolumes();

        private void Update() => _service?.Tick(Time.unscaledDeltaTime);

        private void OnDestroy() => _service?.Dispose();
    }
}
