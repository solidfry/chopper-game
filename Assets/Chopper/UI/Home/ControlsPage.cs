using System.Collections.Generic;
using Chopper.Presentation.Hud;
using UnityEngine.UIElements;

namespace Chopper.UI.Home
{
    /// <summary>
    /// The CONTROLS page (<c>page-controls</c> in Home.uxml, styled in Controls.uss): every control in groups, with its
    /// keys on keyboard and mouse and on a gamepad (<see cref="InputPrompts.ControlRows"/>). The column of the device
    /// you used last is lit, and the page redraws when you switch device or plug in another pad. Read only for now
    /// (rebinding comes later; the flight keys already come from the input actions, so it will show here).
    /// </summary>
    internal sealed class ControlsPage
    {
        private const string ActiveClass = "ctl-cell--active";

        private readonly ScrollView _list;
        private readonly Label _device;
        private readonly VisualElement _headKeyboard;
        private readonly VisualElement _headPad;
        private readonly List<InputPrompts.ControlRow> _rows = new();
        private InputPrompts _prompts;

        public ControlsPage(VisualElement root)
        {
            Back = root.Q<Button>("controls-back");
            _list = root.Q<ScrollView>("ctl-list");
            _device = root.Q<Label>("ctl-device");
            _headKeyboard = root.Q("ctl-head-keyboard");
            _headPad = root.Q("ctl-head-pad");
        }

        public Button Back { get; }

        /// <summary>Fills the list; call when the page opens. It then follows the prompts' device changes.</summary>
        public void Show(InputPrompts prompts)
        {
            if (_prompts != prompts)
            {
                if (_prompts != null) _prompts.Changed -= Rebuild;
                _prompts = prompts;
                if (_prompts != null) _prompts.Changed += Rebuild;
            }
            Rebuild();
        }

        public void Dispose()
        {
            if (_prompts != null) _prompts.Changed -= Rebuild;
            _prompts = null;
        }

        private void Rebuild()
        {
            if (_list == null || _prompts == null) return;
            var pad = _prompts.Device == PromptDevice.Gamepad;
            if (_device != null) _device.text = pad ? "INPUT // GAMEPAD" : "INPUT // KEYBOARD + MOUSE";
            _headKeyboard?.EnableInClassList(ActiveClass, !pad);
            _headPad?.EnableInClassList(ActiveClass, pad);

            _prompts.ControlRows(_rows);
            _list.Clear();
            string group = null;
            var index = 0;
            foreach (var row in _rows)
            {
                if (row.Group != group)
                {
                    group = row.Group;
                    var head = new Label("// " + group);
                    head.AddToClassList("ctl-group");
                    head.AddToClassList("chopper-title");
                    _list.Add(head);
                }
                var line = new VisualElement();
                line.AddToClassList("ctl-row");
                if (index++ % 2 == 1) line.AddToClassList("ctl-row--odd");
                line.Add(Cell(row.Label, "ctl-cell--action", false));
                line.Add(Cell(row.Keyboard, "ctl-cell--key", !pad));
                line.Add(Cell(row.Pad, "ctl-cell--key", pad));
                _list.Add(line);
            }
        }

        private static Label Cell(string text, string kind, bool active)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ctl-cell");
            label.AddToClassList(kind);
            label.AddToClassList("chopper-title");
            if (active) label.AddToClassList(ActiveClass);
            if (text == "-") label.AddToClassList("ctl-cell--none");
            return label;
        }
    }
}
