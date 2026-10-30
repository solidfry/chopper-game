using Chopper.Presentation.Hangar;
using Chopper.Services.Matchmaking;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Chopper.App
{
    /// <summary>
    /// The hangar scene's scope (a child of <see cref="RootLifetimeScope"/>). Hands the scene's
    /// <see cref="HangarStage"/> the root's matchmaking service for FIND MATCH, so presentation needs no container.
    /// Chopper → Setup, step 7 puts it in the hangar scene.
    /// </summary>
    public sealed class HangarLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterBuildCallback(container =>
            {
                var stage = Object.FindAnyObjectByType<HangarStage>(FindObjectsInactive.Include);
                if (stage != null) stage.UseMatchmaking(container.Resolve<IMatchmakingService>());
                else Debug.LogWarning($"{name}: no hangar stage in the scene; FIND MATCH has no matchmaking service.", this);
            });
        }
    }
}
