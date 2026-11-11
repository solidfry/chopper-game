using System;
using Chopper.Simulation.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Chopper.Presentation.Hud
{
    public enum PromptDevice
    {
        KeyboardMouse = 0,
        Gamepad = 1,
    }

    /// <summary>
    /// Key names for on-screen prompts, for the device the player used last: "HOLD R" on the keyboard, "HOLD X" on an Xbox
    /// pad, "HOLD SQUARE" on a DualSense. Flight keys come from the input actions, so later rebinding works here too.
    /// One per <see cref="Bridge.PresentationContext"/>; the last device is saved, so it carries across scenes.
    /// In UXML, a label with the class <c>input-prompt</c> holds a template such as "{Back} = BACK" (UI: InputPromptLabels).
    /// </summary>
    public sealed class InputPrompts
    {
        private const string DeviceKey = "Chopper.Input.PromptDevice";
        private const string KeyboardGroup = "Mouse and Keyboard";
        private const string GamepadGroup = "Gamepad";
        /// <summary>Mouse movement below this (pixels squared per frame) is a bumped desk, not a switch to the mouse.</summary>
        private const float MouseMoveSquared = 16f;

        private readonly PlayerControls _controls;
        private Gamepad _pad;

        public InputPrompts(PlayerControls controls)
        {
            _controls = controls;
            Device = (PromptDevice)PlayerPrefs.GetInt(DeviceKey, (int)PromptDevice.KeyboardMouse);
            _pad = Gamepad.current;
            if (_pad == null) Device = PromptDevice.KeyboardMouse;
        }

        /// <summary>The device changed, or another kind of pad was connected.</summary>
        public event Action Changed;

        public PromptDevice Device { get; private set; }

        /// <summary>Key name in upper case. Names: Repair, Ability, Back (leave a screen), Menu (open or close the options).</summary>
        public string Key(string name) => name switch
        {
            "Repair" => ActionKey(_controls.Flight.Repair),
            "Ability" => ActionKey(_controls.Flight.Ability),
            "Back" => MenuKey("ESC", "<Gamepad>/buttonEast"),
            "Submit" => MenuKey("ENTER", "<Gamepad>/buttonSouth"),
            "Menu" => MenuKey("ESC", "<Gamepad>/start"),
            _ => name,
        };

        /// <summary>"{Back} = BACK" to "ESC = BACK" or "B = BACK".</summary>
        public string Fill(string template)
        {
            if (string.IsNullOrEmpty(template)) return template;
            var text = template;
            foreach (var name in new[] { "Repair", "Ability", "Back", "Menu", "Submit" })
                text = text.Replace("{" + name + "}", Key(name));
            return text;
        }

        public void Use(PromptDevice device)
        {
            if (device == Device) return;
            Device = device;
            Changed?.Invoke();
        }

        /// <summary>Call once per frame: a real press or move on a device makes it the current one.</summary>
        public void Tick()
        {
            var pad = Gamepad.current;
            var padChanged = pad != _pad;
            _pad = pad;

            var before = Device;
            if (pad == null) Device = PromptDevice.KeyboardMouse;
            else if (pad.wasUpdatedThisFrame && !pad.CheckStateIsAtDefaultIgnoringNoise()) Device = PromptDevice.Gamepad;
            if (KeyboardOrMouseUsed()) Device = PromptDevice.KeyboardMouse;

            if (Device != before) PlayerPrefs.SetInt(DeviceKey, (int)Device);
            if (Device != before || padChanged) Changed?.Invoke();
        }

        private static bool KeyboardOrMouseUsed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;
            var mouse = Mouse.current;
            return mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                                     mouse.delta.ReadValue().sqrMagnitude > MouseMoveSquared);
        }

        private string ActionKey(InputAction action)
        {
            var group = Device == PromptDevice.Gamepad ? GamepadGroup : KeyboardGroup;
            var mask = InputBinding.MaskByGroup(group);
            foreach (var binding in action.bindings)
            {
                if (!mask.Matches(binding)) continue;
                return Device == PromptDevice.Gamepad
                    ? PadKey(binding.effectivePath)
                    : binding.ToDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions).ToUpperInvariant();
            }
            return "";
        }

        private string MenuKey(string keyboard, string padPath) => Device == PromptDevice.Gamepad ? PadKey(padPath) : keyboard;

        /// <summary>The connected pad's own name for the button (X or SQUARE); without a pad, the binding's.</summary>
        private string PadKey(string path)
        {
            var control = _pad != null ? InputControlPath.TryFindControl(_pad, path) : null;
            var name = control != null ? control.displayName.ToUpperInvariant() : "";
            // A generic pad has only layout names ("Button West"): the binding's name reads better ("X").
            if (name.Length > 0 && !name.StartsWith("BUTTON ", StringComparison.Ordinal))
                return control.parent is DpadControl && !name.StartsWith("D-PAD", StringComparison.Ordinal) ? "D-PAD " + name : name;
            var binding = new InputBinding { path = path };
            return binding.ToDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions).Replace('/', ' ').ToUpperInvariant();
        }
    }
}
