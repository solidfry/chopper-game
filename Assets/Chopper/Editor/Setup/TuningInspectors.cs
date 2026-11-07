using Chopper.Presentation.Bridge;
using Chopper.Presentation.Flow;
using Chopper.Presentation.Hud;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>The default inspector with a button that plays the effect in play mode, to tune it live.</summary>
    internal abstract class PreviewInspector : UnityEditor.Editor
    {
        protected abstract string ButtonText { get; }
        protected abstract void Preview(PresentationContext context);

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var button = new Button(RunPreview) { text = ButtonText };
            button.style.height = 28;
            button.style.marginBottom = 6;
            root.Add(button);
            root.Add(new HelpBox("Press Play, then use this button. Changes made in play mode stay in this asset.", HelpBoxMessageType.Info));
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }

        private void RunPreview()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Preview works only in play mode.");
                return;
            }
            var bridge = Object.FindAnyObjectByType<PresentationBridge>();
            if (bridge == null) Debug.LogWarning("No presentation bridge in the open scene.");
            else Preview(bridge.Context);
        }
    }

    [CustomEditor(typeof(SceneTransitionSettings))]
    internal sealed class SceneTransitionSettingsInspector : PreviewInspector
    {
        protected override string ButtonText => "Preview transition";

        protected override void Preview(PresentationContext context)
        {
            if (!context.PreviewTransition()) Debug.LogWarning("No preview: a transition is running, or there is no scene flow.");
        }
    }

    [CustomEditor(typeof(HudLookSettings))]
    internal sealed class HudLookSettingsInspector : PreviewInspector
    {
        protected override string ButtonText => "Replay cockpit boot-up (fly in a cockpit view)";

        protected override void Preview(PresentationContext context) => context.HudLook.ReplayBoot();
    }
}
