using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Scene owner of the <see cref="ChopperHudModel"/>. The HUD view binds to it; <see cref="LocalChopperHudSystem"/>
    /// finds it and writes to it. TODO: register the model in the match <c>LifetimeScope</c> (VContainer) instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperHudModelHost : MonoBehaviour
    {
        public ChopperHudModel Model { get; } = new();
    }
}
