using Chopper.Services.Auth;
using Chopper.Services.Matchmaking;
using Chopper.Services.Platform;
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
        protected override void Configure(IContainerBuilder builder)
        {
#if CHOPPER_STEAM
            // Entry point: VContainer runs Steam callbacks every frame and shuts Steam down on exit.
            builder.RegisterEntryPoint<SteamPlatform>();
#else
            builder.Register<OfflinePlatform>(Lifetime.Singleton).As<IPlatformIdentity>();
#endif
            builder.Register<UnityAuthService>(Lifetime.Singleton).As<IAuthService>();
            // TODO: Phase 3 — replace with the Unity Matchmaker + Edgegap allocator implementation.
            builder.Register<LocalMatchmakingService>(Lifetime.Singleton).As<IMatchmakingService>();

            builder.RegisterEntryPoint<SignInOnStart>();
        }
    }
}
