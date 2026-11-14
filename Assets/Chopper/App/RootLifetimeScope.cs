using Chopper.Presentation.Flow;
using Chopper.Services.Auth;
using Chopper.Services.Matchmaking;
using Chopper.Services.Platform;
using UnityEngine;
using VContainer;
using VContainer.Unity;
#if CHOPPER_STEAM
using Chopper.Services.Steam;
#endif

namespace Chopper.App
{
    /// <summary>
    /// Composition root: services that live for the whole game. Put it on a prefab and set that prefab as the
    /// Root Lifetime Scope in the VContainer settings asset, so every scene's scope is a child of it.
    /// Scene scopes (menu, hangar, match) register their own screens and view models.
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        [SerializeField, Tooltip("Scene change look and timing (Setup step 1 links it). Players' builds only.")]
        private SceneTransitionSettings sceneTransitions;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetSceneTransitions(SceneTransitionSettings settings) => sceneTransitions = settings;
        public SceneTransitionSettings SceneTransitions => sceneTransitions;

        protected override void Configure(IContainerBuilder builder)
        {
#if UNITY_SERVER
            // Dedicated server: no store platform (Steam is the players' side).
            builder.Register<OfflinePlatform>(Lifetime.Singleton).As<IPlatformIdentity>();
#elif CHOPPER_STEAM
            // Entry point: VContainer runs Steam callbacks every frame and shuts Steam down on exit.
            builder.RegisterEntryPoint<SteamPlatform>();
#else
            builder.Register<OfflinePlatform>(Lifetime.Singleton).As<IPlatformIdentity>();
#endif
            builder.Register<UnityAuthService>(Lifetime.Singleton).As<IAuthService>();
            // The player's progression record on the server (written by match servers; the client only reads it).
            builder.Register<UnityProfileCloud>(Lifetime.Singleton).As<IProfileCloud>();
            // Toast notices for the whole game; the scene flow draws them, scenes post through PresentationContext.Notify.
            builder.Register<NotificationFeed>(Lifetime.Singleton);
            // Unity Matchmaker; its queue's pool allocates Edgegap servers (Cloud Code module CloudCode/ChopperAllocator).
            builder.Register<UnityMatchmakingService>(Lifetime.Singleton).As<IMatchmakingService>();

#if !UNITY_SERVER
            // A dedicated server does not sign in as a player (later: a service account for Cloud Code, Phase 3).
            builder.RegisterEntryPoint<SignInOnStart>();
            // Scene changes: music across scenes, the block transition, world sound fade.
            builder.RegisterInstance(sceneTransitions != null ? sceneTransitions : ScriptableObject.CreateInstance<SceneTransitionSettings>());
            builder.RegisterEntryPoint<SceneFlowEntryPoint>();
#endif
        }
    }
}
