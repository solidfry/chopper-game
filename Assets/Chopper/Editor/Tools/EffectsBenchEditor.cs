using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Editor.Tools
{
    /// <summary>
    /// Buttons for the <see cref="EffectsBench"/>: play the bench scene, fire each effect, each explosion layer alone,
    /// clear, and the time scale. The effect buttons work in play mode only (the bench owns live effects there).
    /// </summary>
    [CustomEditor(typeof(EffectsBench))]
    internal sealed class EffectsBenchEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var bench = (EffectsBench)target;
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            var play = new Button(() => SceneJumper.PlayFrom(bench.gameObject.scene.path)) { text = "▶ Play the bench" };
            root.Add(play);
            root.Add(new HelpBox("Keys in play: 1 ground blast, 2 air blast, 3 missile, 4 rocket, 5 gun, 6 next layer, " +
                                 "7 hit sparks, 8 crash dust, 9 shoot down (debris), 0 flares, N nuke (then F5), B boost, E engine, H chopper health (100/50/25/10 %), F fly a circle or hover, " +
                                 "R repeat, C clear, [ ] time. Camera: right drag turn, wheel zoom, middle drag height, " +
                                 "F1 side, F2 above (chopper view), F3 low, F4 close, F5 far (nuke).", HelpBoxMessageType.Info));

            var effects = new VisualElement();
            root.Add(effects);
            effects.Add(Row(("Ground blast", bench.GroundBlast), ("Air blast", bench.AirBlast), ("Nuke", () =>
            {
                bench.Nuke();
                var orbit = FindAnyObjectByType<BenchOrbitCamera>();
                if (orbit != null) orbit.FarView();
            })));
            effects.Add(Row(("Missile impact", bench.MissileImpact), ("Rocket impact", bench.RocketImpact), ("Gun impact", bench.GunImpact)));
            effects.Add(new Label("Damaged chopper (health drives smoke and fire as in a match):") { style = { marginTop = 6 } });
            effects.Add(Row(("100 %", () => bench.Health = 1f), ("50 %", () => bench.Health = 0.5f),
                ("25 %", () => bench.Health = 0.25f), ("10 %", () => bench.Health = 0.1f)));
            effects.Add(Row(("Hover", () => bench.CircleRadius = 0f), ("Fly a circle", () => bench.CircleRadius = 30f),
                ("Hit sparks", bench.HitSparks), ("Crash dust", bench.CrashDust), ("Shoot down", bench.Wreck), ("Flares", bench.Flares), ("Boost", bench.Boost), ("Next engine", bench.NextEngine)));
            effects.Add(new Label("Explosion layers, one at a time:") { style = { marginTop = 6 } });
            var layers = new VisualElement();
            effects.Add(layers);
            effects.Add(Row(("Clear", bench.Clear)));

            void Refresh()
            {
                play.SetEnabled(!EditorApplication.isPlaying);
                effects.SetEnabled(EditorApplication.isPlaying);
                if (bench == null || bench.Settings == null) return;
                var list = bench.Settings.ChopperExplosionLayers;
                if (layers.childCount == list.Count) return;
                layers.Clear();
                for (var i = 0; i < list.Count; i++)
                {
                    var index = i;
                    var layer = list[i];
                    var name = layer.Prefab != null ? layer.Prefab.name.Replace("Prefab_", "") : "(none)";
                    layers.Add(new Button(() => bench.Layer(index))
                    {
                        text = $"{i + 1}. {name}  x{layer.Scale:0.#}, {layer.LifeSeconds:0.#} s{(layer.Ground ? ", ground" : "")}",
                    });
                }
            }

            Refresh();
            root.schedule.Execute(Refresh).Every(1000);
            return root;
        }

        private static VisualElement Row(params (string Text, System.Action Click)[] buttons)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            foreach (var (text, click) in buttons)
                row.Add(new Button(click) { text = text, style = { flexGrow = 1 } });
            return row;
        }
    }
}
