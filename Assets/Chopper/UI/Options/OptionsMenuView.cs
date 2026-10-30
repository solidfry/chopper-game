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
    /// In-game options overlay (<c>OptionsMenu.uxml</c>): controls (mouse steer, sensitivity, invert), start camera view, HUD look preset,
    /// "reduce flicker / effects", volumes, and HANGAR (leave the match to change the loadout).
    /// Escape or gamepad Start opens and closes it. While open, the cursor is free and flight input is blocked
    /// (<see cref="PresentationContext.MenuOpen"/>). Values are saved by the services they belong to
    /// (<see cref="InputOptions"/>, <see cref="HudLook"/>, <see cref="IAudioService"/>, the camera rig's default view). The first part of the menu port (old UGUI menus).
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class OptionsMenuView : MonoBehaviour
    {
        private const string HiddenClass = "options--hidden";
        private const string ButtonHiddenClass = "options__button--hidden";

        [SerializeField, Tooltip("Scene presentation bridge; it owns the HUD look and the audio service.")]
        private PresentationBridge bridge;

        private PresentationContext _context;
        private PanelRenderer _renderer;
        private VisualElement _root;
        private DropdownField _mouseSteer;
        private Slider _mouseSensitivity;
        private Toggle _invertPitch;
        private DropdownField _defaultView;
        private DropdownField _hudLook;
        private Toggle _reduceEffects;
        private Button _resume;
        private Button _hangar;
        private readonly List<(Slider Slider, AudioChannel Channel)> _volumes = new();
        /// <summary>Same order as <see cref="HudLookPreset"/>.</summary>
        private readonly List<string> _presetNames = new() { "CLEAN", "VECTOR", "CRT 84" };
        /// <summary>Same order as <see cref="InputOptions.MouseSteerIndex"/>.</summary>
        private readonly List<string> _steerNames = new() { "TURN (AIM)", "ROLL (BATTLEFIELD)" };
        /// <summary>The camera views (<see cref="Presentation.Camera.CameraSettings.Views"/>), upper case; filled when the menu opens.</summary>
        private readonly List<string> _viewNames = new();

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
            _renderer = GetComponent<PanelRenderer>();
            // Called at once if the tree exists, and again after each rebuild.
            _renderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            Unbind();
            _root = rootElement?.Q("options");
            if (_root == null) return;
            _mouseSteer = _root.Q<DropdownField>("mouse-steer");
            _mouseSensitivity = _root.Q<Slider>("mouse-sensitivity");
            _invertPitch = _root.Q<Toggle>("invert-pitch");
            _defaultView = _root.Q<DropdownField>("default-view");
            _hudLook = _root.Q<DropdownField>("hud-look");
            _reduceEffects = _root.Q<Toggle>("reduce-effects");
            _resume = _root.Q<Button>("resume");
            _hangar = _root.Q<Button>("hangar");

            _mouseSteer.choices = _steerNames;
            _mouseSteer.RegisterValueChangedCallback(OnMouseSteerChanged);
            _mouseSensitivity.RegisterValueChangedCallback(OnSensitivityChanged);
            _invertPitch.RegisterValueChangedCallback(OnInvertPitchChanged);
            _defaultView.choices = _viewNames;
            _defaultView.RegisterValueChangedCallback(OnDefaultViewChanged);
            _hudLook.choices = _presetNames;
            _hudLook.RegisterValueChangedCallback(OnHudLookChanged);
            _reduceEffects.RegisterValueChangedCallback(OnReduceEffectsChanged);
            _resume.clicked += Close;
            if (_hangar != null) _hangar.clicked += OpenHangar;

            _volumes.Clear();
            AddVolume("volume-master", AudioChannel.Master);
            AddVolume("volume-music", AudioChannel.Music);
            AddVolume("volume-effects", AudioChannel.Effects);
            AddVolume("volume-ui", AudioChannel.UI);
            AddVolume("volume-ambient", AudioChannel.Ambient);

            SetOpen(IsOpen);
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            Unbind();
            if (IsOpen && !_context.IsDisposed) _context.MenuOpen = false;
            IsOpen = false;
        }

        private void Unbind()
        {
            if (_root == null) return;
            _mouseSteer.UnregisterValueChangedCallback(OnMouseSteerChanged);
            _mouseSensitivity.UnregisterValueChangedCallback(OnSensitivityChanged);
            _invertPitch.UnregisterValueChangedCallback(OnInvertPitchChanged);
            _defaultView.UnregisterValueChangedCallback(OnDefaultViewChanged);
            _hudLook.UnregisterValueChangedCallback(OnHudLookChanged);
            _reduceEffects.UnregisterValueChangedCallback(OnReduceEffectsChanged);
            _resume.clicked -= Close;
            if (_hangar != null) _hangar.clicked -= OpenHangar;
            foreach (var (slider, _) in _volumes) slider.UnregisterValueChangedCallback(OnVolumeChanged);
            _root = null;
        }

        private void Update()
        {
            var escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            var start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if ((escape || start) && _root != null) SetOpen(!IsOpen);
        }

        private void Close() => SetOpen(false);

        /// <summary>Leave the match for the hangar (change the loadout). The cursor stays free there.</summary>
        private void OpenHangar()
        {
            IsOpen = false;
            _context.OpenHangar();
        }

        private void SetOpen(bool open)
        {
            IsOpen = open;
            _root.EnableInClassList(HiddenClass, !open);
            if (!_context.IsDisposed) _context.MenuOpen = open;
            if (_context.CameraRig != null) _context.CameraRig.SetCursorFree(open);
            if (!open) return;

            // Show the saved values each time it opens.
            var input = _context.InputOptions;
            _mouseSteer.SetValueWithoutNotify(_steerNames[Mathf.Clamp(input.MouseSteerIndex, 0, _steerNames.Count - 1)]);
            _mouseSensitivity.SetValueWithoutNotify(input.MouseSensitivity);
            _invertPitch.SetValueWithoutNotify(input.InvertPitch);
            ShowDefaultView();
            _hudLook.SetValueWithoutNotify(_presetNames[Mathf.Clamp((int)_context.HudLook.Preset, 0, _presetNames.Count - 1)]);
            _reduceEffects.SetValueWithoutNotify(_context.HudLook.ReduceEffects);
            foreach (var (slider, channel) in _volumes) slider.SetValueWithoutNotify(_context.Audio.GetVolume(channel));
            _hangar?.EnableInClassList(ButtonHiddenClass, !_context.CanOpenHangar);
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

        private void OnMouseSteerChanged(ChangeEvent<string> evt)
        {
            var index = _steerNames.IndexOf(evt.newValue);
            if (index >= 0) _context.InputOptions.MouseSteerIndex = index;
        }

        private void OnSensitivityChanged(ChangeEvent<float> evt) => _context.InputOptions.MouseSensitivity = evt.newValue;

        private void OnInvertPitchChanged(ChangeEvent<bool> evt) => _context.InputOptions.InvertPitch = evt.newValue;

        /// <summary>Lists the camera views and shows the saved one. Hidden when there is no rig or only one view.</summary>
        private void ShowDefaultView()
        {
            _viewNames.Clear();
            var rig = _context.CameraRig;
            var current = -1;
            if (rig != null)
            {
                var views = rig.ActiveSettings.Views;
                var wanted = rig.DefaultView;
                foreach (var view in views)
                {
                    if (view == null) continue;
                    if (view.Name == wanted) current = _viewNames.Count;
                    _viewNames.Add(view.Name.ToUpperInvariant());
                }
            }
            _defaultView.choices = _viewNames;
            _defaultView.style.display = _viewNames.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_viewNames.Count > 0) _defaultView.SetValueWithoutNotify(_viewNames[Mathf.Max(current, 0)]);
        }

        /// <summary>Saved for the next match; the current view does not change (C / gamepad Y does that).</summary>
        private void OnDefaultViewChanged(ChangeEvent<string> evt)
        {
            var rig = _context.CameraRig;
            if (rig == null) return;
            foreach (var view in rig.ActiveSettings.Views)
                if (view != null && view.Name.ToUpperInvariant() == evt.newValue) rig.DefaultView = view.Name;
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
