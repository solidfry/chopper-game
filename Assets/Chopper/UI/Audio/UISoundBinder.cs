using Chopper.Presentation.Audio;
using Chopper.Presentation.Bridge;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Audio
{
    /// <summary>
    /// Put next to a <see cref="UIDocument"/>: every button in it plays the UI click sound when pressed, and the
    /// navigate sound when the pointer moves onto it or it gets focus (keyboard / gamepad). Replaces the old
    /// <c>UIAudioClipRequester</c> and <c>UIAudioManager</c>. One binder covers the whole document.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UISoundBinder : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene presentation bridge; it owns the audio service and the sound library.")]
        private PresentationBridge bridge;

        private PresentationContext _context;
        private VisualElement _root;
        private VisualElement _lastHovered;

        private void OnEnable()
        {
            if (bridge == null)
            {
                Debug.LogError($"{name}: no presentation bridge set. Run Chopper → Setup.", this);
                enabled = false;
                return;
            }
            _context = bridge.Context;
            _root = GetComponent<UIDocument>().rootVisualElement;
            // Trickle down: the root sees the event before the button handles it.
            _root.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerOverEvent>(OnPointerOver, TrickleDown.TrickleDown);
            _root.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
        }

        private void OnDisable()
        {
            if (_root == null) return;
            _root.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerOverEvent>(OnPointerOver, TrickleDown.TrickleDown);
            _root.UnregisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
        }

        private void OnClick(ClickEvent evt)
        {
            var button = ButtonFrom(evt.target);
            if (button != null && button.enabledInHierarchy)
                Play(_context.Sounds != null ? _context.Sounds.UIClick : null);
        }

        private void OnPointerOver(PointerOverEvent evt)
        {
            var button = ButtonFrom(evt.target);
            // Pointer-over fires again for each child of the button; play once per button.
            if (button == null || button == _lastHovered) return;
            _lastHovered = button;
            Play(_context.Sounds != null ? _context.Sounds.UINavigate : null);
        }

        private void OnFocusIn(FocusInEvent evt)
        {
            var button = ButtonFrom(evt.target);
            if (button == null || button == _lastHovered) return;
            _lastHovered = button;
            Play(_context.Sounds != null ? _context.Sounds.UINavigate : null);
        }

        private void Play(SoundCue cue)
        {
            if (cue != null) _context.Audio.Play(cue);
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

        /// <summary>The button the event is on, also when it hit the button's label or icon.</summary>
        private static Button ButtonFrom(IEventHandler target)
        {
            for (var element = target as VisualElement; element != null; element = element.parent)
                if (element is Button button) return button;
            return null;
        }
    }
}
