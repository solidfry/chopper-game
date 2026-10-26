using Chopper.Presentation.Hud;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Hud
{
    /// <summary>
    /// Binds the in-flight HUD (<c>ChopperHud.uxml</c>) to the <see cref="ChopperHudModel"/>. Layout and style live in
    /// UXML/USS; this class only sets up bindings and the few class toggles that USS cannot express.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(ChopperHudModelHost))]
    public sealed class ChopperHudView : MonoBehaviour
    {
        private const string HiddenClass = "hud--hidden";
        private const string DashReadyClass = "hud-dash--ready";

        private ChopperHudModel _model;
        private VisualElement _root;
        private VisualElement _dash;

        private void OnEnable()
        {
            _model = GetComponent<ChopperHudModelHost>().Model;
            _root = GetComponent<UIDocument>().rootVisualElement.Q("hud");
            _dash = _root.Q("dash");

            _root.dataSource = _model;
            Bind(_root.Q<Label>("speed-value"), "text", nameof(ChopperHudModel.SpeedText));
            Bind(_root.Q<Label>("altitude-value"), "text", nameof(ChopperHudModel.AltitudeText));
            Bind(_root.Q<ProgressBar>("dash-bar"), "value", nameof(ChopperHudModel.DashCharge));

            _model.propertyChanged += OnModelChanged;
            RefreshClasses();
        }

        private void OnDisable()
        {
            if (_model != null)
                _model.propertyChanged -= OnModelChanged;
        }

        private static void Bind(VisualElement element, string targetProperty, string modelProperty)
        {
            element.SetBinding(targetProperty, new DataBinding
            {
                dataSourcePath = new PropertyPath(modelProperty),
                bindingMode = BindingMode.ToTarget,
            });
        }

        private void OnModelChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            if (args.propertyName == nameof(ChopperHudModel.HasChopper) ||
                args.propertyName == nameof(ChopperHudModel.DashReady))
                RefreshClasses();
        }

        private void RefreshClasses()
        {
            _root.EnableInClassList(HiddenClass, !_model.HasChopper);
            _dash.EnableInClassList(DashReadyClass, _model.DashReady);
        }
    }
}
