using System;
using Chopper.Presentation.Hud;
using UnityEngine.UIElements;

namespace Chopper.UI.Hud
{
    /// <summary>
    /// Draws the <see cref="WorldMarkerModel"/>: a fixed pool of marker elements (made once, never per frame) and the
    /// lead indicator. Layout and colours are in <c>ChopperHud.uss</c> (<c>hud-marker*</c>, <c>hud-lead*</c>).
    /// Made and disposed by <see cref="ChopperHudView"/>.
    /// </summary>
    public sealed class WorldMarkerView : IDisposable
    {
        private const string MarkerClass = "hud-marker";
        private const string HiddenClass = "hud-marker--hidden";
        private const string GroundClass = "hud-marker--ground";
        private const string OffScreenClass = "hud-marker--offscreen";
        private const string LockedClass = "hud-marker--locked";
        private const string NoHealthClass = "hud-marker--no-health";
        private const string LeadHiddenClass = "hud-lead--hidden";

        private readonly WorldMarkerModel _model;
        private readonly VisualElement _lead;
        private readonly Slot[] _slots = new Slot[WorldMarkerModel.Capacity];

        private sealed class Slot
        {
            public VisualElement Root;
            public VisualElement Arrow;
            public Label Distance;
            public VisualElement HealthFill;
            public int ShownDistance = -1;
        }

        public WorldMarkerView(WorldMarkerModel model, VisualElement layer, VisualElement lead)
        {
            _model = model;
            _lead = lead;
            for (var i = 0; i < _slots.Length; i++)
                _slots[i] = CreateSlot(layer);

            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private static Slot CreateSlot(VisualElement layer)
        {
            var slot = new Slot { Root = Element(MarkerClass, HiddenClass) };
            slot.Root.Add(Element("hud-marker__icon"));
            slot.Arrow = Element("hud-marker__arrow");
            slot.Root.Add(slot.Arrow);
            slot.Distance = new Label { pickingMode = PickingMode.Ignore };
            slot.Distance.AddToClassList("hud-marker__distance");
            slot.Root.Add(slot.Distance);
            var health = Element("hud-marker__health");
            slot.HealthFill = Element("hud-marker__health-fill");
            health.Add(slot.HealthFill);
            slot.Root.Add(health);
            layer.Add(slot.Root);
            return slot;
        }

        private static VisualElement Element(params string[] classes)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (var name in classes) element.AddToClassList(name);
            return element;
        }

        private void Refresh()
        {
            var markers = _model.Markers;
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                var visible = i < markers.Length;
                slot.Root.EnableInClassList(HiddenClass, !visible);
                if (visible) Show(slot, markers[i]);
            }

            _lead.EnableInClassList(LeadHiddenClass, !_model.LeadVisible);
            if (!_model.LeadVisible) return;
            _lead.style.left = Length.Percent(_model.LeadViewport.x * 100f);
            _lead.style.top = Length.Percent((1f - _model.LeadViewport.y) * 100f);
        }

        /// <summary>Viewport y goes up, panel top goes down.</summary>
        private static void Show(Slot slot, in WorldMarker marker)
        {
            var root = slot.Root;
            root.style.left = Length.Percent(marker.Viewport.x * 100f);
            root.style.top = Length.Percent((1f - marker.Viewport.y) * 100f);
            root.EnableInClassList(GroundClass, marker.Kind == WorldMarkerKind.Ground);
            root.EnableInClassList(OffScreenClass, !marker.OnScreen);
            root.EnableInClassList(LockedClass, marker.IsLockTarget);
            root.EnableInClassList(NoHealthClass, marker.HealthFraction < 0f);

            if (!marker.OnScreen) slot.Arrow.style.rotate = new Rotate(Angle.Degrees(marker.EdgeAngleDegrees));
            if (marker.HealthFraction >= 0f) slot.HealthFill.style.width = Length.Percent(marker.HealthFraction * 100f);

            // Text only when it changes: no string per marker per frame.
            if (slot.ShownDistance == marker.Distance) return;
            slot.ShownDistance = marker.Distance;
            slot.Distance.text = TabularText.Wrap(marker.Distance.ToString());
        }
    }
}
