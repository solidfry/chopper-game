using System;
using System.Collections.Generic;

namespace Chopper.Presentation.Flow
{
    /// <summary>Kind of a notice: sets its accent (palette) and icon mark.</summary>
    public enum NoticeKind
    {
        /// <summary>Console Green: plain news.</summary>
        Info,
        /// <summary>Console Green, filled mark: something you did worked (pilot set, build saved).</summary>
        Success,
        /// <summary>Lightning Yellow: works, but not fully (offline mode).</summary>
        Warning,
        /// <summary>Screamer Red: it failed (matchmaking, connection).</summary>
        Error,
        /// <summary>Cruise Pink: an unlock or a level.</summary>
        Reward,
    }

    /// <summary>One notice on screen. <see cref="Age"/> counts up; the view reads it for the timer bar.</summary>
    public sealed class ShownNotice
    {
        internal ShownNotice(int id, string title, string text, NoticeKind kind, float seconds)
        {
            Id = id;
            Title = title;
            Text = text;
            Kind = kind;
            Seconds = seconds;
        }

        public int Id { get; }
        public string Title { get; }
        public string Text { get; }
        public NoticeKind Kind { get; }
        /// <summary>How long it stays (unscaled seconds).</summary>
        public float Seconds { get; }
        public float Age { get; internal set; }
        /// <summary>The same notice came again while shown: "x2", "x3".</summary>
        public int Count { get; internal set; } = 1;
        /// <summary>0 when new, 1 when it goes.</summary>
        public float Progress => Seconds > 0f ? Math.Min(1f, Age / Seconds) : 1f;
    }

    /// <summary>
    /// Toast notices for the whole game. One feed lives on <see cref="SceneFlow"/> (root scope), so a notice posted
    /// just before a scene change still shows in the next scene. At most <see cref="MaxShown"/> show at once; more wait
    /// in a queue. A notice equal to one on screen (title and text) does not stack: the shown one restarts and counts up.
    /// Pure C#: call <see cref="Tick"/> once per frame with unscaled time. Post through
    /// <c>PresentationContext.Notify</c> from scene code.
    /// </summary>
    public sealed class NotificationFeed
    {
        public const int MaxShown = 3;
        public const int MaxQueued = 8;
        public const float DefaultSeconds = 4.5f;

        private readonly List<ShownNotice> _shown = new();
        private readonly Queue<ShownNotice> _queue = new();
        private int _nextId = 1;

        /// <summary>A notice came on screen (new, or one that waited).</summary>
        public event Action<ShownNotice> Shown;
        /// <summary>A notice left the screen (timed out or dismissed).</summary>
        public event Action<ShownNotice> Hidden;
        /// <summary>A shown notice came again: its count and timer changed.</summary>
        public event Action<ShownNotice> Repeated;

        public IReadOnlyList<ShownNotice> Visible => _shown;
        public int Queued => _queue.Count;

        /// <summary>Shows a notice (or queues it). <paramref name="seconds"/> 0 = <see cref="DefaultSeconds"/>.</summary>
        public void Post(string title, string text = null, NoticeKind kind = NoticeKind.Info, float seconds = 0f)
        {
            title = string.IsNullOrEmpty(title) ? "" : title.ToUpperInvariant();
            text ??= "";
            if (title.Length == 0 && text.Length == 0) return;
            if (seconds <= 0f) seconds = DefaultSeconds;

            foreach (var shown in _shown)
            {
                if (shown.Title != title || shown.Text != text) continue;
                shown.Age = 0f;
                shown.Count++;
                Repeated?.Invoke(shown);
                return;
            }
            foreach (var waiting in _queue)
                if (waiting.Title == title && waiting.Text == text) return;

            var notice = new ShownNotice(_nextId++, title, text, kind, seconds);
            if (_shown.Count < MaxShown) Show(notice);
            else if (_queue.Count < MaxQueued) _queue.Enqueue(notice);
        }

        public void Tick(float deltaTime)
        {
            for (var i = _shown.Count - 1; i >= 0; i--)
            {
                var notice = _shown[i];
                notice.Age += deltaTime;
                if (notice.Age >= notice.Seconds) Remove(i);
            }
            while (_shown.Count < MaxShown && _queue.Count > 0) Show(_queue.Dequeue());
        }

        /// <summary>Removes a shown notice now (clicked away). False when it is not shown.</summary>
        public bool Dismiss(int id)
        {
            var index = _shown.FindIndex(n => n.Id == id);
            if (index < 0) return false;
            Remove(index);
            return true;
        }

        /// <summary>Removes every notice, shown and queued (no events for the queued ones).</summary>
        public void Clear()
        {
            _queue.Clear();
            for (var i = _shown.Count - 1; i >= 0; i--) Remove(i);
        }

        private void Show(ShownNotice notice)
        {
            _shown.Add(notice);
            Shown?.Invoke(notice);
        }

        private void Remove(int index)
        {
            var notice = _shown[index];
            _shown.RemoveAt(index);
            Hidden?.Invoke(notice);
        }
    }
}
