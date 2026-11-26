using System;
using Chopper.Presentation.Audio;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Flow
{
    /// <summary>What the transition overlay draws. Written by <see cref="SceneFlow"/>, read each frame by the UI view.</summary>
    public sealed class TransitionModel
    {
        /// <summary>0 = clear, 1 = fully covered.</summary>
        public float Cover { get; internal set; }
        /// <summary>True while the blocks clear (the front runs the other way).</summary>
        public bool Revealing { get; internal set; }
        /// <summary>New per transition: another block pattern each time.</summary>
        public int Seed { get; internal set; }
        /// <summary>Unscaled seconds since the transition started (flicker, scan lines).</summary>
        public float Time { get; internal set; }
        /// <summary>Where we go, e.g. "MAP CITY04".</summary>
        public string Label { get; internal set; } = "";
        /// <summary>What happens now, e.g. "LOADING", "SYNCING".</summary>
        public string Status { get; internal set; } = "";
        public bool Visible => Cover > 0.0001f;
        /// <summary>Match briefing for this transition, or null (menu scenes).</summary>
        public MatchBriefing Briefing { get; internal set; }
        /// <summary>
        /// <see cref="Time"/> when the briefing starts to type: once the new scene has loaded, so the main-thread hitches
        /// of the load (world creation, scene swap) fall before it and the typing stays smooth. Infinity until then.
        /// </summary>
        public float BriefingFrom { get; internal set; } = float.PositiveInfinity;
    }

    /// <summary>
    /// What the load screen tells you about the match you are going into (<see cref="SceneFlow.Load"/>). Made by
    /// <c>PresentationContext.LoadMatch</c> from the map and mode catalogs; the conditions line comes later, when the
    /// server's map announcement arrives. <see cref="Version"/> goes up on each change (the view redraws then).
    /// </summary>
    public sealed class MatchBriefing
    {
        /// <summary>e.g. "CITY04 // CRATER".</summary>
        public string Map = "";
        /// <summary>e.g. "FREE FOR ALL" or "TRAINING".</summary>
        public string Mode = "";
        /// <summary>One line under the mode.</summary>
        public string Tagline = "";
        /// <summary>Rule lines, one per line.</summary>
        public string Rules = "";
        /// <summary>e.g. "2-8 PILOTS"; empty in training.</summary>
        public string Players = "";
        /// <summary>Time of day and weather, e.g. "NIGHT // RAIN"; empty until the server says.</summary>
        public string Conditions = "";
        /// <summary>A tip in sentence case.</summary>
        public string Tip = "";
        public bool Training;
        public int Version { get; internal set; }
    }

    /// <summary>
    /// Scene changes for the whole game (one per game, made by the root scope's entry point; it lives across scenes):
    /// <list type="number">
    /// <item>cover: the transition blocks fill the screen, world sounds fade out (<see cref="MixerVolumes.WorldFade"/>);</item>
    /// <item>load: a callback runs while covered (start or stop the local session), then the scene loads;</item>
    /// <item>wait: the new scene's bridge says when it is ready (<see cref="SceneReady"/>; a match scene when your
    /// chopper has spawned), never longer than the settings' hold time;</item>
    /// <item>reveal: the blocks clear, world sounds fade in.</item>
    /// </list>
    /// The music player lives here too, so music plays on through the change and cross-fades into the new scene's set.
    /// Call <see cref="Tick"/> once per frame with unscaled time.
    /// </summary>
    public sealed class SceneFlow : IDisposable
    {
        private enum Phase
        {
            Idle,
            Covering,
            Loading,
            Waiting,
            Revealing,
        }

        /// <summary>A loaded scene with no bridge does not say it is ready: reveal after this long.</summary>
        private const float NoBridgeSeconds = 1f;

        private readonly SceneTransitionSettings _settings;
        private readonly GameObject _root;
        private readonly AudioSource _stinger;
        private Phase _phase;
        private float _phaseTime;
        private string _target;
        private Action _whenCovered;
        private AsyncOperation _loading;
        private bool _ready;
        private bool _attached;
        private int _seed = 1;
        private int _shownPercent = -1;

        public SceneFlow(SceneTransitionSettings settings, NotificationFeed notices = null)
        {
            _settings = settings;
            Notices = notices ?? new NotificationFeed();
            Notices.Shown += OnNoticeShown;
            _root = new GameObject("Scene Flow");
            Object.DontDestroyOnLoad(_root);
            Music = new MusicPlayer(_root.transform);

            var stinger = new GameObject("Transition Sound");
            stinger.transform.SetParent(_root.transform, false);
            _stinger = stinger.AddComponent<AudioSource>();
            _stinger.playOnAwake = false;
            _stinger.spatialBlend = 0f;
            _stinger.ignoreListenerPause = true;

            if (settings != null && settings.RevealOnBoot)
            {
                // The first scene: start covered and clear in once it is ready.
                Model.Cover = 1f;
                Enter(Phase.Waiting);
            }
        }

        /// <summary>Persistent object: the overlay's panel and the music live under it.</summary>
        public GameObject Root => _root;
        public SceneTransitionSettings Settings => _settings;
        public MusicPlayer Music { get; }
        public MixerVolumes Volumes { get; } = new();
        public TransitionModel Model { get; } = new();
        /// <summary>Toast notices for the whole game (they live across scene changes, as the music does).</summary>
        public NotificationFeed Notices { get; }
        /// <summary>A transition runs: a new <see cref="Load"/> is ignored.</summary>
        public bool Busy => _phase != Phase.Idle;
        public bool IsDisposed { get; private set; }

        /// <summary>A scene's bridge joined (its sound library sets the music output and the mixer).</summary>
        public void Attach(SoundLibrary sounds)
        {
            _attached = true;
            if (sounds == null) return;
            Music.SetOutput(sounds.MusicGroup);
            Volumes.UseMixer(sounds.Mixer);
        }

        /// <summary>
        /// Covers the screen, runs <paramref name="whenCovered"/>, loads <paramref name="scene"/> (single mode) and clears
        /// once it is ready. False (nothing happens) while another transition runs or the scene is not in the build.
        /// </summary>
        /// <param name="label">Shown on the overlay; empty = the scene name.</param>
        public bool Load(string scene, Action whenCovered = null, string label = null, MatchBriefing briefing = null)
        {
            if (Busy || IsDisposed) return false;
            if (string.IsNullOrEmpty(scene) || !Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"Scene flow: scene '{scene}' is not in the build settings.");
                return false;
            }

            _target = scene;
            _whenCovered = whenCovered;
            Model.Label = string.IsNullOrEmpty(label) ? Pretty(scene) : label.ToUpperInvariant();
            Model.Seed = ++_seed;
            Model.Time = 0f;
            Model.Briefing = briefing;
            if (briefing != null)
            {
                if (string.IsNullOrEmpty(briefing.Tip)) briefing.Tip = PickTip(_seed);
                briefing.Version++;
            }
            Enter(Phase.Covering);
            Play(_settings != null ? _settings.CoverSound : null);
            return true;
        }

        /// <summary>The match's time and weather (client, from the server's announcement). Ignored with no briefing.</summary>
        public void SetBriefingConditions(string conditions)
        {
            var briefing = Model.Briefing;
            if (briefing == null || briefing.Conditions == conditions) return;
            briefing.Conditions = conditions ?? "";
            briefing.Version++;
        }

        private string PickTip(int seed)
        {
            var tips = _settings != null ? _settings.Tips : null;
            if (tips == null || tips.Count == 0) return "";
            return tips[(int)((uint)(seed * 2654435761u) % (uint)tips.Count)] ?? "";
        }

        /// <summary>
        /// Runs the whole transition with no scene change (tuning in play mode). False while another one runs. With no
        /// target, the scene that is open says it is ready, so the hold is the same as in a real change.
        /// </summary>
        public bool Preview()
        {
            if (Busy || IsDisposed) return false;
            _target = null;
            _whenCovered = null;
            Model.Label = "PREVIEW";
            Model.Seed = ++_seed;
            Model.Time = 0f;
            // A sample briefing, so the layout can be tuned too.
            Model.Briefing = new MatchBriefing
            {
                Map = "CITY04 // CRATER", Mode = "FREE FOR ALL", Tagline = "EVERY PILOT FOR THEMSELVES",
                Rules = "FIRST TO 15 KILLS\n10 MINUTES", Players = "2-8 PILOTS", Conditions = "NIGHT // RAIN",
                Tip = PickTip(_seed),
            };
            Model.Briefing.Version++;
            Enter(Phase.Covering);
            Play(_settings != null ? _settings.CoverSound : null);
            return true;
        }

        /// <summary>True while the flow waits for <paramref name="scene"/> to say it is ready.</summary>
        public bool WaitingFor(Scene scene) => _phase == Phase.Waiting && (_target == null || scene.name == _target);

        /// <summary>The new scene is ready to show (from its bridge). Ignored for other scenes.</summary>
        private void OnNoticeShown(ShownNotice notice)
        {
            if (_settings == null) return;
            var cue = notice.Kind == NoticeKind.Error && _settings.NoticeErrorSound != null ? _settings.NoticeErrorSound : _settings.NoticeSound;
            Play(cue);
        }

        public void SceneReady(Scene scene)
        {
            if (WaitingFor(scene)) _ready = true;
        }

        public void Tick(float deltaTime)
        {
            if (IsDisposed) return;
            Music.Tick(deltaTime);
            Notices.Tick(deltaTime);
            _phaseTime += deltaTime;
            Model.Time += deltaTime;

            switch (_phase)
            {
                case Phase.Covering:
                    Model.Cover = Ease(_phaseTime / CoverSeconds);
                    Model.Status = "LINK OUT";
                    if (_phaseTime >= CoverSeconds) StartLoad();
                    break;
                case Phase.Loading:
                    Model.Cover = 1f;
                    ShowLoadProgress();
                    if (_loading == null || _loading.isDone)
                    {
                        _loading = null;
                        Enter(Phase.Waiting);
                    }
                    break;
                case Phase.Waiting:
                    Model.Cover = 1f;
                    Model.Status = _phaseTime > 1f ? "SYNCING" : "LINKING";
                    var minHold = _settings != null ? _settings.MinHoldSeconds : 0.25f;
                    // A briefing stays up long enough to read (counted from fully covered, so the load time counts).
                    var briefingWait = Model.Briefing != null && _settings != null &&
                                       Model.Time < CoverSeconds + _settings.BriefingSeconds;
                    var maxHold = _settings != null ? _settings.MaxHoldSeconds : 12f;
                    var noBridge = !_attached && _phaseTime >= NoBridgeSeconds;
                    if ((_ready || noBridge) && _phaseTime >= minHold && !briefingWait || _phaseTime >= maxHold)
                    {
                        if (!_ready && !noBridge) Debug.LogWarning($"Scene flow: '{_target}' did not say it was ready in {maxHold} s; showing it anyway.");
                        Enter(Phase.Revealing);
                        Play(_settings != null ? _settings.RevealSound : null);
                    }
                    break;
                case Phase.Revealing:
                    Model.Cover = 1f - Ease(_phaseTime / RevealSeconds);
                    Model.Status = "LINKED";
                    if (_phaseTime >= RevealSeconds) Enter(Phase.Idle);
                    break;
                default:
                    Model.Cover = 0f;
                    break;
            }

            Model.Revealing = _phase == Phase.Revealing;
            Volumes.WorldFade = 1f - Model.Cover;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Music.Dispose();
            if (_root != null) Object.Destroy(_root);
        }

        private float CoverSeconds => _settings != null ? _settings.CoverSeconds : 0.55f;
        private float RevealSeconds => _settings != null ? _settings.RevealSeconds : 0.75f;

        private void StartLoad()
        {
            if (_target == null)
            {
                Enter(Phase.Waiting);   // Preview: nothing to load.
                return;
            }

            var callback = _whenCovered;
            _whenCovered = null;
            try
            {
                callback?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            _attached = false;
            _loading = SceneManager.LoadSceneAsync(_target, LoadSceneMode.Single);
            Enter(Phase.Loading);
        }

        /// <summary>"LOADING 40%": a new string only when the number changes.</summary>
        private void ShowLoadProgress()
        {
            var percent = _loading != null ? Mathf.RoundToInt(Mathf.Clamp01(_loading.progress / 0.9f) * 100f) : 0;
            if (percent == _shownPercent && Model.Status.StartsWith("LOADING", StringComparison.Ordinal)) return;
            _shownPercent = percent;
            Model.Status = $"LOADING {percent}%";
        }

        private void Enter(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
            if (phase == Phase.Covering) Model.BriefingFrom = float.PositiveInfinity;
            if (phase == Phase.Waiting)
            {
                _ready = false;
                Model.BriefingFrom = Model.Time;
            }
            if (phase == Phase.Idle)
            {
                _target = null;
                Model.Briefing = null;
            }
        }

        private void Play(SoundCue cue)
        {
            if (cue == null || !cue.HasClips || _stinger == null) return;
            cue.Apply(_stinger);
            _stinger.spatialBlend = 0f;
            _stinger.pitch = cue.PickPitch();
            _stinger.PlayOneShot(cue.PickClip(), cue.PickVolume());
        }

        /// <summary>Smooth start and end.</summary>
        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>"Map_City04" → "MAP CITY04", "TrainingRange" → "TRAINING RANGE".</summary>
        public static string Pretty(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return "";
            var text = new System.Text.StringBuilder(scene.Length + 4);
            for (var i = 0; i < scene.Length; i++)
            {
                var c = scene[i];
                if (c == '_' || c == '-') { text.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && char.IsLower(scene[i - 1])) text.Append(' ');
                text.Append(char.ToUpperInvariant(c));
            }
            return text.ToString();
        }
    }
}
