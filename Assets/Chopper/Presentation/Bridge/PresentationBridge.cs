using Chopper.Presentation.Audio;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using UnityEngine;

namespace Chopper.Presentation.Bridge
{
    /// <summary>
    /// The one scene object between ECS and presentation. Holds the <see cref="GameConfig"/> and owns the
    /// <see cref="PresentationContext"/>. <see cref="PresentationBridgeSystem"/> hands the context to the client world.
    /// Set up by Chopper → Setup.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class PresentationBridge : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField, Tooltip("Chase camera. Optional.")] private ChopperCameraRig cameraRig;
        [SerializeField, Tooltip("Music for this scene: the match playlist or the menu identity track (GameConfig → Music).")]
        private MusicLibrary.Set music = MusicLibrary.Set.Match;
        [SerializeField, Tooltip("One track for this scene instead. Empty = the set above.")] private AudioClip musicOverride;

        private PresentationContext _context;

        public GameConfig Config => config;
        public ChopperCameraRig CameraRig => cameraRig;

        /// <summary>Made on first use, so UI that enables before this object still gets it.</summary>
        public PresentationContext Context => _context ??= new PresentationContext(config, transform, cameraRig);

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Configure(GameConfig gameConfig, ChopperCameraRig rig)
        {
            config = gameConfig;
            cameraRig = rig;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetMusic(MusicLibrary.Set set) => music = set;

        private void Start() => Context.Start(music, musicOverride);

        private void Update() => _context?.Tick(Time.deltaTime, Time.unscaledDeltaTime);

        private void OnDestroy() => _context?.Dispose();
    }
}
