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
        [SerializeField, Tooltip("Music for this scene. Empty = the config's match music.")] private AudioClip musicOverride;

        private PresentationContext _context;

        public GameConfig Config => config;

        /// <summary>Made on first use, so UI that enables before this object still gets it.</summary>
        public PresentationContext Context => _context ??= new PresentationContext(config, transform, cameraRig);

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Configure(GameConfig gameConfig, ChopperCameraRig rig)
        {
            config = gameConfig;
            cameraRig = rig;
        }

        private void Start() => Context.Start(musicOverride);

        private void Update() => _context?.Tick(Time.deltaTime, Time.unscaledDeltaTime);

        private void OnDestroy() => _context?.Dispose();
    }
}
