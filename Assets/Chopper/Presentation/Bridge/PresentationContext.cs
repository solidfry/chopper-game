using System;
using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using Chopper.Presentation.Effects;
using Chopper.Presentation.Events;
using Chopper.Presentation.Feedback;
using Chopper.Presentation.Hud;
using UnityEngine;

namespace Chopper.Presentation.Bridge
{
    /// <summary>
    /// Everything presentation needs, in one object: config, audio, effects, HUD view models, camera and the event
    /// channel. Made by the scene's <see cref="PresentationBridge"/>; ECS presentation systems get it through
    /// <see cref="PresentationSystemBase"/>, UI through the bridge. Ticks all of them from one place.
    /// TODO: becomes the match <c>LifetimeScope</c> (VContainer) registrations.
    /// </summary>
    public sealed class PresentationContext : IDisposable
    {
        private readonly AudioService _audio;
        private readonly List<IDisposable> _owned = new();

        public PresentationContext(GameConfig config, Transform root, ChopperCameraRig cameraRig)
        {
            Config = config;
            CameraRig = cameraRig;
            Events = new PresentationEvents();
            _audio = new AudioService(config != null ? config.Audio : null, root);
            Effects = new CombatEffects(config != null ? config.Effects : null, root);

            _owned.Add(new CombatFeedback(this));
            _owned.Add(new MatchFeedback(this));
            _owned.Add(new TargetingFeedback(this));
        }

        public GameConfig Config { get; }
        public IAudioService Audio => _audio;
        /// <summary>Can be null: cues are then not played.</summary>
        public SoundLibrary Sounds => Config != null ? Config.Audio : null;
        public CombatEffects Effects { get; }
        public ChopperHudModel Hud { get; } = new();
        public MatchHudModel Match { get; } = new();
        /// <summary>Can be null (no chase camera in the scene).</summary>
        public ChopperCameraRig CameraRig { get; }
        public PresentationEvents Events { get; }
        public bool IsDisposed { get; private set; }

        /// <summary>Call from Start or later: the mixer ignores volume changes during Awake.</summary>
        public void Start(AudioClip music)
        {
            _audio.ApplySavedVolumes();
            _audio.PlayMusic(music != null ? music : Config != null ? Config.MatchMusic : null);
        }

        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            _audio.Tick(unscaledDeltaTime);
            Effects.Tick(deltaTime);
            Hud.Tick(deltaTime);
            Match.Tick(deltaTime);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (var owned in _owned) owned.Dispose();
            Events.Dispose();
            Effects.Dispose();
            _audio.Dispose();
        }
    }
}
