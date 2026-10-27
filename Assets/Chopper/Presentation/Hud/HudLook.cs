using System;
using Chopper.Presentation.Events;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Hud
{
    /// <summary>Global shader properties shared by <see cref="HudLook"/> (writes) and the HUD look shader and feature (read).</summary>
    public static class HudLookShaderIds
    {
        /// <summary>1 while a HUD texture is bound and a look preset is on; the renderer feature skips its pass otherwise.</summary>
        public static readonly int Enabled = Shader.PropertyToID("_ChopperHudLook");
        public static readonly int Texture = Shader.PropertyToID("_ChopperHudTex");
        /// <summary>(width, height, 1/width, 1/height) of the HUD texture.</summary>
        public static readonly int Size = Shader.PropertyToID("_ChopperHudSize");
        /// <summary>(glow, glow radius, scanlines, scanline count).</summary>
        public static readonly int Params0 = Shader.PropertyToID("_ChopperHudParams0");
        /// <summary>(curvature, chromatic, vignette, noise).</summary>
        public static readonly int Params1 = Shader.PropertyToID("_ChopperHudParams1");
        /// <summary>(brightness, glitch, glitch seed, unused).</summary>
        public static readonly int Params2 = Shader.PropertyToID("_ChopperHudParams2");
    }

    /// <summary>
    /// Retro HUD look (plan section 3b). Moves the HUD document's panel into a render texture, feeds gameplay into
    /// <see cref="HudLookModel"/> (damage, low hull, lock, missile, death/spawn) and writes the result as global shader
    /// properties for <see cref="HudLookRendererFeature"/>. With the Clean preset (or no settings) the HUD stays plain UI.
    /// Owned by the <see cref="Bridge.PresentationContext"/>; the HUD view calls <see cref="Attach"/>.
    /// </summary>
    public sealed class HudLook : IDisposable
    {
        private const string PresetKey = "Chopper.HudLook.Preset";
        private const string ReduceEffectsKey = "Chopper.HudLook.ReduceEffects";
        private const float DeathDropoutSeconds = 0.35f;
        private const float SpawnBootSeconds = 0.25f;

        private readonly HudLookSettings _settings;
        private readonly ChopperHudModel _hud;
        private readonly IDisposable _damage;
        private readonly HudLookModel _model = new();

        private UIDocument _document;
        private PanelSettings _sourcePanel;
        private PanelSettings _texturePanel;
        private RenderTexture _texture;
        private HudLookPreset _preset;
        private bool _reduceEffects;
        private bool _hadChopper;

        /// <param name="settings">Can be null: the HUD then stays plain UI.</param>
        public HudLook(HudLookSettings settings, ChopperHudModel hud, PresentationEvents events)
        {
            _settings = settings;
            _hud = hud;
            _preset = (HudLookPreset)PlayerPrefs.GetInt(PresetKey, (int)(settings != null ? settings.DefaultPreset : HudLookPreset.Clean));
            _reduceEffects = PlayerPrefs.GetInt(ReduceEffectsKey, 0) != 0;
            _hud.propertyChanged += OnHudChanged;
            _damage = events.Subscribe<DamageTaken>(damage => _model.OnDamage(damage.Amount));
        }

        /// <summary>Player choice, saved. Starts at the settings' default preset.</summary>
        public HudLookPreset Preset
        {
            get => _preset;
            set
            {
                _preset = value;
                PlayerPrefs.SetInt(PresetKey, (int)value);
                Refresh();
            }
        }

        /// <summary>Player option (accessibility, photosensitivity): no flicker, weaker glitches and pulses. Saved.</summary>
        public bool ReduceEffects
        {
            get => _reduceEffects;
            set
            {
                _reduceEffects = value;
                PlayerPrefs.SetInt(ReduceEffectsKey, value ? 1 : 0);
            }
        }

        /// <summary>True while the HUD renders into the texture and the look pass draws it.</summary>
        public bool IsActive => _texture != null;

        /// <summary>For EMP and hack effects (not built yet): hold a glitch for a time.</summary>
        public void Disrupt(float strength, float seconds) => _model.Disrupt(strength, seconds);

        /// <summary>Call from the HUD document's OnEnable, before reading its visual tree.</summary>
        public void Attach(UIDocument document)
        {
            if (_document == document) return;
            if (_document != null) Detach(_document);
            _document = document;
            _sourcePanel = document.panelSettings;
            Refresh();
        }

        public void Detach(UIDocument document)
        {
            if (_document != document) return;
            Release();
            _document = null;
            _sourcePanel = null;
        }

        public void Tick(float deltaTime)
        {
            if (_texture == null || !_settings.TryGetProfile(_preset, out var profile)) return;
            if (TextureSizeChanged()) Refresh();

            _model.Configure(profile, _settings.Reactions, _reduceEffects);
            _model.LowHull = _hud.HasChopper && _hud.HealthLow;
            _model.MissileUrgency = _hud.MissileInbound ? _hud.MissileUrgency : 0f;
            _model.Tick(deltaTime);

            Shader.SetGlobalVector(HudLookShaderIds.Params0,
                new Vector4(profile.Glow * (1f + _model.Pulse), profile.GlowRadius, profile.Scanlines, profile.ScanlineCount));
            Shader.SetGlobalVector(HudLookShaderIds.Params1,
                new Vector4(profile.Curvature, _model.Chromatic, profile.Vignette, _model.Noise));
            Shader.SetGlobalVector(HudLookShaderIds.Params2, new Vector4(_model.Brightness, _model.Glitch, _model.GlitchSeed, 0f));
        }

        public void Dispose()
        {
            _hud.propertyChanged -= OnHudChanged;
            _damage.Dispose();
            Release();
            _document = null;
        }

        private void OnHudChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            var name = args.propertyName.ToString();
            if (name == nameof(ChopperHudModel.Locked) && _hud.Locked)
            {
                _model.OnLockAcquired();
            }
            else if (name == nameof(ChopperHudModel.HasChopper) && _hud.HasChopper != _hadChopper)
            {
                // The HUD drops out on death and boots up on spawn.
                _hadChopper = _hud.HasChopper;
                var strength = _settings != null ? _settings.Reactions.SpawnGlitch : 0f;
                _model.Disrupt(strength, _hadChopper ? SpawnBootSeconds : DeathDropoutSeconds);
            }
        }

        /// <summary>Into the texture when a look preset is on and a document is attached; plain UI otherwise.</summary>
        private void Refresh()
        {
            var wanted = _document != null && _sourcePanel != null && _settings != null && _settings.TryGetProfile(_preset, out _);
            if (!wanted)
            {
                Release();
                return;
            }

            var (width, height) = TextureSize();
            if (_texture != null && _texture.width == width && _texture.height == height) return;
            ReleaseTexture();

            // Depth 24 includes stencil: UI Toolkit masks (overflow, rounded clips) need it.
            _texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Chopper HUD",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _texture.Create();

            // A copy, so play mode does not write a runtime texture into the shared panel settings asset.
            if (_texturePanel == null)
            {
                _texturePanel = Object.Instantiate(_sourcePanel);
                _texturePanel.name = $"{_sourcePanel.name} (HUD texture)";
                _texturePanel.clearColor = true;
                _texturePanel.colorClearValue = Color.clear;
            }
            _texturePanel.targetTexture = _texture;
            if (_document.panelSettings != _texturePanel) _document.panelSettings = _texturePanel;

            Shader.SetGlobalTexture(HudLookShaderIds.Texture, _texture);
            Shader.SetGlobalVector(HudLookShaderIds.Size, new Vector4(width, height, 1f / width, 1f / height));
            Shader.SetGlobalFloat(HudLookShaderIds.Enabled, 1f);
        }

        private (int width, int height) TextureSize()
        {
            var scale = _settings != null ? _settings.RenderScale : 1f;
            return (Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale)), Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale)));
        }

        private bool TextureSizeChanged()
        {
            var (width, height) = TextureSize();
            return _texture.width != width || _texture.height != height;
        }

        /// <summary>Back to plain UI: the document gets its own panel settings again.</summary>
        private void Release()
        {
            Shader.SetGlobalFloat(HudLookShaderIds.Enabled, 0f);
            if (_document != null && _sourcePanel != null && _document.panelSettings == _texturePanel)
                _document.panelSettings = _sourcePanel;
            if (_texturePanel != null)
            {
                Object.Destroy(_texturePanel);
                _texturePanel = null;
            }
            ReleaseTexture();
        }

        private void ReleaseTexture()
        {
            if (_texture == null) return;
            if (_texturePanel != null) _texturePanel.targetTexture = null;
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }
    }
}
