using System.Collections.Generic;
using Chopper.Presentation.Hud;
using UnityEngine.UIElements;

namespace Chopper.UI
{
    /// <summary>
    /// Keeps every label with the class <c>input-prompt</c> under a root on the current input device. The label's UXML text
    /// is the template, for example "{Back} = BACK" (<see cref="InputPrompts.Fill"/>). Make one each time the tree is built.
    /// </summary>
    public sealed class InputPromptLabels
    {
        private const string PromptClass = "input-prompt";

        private readonly InputPrompts _prompts;
        private readonly List<(Label Label, string Template)> _labels = new();

        public InputPromptLabels(VisualElement root, InputPrompts prompts)
        {
            _prompts = prompts;
            root?.Query<Label>(className: PromptClass).ForEach(label => _labels.Add((label, label.text)));
            _prompts.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _prompts.Changed -= Refresh;

        private void Refresh()
        {
            foreach (var (label, template) in _labels) label.text = _prompts.Fill(template);
        }
    }
}
