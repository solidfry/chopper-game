using System;
using UnityEngine.UIElements;

namespace Chopper.UI
{
    /// <summary>
    /// The shared window open and close: a CRT screen that powers on and off (theme classes <c>chopper-window*</c> in
    /// ChopperTheme.uss).
    /// Open: a thin bright line grows out from the middle, then opens up into the window while the glow fades.
    /// Close: the window falls in to a bright line, then the line shrinks to the middle and goes out.
    /// Works on any element that a class hides (<c>display: none</c>); it adds a beam child (the glow) on first use.
    /// A new open stops a close that is still running, and the other way round.
    /// </summary>
    public static class WindowTransition
    {
        public const string WindowClass = "chopper-window";
        public const string OffClass = "chopper-window--off";
        public const string LineClass = "chopper-window--line";
        public const string ClosingClass = "chopper-window--closing";
        public const string SnapClass = "chopper-window--snap";
        public const string BeamClass = "chopper-window__beam";

        /// <summary>Milliseconds of each step; the same as the transition durations in ChopperTheme.uss.</summary>
        private const long LineMs = 120;
        private const long CollapseMs = 130;
        /// <summary>One frame: lets the start pose show before the transition starts.</summary>
        private const long FrameMs = 16;

        /// <summary>Shows the window (removes <paramref name="hiddenClass"/>) and plays the open.</summary>
        public static void Open(VisualElement window, string hiddenClass)
        {
            if (window == null) return;
            Prepare(window);
            window.RemoveFromClassList(ClosingClass);
            // Start pose with no transition: nothing but a dark point in the middle.
            window.AddToClassList(SnapClass);
            window.AddToClassList(OffClass);
            window.RemoveFromClassList(LineClass);
            window.RemoveFromClassList(hiddenClass);
            window.schedule.Execute(() =>
            {
                if (window.ClassListContains(ClosingClass)) return;
                window.RemoveFromClassList(SnapClass);
                window.RemoveFromClassList(OffClass);
                window.AddToClassList(LineClass);
            }).StartingIn(FrameMs);
            window.schedule.Execute(() =>
            {
                if (!window.ClassListContains(ClosingClass)) window.RemoveFromClassList(LineClass);
            }).StartingIn(FrameMs + LineMs);
        }

        /// <summary>Plays the close, then hides the window (adds <paramref name="hiddenClass"/>) and calls <paramref name="closed"/>.</summary>
        public static void Close(VisualElement window, string hiddenClass, Action closed = null)
        {
            if (window == null) return;
            if (window.ClassListContains(hiddenClass))
            {
                closed?.Invoke();
                return;
            }
            Prepare(window);
            window.RemoveFromClassList(SnapClass);
            window.RemoveFromClassList(OffClass);
            window.AddToClassList(ClosingClass);
            window.AddToClassList(LineClass);
            window.schedule.Execute(() =>
            {
                if (window.ClassListContains(ClosingClass)) window.AddToClassList(OffClass);
            }).StartingIn(CollapseMs);
            window.schedule.Execute(() =>
            {
                if (!window.ClassListContains(ClosingClass)) return;
                window.AddToClassList(hiddenClass);
                window.RemoveFromClassList(ClosingClass);
                window.RemoveFromClassList(LineClass);
                window.RemoveFromClassList(OffClass);
                closed?.Invoke();
            }).StartingIn(CollapseMs + LineMs);
        }

        /// <summary>The window class, and the beam (the glow) as the top child.</summary>
        private static void Prepare(VisualElement window)
        {
            window.AddToClassList(WindowClass);
            var beam = window.Q(className: BeamClass);
            if (beam == null)
            {
                beam = new VisualElement { pickingMode = PickingMode.Ignore };
                beam.AddToClassList(BeamClass);
                window.Add(beam);
            }
            else beam.BringToFront();
        }
    }
}
