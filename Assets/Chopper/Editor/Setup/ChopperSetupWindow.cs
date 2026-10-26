using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The one editor entry point for project setup: create/repair config, set up the chopper prefab and the open
    /// scene, and validate everything in one list. Add new systems' setup steps and checks here.
    /// </summary>
    internal sealed class ChopperSetupWindow : EditorWindow
    {
        private ScrollView _problems;
        private Label _summary;

        [MenuItem("Chopper/Setup", priority = 0)]
        private static void Open() => GetWindow<ChopperSetupWindow>("Chopper Setup");

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = 8;

            root.Add(new Label("Steps") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            root.Add(new Button(() => Do(() => ConfigAssets.EnsureAll())) { text = "1. Create / repair config" });
            root.Add(new Button(() => Do(() => PrefabSetup.Apply(ConfigAssets.Load()))) { text = "2. Set up chopper prefab" });
            root.Add(new Button(() => Do(() => SceneSetup.Apply(ConfigAssets.Load()))) { text = "3. Set up open scene" });
            root.Add(new Button(() => Do(() =>
            {
                var config = ConfigAssets.EnsureAll();
                PrefabSetup.Apply(config);
                SceneSetup.Apply(config);
            })) { text = "Do all" });

            var configRow = new Button(() =>
            {
                var config = ConfigAssets.Load();
                if (config != null) { Selection.activeObject = config; EditorGUIUtility.PingObject(config); }
            }) { text = "Select game config" };
            root.Add(configRow);

            root.Add(new Label("Problems") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } });
            root.Add(new Button(Validate) { text = "Validate" });
            _summary = new Label();
            root.Add(_summary);
            _problems = new ScrollView { style = { flexGrow = 1 } };
            root.Add(_problems);

            Validate();
        }

        private void Do(System.Action step)
        {
            step();
            Validate();
        }

        private void Validate()
        {
            _problems.Clear();
            var problems = SetupValidator.Run();
            _summary.text = problems.Count == 0 ? "No problems found." : $"{problems.Count} problem(s). Click one to select its asset.";

            foreach (var problem in problems)
            {
                var box = new HelpBox(problem.Message, HelpBoxMessageType.Warning);
                var context = problem.Context;
                if (context != null)
                    box.RegisterCallback<ClickEvent>(_ => { Selection.activeObject = context; EditorGUIUtility.PingObject(context); });
                _problems.Add(box);
            }
        }
    }
}
