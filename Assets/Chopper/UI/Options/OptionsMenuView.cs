using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hud;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Chopper.UI.Options
{
    /// <summary>
    /// In-game options overlay (<c>OptionsMenu.uxml</c>): HUD look preset, "reduce flicker / effects", and volumes.
    /// Escape or gamepad Start opens and closes it. While open, the cursor is free and flight input is blocked
    /// (<see cref="PresentationContext.MenuOpen"/>). Values are saved by the services they belong to
    /// (<see cref="HudLook"/>, <see cref="IAudioService"/>). The first part of the menu port (old UGUI menus).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OptionsMenuView : MonoBehaviour
    {
        private const string HiddenClass = "options--hidden";

        [SerializeField, Tooltip("Scene presentation bridge; it owns the HUD look and the audio service.")]
        private PresentationBridge bridge;

        private PresentationContext _context;
        private VisualElement _root;
        private DropdownField _hudLook;
        private Toggle _reduceEffects;
        private Button _resume;
        private readonly List<(Slider Slider, AudioChannel Channel)> _volumes = new();
        /// <summary>Same order as <see cref="HudLookPreset"/>.</summary>
        private readonly List<string> _presetNames = new() { "CLEAN", "VECTOR", "CRT 84" };

        public bool IsOpen { get; private set; }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

        private void OnEnable()
        {
            if (bridge == null)
            {
                Debug.LogError($"{name}: no presentation bridge set. Run Chopper → Setup.", this);
                enabled = false;
                return;
            }
            _context = bridge.Context;

            var document = GetComponent<UIDocument>().rootVisualElement;
            _root = document.Q("options");
            _hudLook = _root.Q<DropdownField>("hud-look");
            _reduceEffects = _root.Q<Toggle>("reduce-effects");
            _resume = _root.Q<Button>("resume");

            _hudLook.choices = _presetNames;
            _hudLook.RegisterValueChangedCallback(OnHudLookChanged);
            _reduceEffects.RegisterValueChangedCallback(OnReduceEffectsChanged);
            _resume.clicked += Close;

            _volumes.Clear();
            AddVolume("volume-master", AudioChannel.Master);
            AddVolume("volume-music", AudioChannel.Music);
            AddVolume("volume-effects", AudioChannel.Effects);
            AddVolume("volume-ui", AudioChannel.UI);
            AddVolume("volume-ambient", AudioChannel.Ambient);

            SetOpen(false);
        }

        private void OnDisable()
        {
            if (_root == null) return;
            _hudLook.UnregisterValueChangedCallback(OnHudLookChanged);
            _reduceEffects.UnregisterValueChangedCallback(OnReduceEffectsChanged);
            _resume.clicked -= Close;
            foreach (var (slider, _) in _volumes) slider.UnregisterValueChangedCallback(OnVolumeChanged);
            if (IsOpen && !_context.IsDisposed) _context.MenuOpen = false;
        }

        private void Update()
        {
            var escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            var start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if (escape || start) SetOpen(!IsOpen);
        }

        private void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            IsOpen = open;
            _root.EnableInClassList(HiddenClass, !open);
            if (!_context.IsDisposed) _context.MenuOpen = open;
            if (_context.CameraRig != null) _context.CameraRig.SetCursorFree(open);
            if (!open) return;

            // Show the saved values each time it opens.
            _hudLook.SetValueWithoutNotify(_presetNames[Mathf.Clamp((int)_context.HudLook.Preset, 0, _presetNames.Count - 1)]);
            _reduceEffects.SetValueWithoutNotify(_context.HudLook.ReduceEffects);
            foreach (var (slider, channel) in _volumes) slider.SetValueWithoutNotify(_context.Audio.GetVolume(channel));
            _resume.Focus();
        }

        private void AddVolume(string elementName, AudioChannel channel)
        {
            var slider = _root.Q<Slider>(elementName);
            if (slider == null) return;
            slider.userData = channel;
            slider.RegisterValueChangedCallback(OnVolumeChanged);
            _volumes.Add((slider, channel));
        }

        private void OnHudLookChanged(ChangeEvent<string> evt)
        {
            var index = _presetNames.IndexOf(evt.newValue);
            if (index >= 0) _context.HudLook.Preset = (HudLookPreset)index;
        }

        private void OnReduceEffectsChanged(ChangeEvent<bool> evt) => _context.HudLook.ReduceEffects = evt.newValue;

        private void OnVolumeChanged(ChangeEvent<float> evt)
        {
            if (evt.target is Slider { userData: AudioChannel channel })
                _context.Audio.SetVolume(channel, evt.newValue);
        }
    }
}
