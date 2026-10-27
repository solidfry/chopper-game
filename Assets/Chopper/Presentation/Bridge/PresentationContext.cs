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
    /// Everything presentation needs, in one object: config, audio, effects, HUD view models and look, camera and the event
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
            var moduleRoot = new GameObject("Module Visuals").transform;
            moduleRoot.SetParent(root, false);
            ModuleVisuals = new EffectPool(moduleRoot);
            HudLook = new HudLook(config != null ? config.HudLook : null, Hud, Events);
            if (cameraRig != null && config != null) cameraRig.UseSettings(config.Camera);

            _owned.Add(new CombatFeedback(this));
            _owned.Add(new MatchFeedback(this));
            _owned.Add(new TargetingFeedback(this));
        }

        public GameConfig Config { get; }
        public IAudioService Audio => _audio;
        /// <summary>Can be null: cues are then not played.</summary>
        public SoundLibrary Sounds => Config != null ? Config.Audio : null;
        public CombatEffects Effects { get; }
        /// <summary>Pool for module prefabs fitted to chassis sockets (guns, pods, rotors...).</summary>
        public EffectPool ModuleVisuals { get; }
        public ChopperHudModel Hud { get; } = new();
        public MatchHudModel Match { get; } = new();
        /// <summary>Screen markers on other choppers and ground targets, and the rocket lead indicator.</summary>
        public WorldMarkerModel Markers { get; } = new();
        /// <summary>Retro HUD look: the HUD view attaches its document; gameplay drives glitches.</summary>
        public HudLook HudLook { get; }
        /// <summary>Can be null (no chase camera in the scene).</summary>
        public ChopperCameraRig CameraRig { get; }
        public PresentationEvents Events { get; }
        /// <summary>A menu (options) is open: flight input is blocked (<see cref="InputSettingsSystem"/>).</summary>
        public bool MenuOpen { get; set; }
        /// <summary>Saved control options (mouse steer, sensitivity, invert).</summary>
        public InputOptions InputOptions { get; } = new();
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
            ModuleVisuals.Tick(deltaTime);
            Hud.Tick(deltaTime);
            Match.Tick(deltaTime);
            HudLook.Tick(unscaledDeltaTime);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (var owned in _owned) owned.Dispose();
            HudLook.Dispose();
            Events.Dispose();
            Effects.Dispose();
            ModuleVisuals.Dispose();
            _audio.Dispose();
        }
    }
}
