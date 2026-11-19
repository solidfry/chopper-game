using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Chopper.Editor.AudioEffects
{
    /// <summary>
    /// Cyberpunk Audio Processor - Editor window.
    /// Select sounds, tune the effect chain, auto-preview, export to a new folder.
    /// </summary>
    public class CyberpunkAudioProcessor : EditorWindow
    {
        // ── Layout constants ───────────────────────────────────
        const float kLeftWidth = 400f;
        const float kRightMinWidth = 560f;
        const float kColumnGap = 10f;
        const float kLabelWidth = 108f;
        const float kDockMinWidth = 330f;
        const float kDockMaxWidth = 460f;
        const float kIconButton = 28f;
        const float kWaveformMinHeight = 120f;
        const float kWaveformMaxHeight = 240f;
        static readonly Color kAccent = new Color(0.78f, 0.35f, 1f, 1f);
        static readonly Color kAccentDim = new Color(0.45f, 0.25f, 0.7f, 1f);
        static readonly Color kRowEven = new Color(0.20f, 0.18f, 0.26f, 0.55f);
        static readonly Color kRowOdd = new Color(0.16f, 0.15f, 0.21f, 0.55f);
        static readonly Color kRowPlaying = new Color(0.45f, 0.20f, 0.75f, 0.85f);

        // ── State ──────────────────────────────────────────────
        Vector2 _listScroll, _effectScroll, _exportLogScroll;
        string _sourceFolder = "Assets/ThirdParty/Audio/Gun & Explosion Sounds";
        string _exportFolder = "Assets/ThirdParty/Audio/Gun & Explosion Sounds/Processed";
        AudioEffectChain _chain;

        List<string> _allFiles = new List<string>();
        bool[] _selected;
        string _previewNameFilter = "";

        // Preview (driven by Unity's internal AudioUtil preview API)
        int _previewIndex = -1;
        string _previewSourcePath = "";
        AudioClip _previewClip;
        bool _previewOwnedClip;
        float[] _waveform;
        bool _previewPlaying;
        float _previewPosition;
        bool _autoAdvance;
        bool _loopPreview;
        bool _showOriginal = false;
        string _previewError = "";

        // Effects UI foldouts
        bool _showEq = true, _showDistortion = true, _showBitcrush = true, _showCompression = true;
        bool _showChorus, _showFlanger, _showWah, _showDelay, _showReverb, _showPitch;

        // Export
        string _exportLog = "";
        int _exportProgress, _exportTotal;
        bool _isExporting;

        readonly string[] _filterExtensions = { ".wav", ".mp3", ".ogg", ".aiff", ".flac" };

        // ───────────────────────────────────────────────────────
        [MenuItem("Tools/Audio/Cyberpunk Audio Processor")]
        public static void ShowWindow()
        {
            var window = GetWindow<CyberpunkAudioProcessor>("Cyberpunk Audio");
            window.minSize = new Vector2(820, 560);
            window.Show();
        }

        void OnEnable()
        {
            if (_chain == null)
                _chain = CreateInstance<AudioEffectChain>();
            LoadFiles();
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopPreview();
            DisposeTempClip();
        }

        /// <summary>
        /// Releases the currently previewed clip.
        ///
        /// Ownership is tracked explicitly rather than inferred: <see cref="_previewOwnedClip"/>
        /// is only set for clips this tool generated. In bypass ("Original") mode the
        /// preview points straight at a project asset, which must never be destroyed -
        /// inferring ownership from the asset database was unreliable and could free the
        /// clip that was still being played, which silenced the preview.
        /// </summary>
        void DisposeTempClip()
        {
            if (_previewClip == null)
            {
                _previewOwnedClip = false;
                return;
            }

            if (_previewOwnedClip)
            {
                EditorAudioPreview.Stop();
                DestroyImmediate(_previewClip);
            }

            _previewClip = null;
            _previewOwnedClip = false;
        }

        void OnEditorUpdate()
        {
            if (!_previewPlaying) return;

            bool stillPlaying = EditorAudioPreview.IsPlaying();
            _previewPosition = EditorAudioPreview.GetPosition();

            if (!stillPlaying)
            {
                _previewPlaying = false;
                if (_autoAdvance) PlayFromSelection(_previewIndex + 1);
            }

            Repaint();
        }

        // ── File list ──────────────────────────────────────────
        void LoadFiles()
        {
            _allFiles.Clear();

            if (!string.IsNullOrEmpty(_sourceFolder) && Directory.Exists(ToAbsolute(_sourceFolder)))
            {
                foreach (var ext in _filterExtensions)
                {
                    foreach (var f in Directory.GetFiles(ToAbsolute(_sourceFolder), "*" + ext, SearchOption.AllDirectories))
                        _allFiles.Add(ToRelative(f));
                }
                _allFiles.Sort(StringComparer.OrdinalIgnoreCase);
            }

            _selected = new bool[_allFiles.Count];
            _previewIndex = -1;
        }

        static string ToAbsolute(string assetPath) =>
            assetPath.StartsWith("Assets") ? assetPath.Replace("Assets", Application.dataPath) : assetPath;

        static string ToRelative(string fullPath)
        {
            string p = fullPath.Replace('\\', '/');
            int i = p.IndexOf("/Assets/", StringComparison.Ordinal);
            if (i >= 0) return p.Substring(i + 1);
            if (p.StartsWith(Application.dataPath.Replace('\\', '/')))
                return "Assets" + p.Substring(Application.dataPath.Length);
            return p;
        }

        bool IsVisible(int i)
        {
            if (string.IsNullOrEmpty(_previewNameFilter)) return true;
            return _allFiles[i].IndexOf(_previewNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── GUI ────────────────────────────────────────────────
        void OnGUI()
        {
            // Narrow label column: keeps parameter names next to their slider/value
            // instead of leaving a wide gap on wide windows.
            EditorGUIUtility.labelWidth = kLabelWidth;

            DrawHeader();
            DrawFolderBar();

            // Layout: file list | effect grid | player over export
            float dockWidth = Mathf.Clamp(position.width * 0.30f, kDockMinWidth, kDockMaxWidth);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(kLeftWidth), GUILayout.ExpandHeight(true));
            DrawSoundList();
            EditorGUILayout.EndVertical();

            GUILayout.Space(kColumnGap);
            DrawSeparator();
            GUILayout.Space(kColumnGap);

            // Centre: effect grid takes the remaining width
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawEffectChain();
            EditorGUILayout.EndVertical();

            GUILayout.Space(kColumnGap);
            DrawSeparator();
            GUILayout.Space(kColumnGap);

            // Right dock: player above, export below - fills the previously empty area
            EditorGUILayout.BeginVertical(GUILayout.Width(dockWidth), GUILayout.ExpandHeight(true));
            DrawPreviewBar();
            GUILayout.Space(6);
            DrawHorizontalRule();
            GUILayout.Space(4);
            DrawExportBar();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
        }

        void DrawHorizontalRule()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, kAccentDim);
        }

        void DrawStatusBar()
        {
            Rect line = EditorGUILayout.GetControlRect(false, 2f);
            EditorGUI.DrawRect(line, kAccentDim);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(_previewSourcePath) ? "No clip previewed" : "Clip: " + Path.GetFileName(_previewSourcePath),
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(_previewPlaying ? "PLAYING" : "IDLE", EditorStyles.miniBoldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField(_showOriginal ? "ORIGINAL (chain bypassed)" : "PROCESSED", EditorStyles.miniLabel, GUILayout.Width(170));
            EditorGUILayout.LabelField(
                Application.isPlaying ? "Play mode" : "Edit mode",
                EditorStyles.miniLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
        }

        void DrawHeader()
        {
            Color old = GUI.color;
            GUI.color = kAccent;
            EditorGUILayout.LabelField("CYBERPUNK AUDIO PROCESSOR", EditorStyles.boldLabel);
            GUI.color = old;

            Rect line = EditorGUILayout.GetControlRect(false, 2f);
            EditorGUI.DrawRect(line, kAccentDim);
            EditorGUILayout.Space(4);
        }

        void DrawSeparator()
        {
            Rect column = EditorGUILayout.BeginVertical(GUILayout.Width(2f), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(new Rect(column.x, column.y, 2f, column.height), kAccentDim);
            EditorGUILayout.EndVertical();
        }

        void DrawFolderBar()
        {
            EditorGUILayout.BeginHorizontal();
            _sourceFolder = EditorGUILayout.TextField("Source", _sourceFolder, GUILayout.MinWidth(220));
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFolderPanel("Source folder", "Assets", "");
                if (!string.IsNullOrEmpty(p)) { _sourceFolder = ToRelative(p); LoadFiles(); }
            }
            if (GUILayout.Button("Reload", GUILayout.Width(70)))
                LoadFiles();

            _exportFolder = EditorGUILayout.TextField("Export", _exportFolder, GUILayout.MinWidth(220));
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFolderPanel("Export folder", "Assets", "");
                if (!string.IsNullOrEmpty(p)) _exportFolder = ToRelative(p);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Left: sound list ───────────────────────────────────
        void DrawSoundList()
        {
            int selectedCount = 0;
            for (int i = 0; i < _selected.Length; i++) if (_selected[i]) selectedCount++;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{_allFiles.Count} clips", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField($"{selectedCount} selected", EditorStyles.miniLabel, GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(44)))
                for (int i = 0; i < _selected.Length; i++) _selected[i] = true;
            if (GUILayout.Button("None", EditorStyles.miniButtonMid, GUILayout.Width(50)))
                for (int i = 0; i < _selected.Length; i++) _selected[i] = false;
            if (GUILayout.Button("Invert", EditorStyles.miniButtonRight, GUILayout.Width(52)))
                for (int i = 0; i < _selected.Length; i++) _selected[i] = !_selected[i];
            EditorGUILayout.EndHorizontal();

            _previewNameFilter = EditorGUILayout.TextField(_previewNameFilter, EditorStyles.toolbarSearchField);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            float rowHeight = 20f;
            for (int i = 0; i < _allFiles.Count; i++)
            {
                if (!IsVisible(i)) continue;

                bool isPlaying = _previewPlaying && i == _previewIndex;
                Rect row = EditorGUILayout.BeginHorizontal(GUILayout.Height(rowHeight));

                if (isPlaying) EditorGUI.DrawRect(row, kRowPlaying);
                else if (i % 2 == 0) EditorGUI.DrawRect(row, kRowEven);
                else EditorGUI.DrawRect(row, kRowOdd);

                // Same Unity transport icon as the player: play, or pause/stop the active clip
                var rowIcon = isPlaying ? TransportIcon.Pause : TransportIcon.Play;
                if (IconButton(rowIcon, isPlaying ? "Stop this clip" : "Preview this clip", size: rowHeight - 2f))
                {
                    if (isPlaying) StopPreview();
                    else PreviewAt(i);
                }

                _selected[i] = EditorGUILayout.ToggleLeft(
                    Path.GetFileName(_allFiles[i]), _selected[i], GUILayout.Height(rowHeight));

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        // ── Right: effects + preview + export ──────────────────
        void DrawEffectChain()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("EFFECT CHAIN", EditorStyles.boldLabel, GUILayout.Width(100));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset", EditorStyles.miniButtonLeft, GUILayout.Width(52)))
                _chain = CreateInstance<AudioEffectChain>();
            if (GUILayout.Button("Save", EditorStyles.miniButtonMid, GUILayout.Width(46)))
                SaveChain();
            if (GUILayout.Button("Load", EditorStyles.miniButtonRight, GUILayout.Width(46)))
                LoadChain();
            EditorGUILayout.EndHorizontal();

            DrawPresetBar();

            _effectScroll = EditorGUILayout.BeginScrollView(_effectScroll, GUILayout.ExpandHeight(true));
            DrawEffectGrid();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Accordion header with an inline enable toggle on the left, the title in the
        /// middle and an optional state suffix (e.g. "2/3") on the right.
        /// Clicking anywhere on the row toggles the section open/closed.
        /// </summary>
        static bool Section(string title, bool expanded, ref bool enabled, string suffix = null, Action onToggle = null)
        {
            const float rowHeight = 20f;
            const float toggleWidth = 18f;

            Rect row = EditorGUILayout.GetControlRect(false, rowHeight);

            // Background + hover highlight
            bool hover = row.Contains(Event.current.mousePosition);
            Color bg = enabled
                ? new Color(0.30f, 0.24f, 0.42f, hover ? 0.95f : 0.80f)
                : new Color(0.19f, 0.17f, 0.24f, hover ? 0.95f : 0.70f);
            EditorGUI.DrawRect(row, bg);

            // Enable toggle
            Rect toggleRect = new Rect(row.x + 4f, row.y + Mathf.Round((rowHeight - 14f) * 0.5f), toggleWidth, 14f);
            bool newEnabled = EditorGUI.Toggle(toggleRect, enabled);
            if (newEnabled != enabled)
            {
                enabled = newEnabled;
                if (onToggle != null) onToggle();
            }

            // Title (dimmed when the stage is off)
            Rect labelRect = new Rect(toggleRect.xMax + 4f, row.y, row.width - toggleWidth - 40f, rowHeight);
            Color oldColor = GUI.contentColor;
            GUI.contentColor = enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
            GUI.contentColor = oldColor;

            // Optional right-aligned state suffix (e.g. "2/3")
            if (!string.IsNullOrEmpty(suffix))
            {
                Rect suffixRect = new Rect(row.xMax - 38f, row.y, 34f, rowHeight);
                EditorGUI.LabelField(suffixRect, suffix, EditorStyles.miniLabel);
            }

            // Foldout arrow + click-to-expand on the remainder of the row
            Rect arrowRect = new Rect(row.xMax - 16f, row.y, 16f, rowHeight);
            bool newExpanded = EditorGUI.Foldout(arrowRect, expanded, GUIContent.none, false);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition) &&
                !toggleRect.Contains(Event.current.mousePosition) && !arrowRect.Contains(Event.current.mousePosition))
            {
                newExpanded = !expanded;
                Event.current.Use();
            }

            return newExpanded;
        }

        // ── Effect sections ────────────────────────────────────
        // The enable toggle lives on the accordion header so a stage can be
        // switched on/off without expanding it.

        void DrawEqSection()
        {
            int on = (_chain.eqLowEnabled ? 1 : 0) + (_chain.eqMidEnabled ? 1 : 0) + (_chain.eqHighEnabled ? 1 : 0);
            bool any = on > 0;

            string suffix = on == 3 ? "all" : on == 0 ? "off" : on + "/3";
            bool open = Section("EQ", _showEq, ref any, suffix, () =>
            {
                bool enableAll = !(_chain.eqLowEnabled && _chain.eqMidEnabled && _chain.eqHighEnabled);
                _chain.eqLowEnabled = enableAll;
                _chain.eqMidEnabled = enableAll;
                _chain.eqHighEnabled = enableAll;
            });
            _showEq = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.eqLowEnabled = EditorGUILayout.Toggle("Low Shelf", _chain.eqLowEnabled);
            _chain.eqLowFreq = EditorGUILayout.Slider("  Freq", _chain.eqLowFreq, 20f, 500f);
            _chain.eqLowGain = EditorGUILayout.Slider("  Gain", _chain.eqLowGain, -18f, 18f);

            _chain.eqMidEnabled = EditorGUILayout.Toggle("Peaking", _chain.eqMidEnabled);
            _chain.eqMidFreq = EditorGUILayout.Slider("  Freq", _chain.eqMidFreq, 200f, 8000f);
            _chain.eqMidGain = EditorGUILayout.Slider("  Gain", _chain.eqMidGain, -18f, 18f);
            _chain.eqMidQ = EditorGUILayout.Slider("  Q", _chain.eqMidQ, 0.3f, 8f);

            _chain.eqHighEnabled = EditorGUILayout.Toggle("High Shelf", _chain.eqHighEnabled);
            _chain.eqHighFreq = EditorGUILayout.Slider("  Freq", _chain.eqHighFreq, 2000f, 20000f);
            _chain.eqHighGain = EditorGUILayout.Slider("  Gain", _chain.eqHighGain, -18f, 18f);
            EditorGUI.indentLevel--;
        }

        void DrawDistortionSection()
        {
            bool open = Section("Distortion + Saturation", _showDistortion, ref _chain.distortionEnabled, null, () =>
            {
                // Header drives distortion; keep saturation in step so the stage is coherent.
                _chain.saturationEnabled = _chain.distortionEnabled;
            });
            _showDistortion = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.distortionEnabled = EditorGUILayout.Toggle("Distortion", _chain.distortionEnabled);
            _chain.distortionDrive = EditorGUILayout.Slider("  Drive", _chain.distortionDrive, 0f, 1f);
            _chain.distortionMix = EditorGUILayout.Slider("  Mix", _chain.distortionMix, 0f, 1f);

            _chain.saturationEnabled = EditorGUILayout.Toggle("Saturation", _chain.saturationEnabled);
            _chain.saturationDrive = EditorGUILayout.Slider("  Drive", _chain.saturationDrive, 0f, 10f);
            _chain.saturationMix = EditorGUILayout.Slider("  Mix", _chain.saturationMix, 0f, 1f);
            EditorGUI.indentLevel--;
        }

        void DrawBitcrushSection()
        {
            bool open = Section("Bitcrush", _showBitcrush, ref _chain.bitcrushEnabled);
            _showBitcrush = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.bitcrushBits = EditorGUILayout.IntSlider("Bits", _chain.bitcrushBits, 1, 16);
            _chain.bitcrushSampleRate = EditorGUILayout.IntSlider("Sample Rate", _chain.bitcrushSampleRate, 2000, 44100);
            EditorGUI.indentLevel--;
        }

        void DrawCompressionSection()
        {
            bool open = Section("Compression", _showCompression, ref _chain.compressionEnabled);
            _showCompression = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.compressionThreshold = EditorGUILayout.Slider("Threshold", _chain.compressionThreshold, -60f, 0f);
            _chain.compressionRatio = EditorGUILayout.Slider("Ratio", _chain.compressionRatio, 1f, 20f);
            _chain.compressionAttack = EditorGUILayout.Slider("Attack", _chain.compressionAttack, 0.001f, 0.3f);
            _chain.compressionRelease = EditorGUILayout.Slider("Release", _chain.compressionRelease, 0.01f, 1f);
            _chain.compressionMix = EditorGUILayout.Slider("Mix", _chain.compressionMix, 0f, 1f);
            EditorGUI.indentLevel--;
        }

        void DrawChorusSection()
        {
            bool open = Section("Chorus", _showChorus, ref _chain.chorusEnabled);
            _showChorus = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.chorusRate = EditorGUILayout.Slider("Rate", _chain.chorusRate, 0.1f, 5f);
            _chain.chorusDepth = EditorGUILayout.Slider("Depth (ms)", _chain.chorusDepth, 0f, 20f);
            _chain.chorusMix = EditorGUILayout.Slider("Mix", _chain.chorusMix, 0f, 1f);
            EditorGUI.indentLevel--;
        }

        void DrawFlangerSection()
        {
            bool open = Section("Flanger", _showFlanger, ref _chain.flangerEnabled);
            _showFlanger = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.flangerRate = EditorGUILayout.Slider("Rate", _chain.flangerRate, 0.1f, 10f);
            _chain.flangerDepth = EditorGUILayout.Slider("Depth (ms)", _chain.flangerDepth, 0f, 6f);
            _chain.flangerFeedback = EditorGUILayout.Slider("Feedback", _chain.flangerFeedback, 0f, 0.9f);
            _chain.flangerMix = EditorGUILayout.Slider("Mix", _chain.flangerMix, 0f, 1f);
            EditorGUI.indentLevel--;
        }

        void DrawWahSection()
        {
            bool open = Section("Wah-Wah", _showWah, ref _chain.wahEnabled);
            _showWah = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.wahRate = EditorGUILayout.Slider("Rate", _chain.wahRate, 0.1f, 10f);
            _chain.wahDepth = EditorGUILayout.Slider("Depth (Hz)", _chain.wahDepth, 100f, 2000f);
            _chain.wahResonance = EditorGUILayout.Slider("Resonance", _chain.wahResonance, 0.7f, 12f);
            _chain.wahMix = EditorGUILayout.Slider("Mix", _chain.wahMix, 0f, 1f);
            EditorGUI.indentLevel--;
        }

        void DrawDelaySection()
        {
            bool open = Section("Delay", _showDelay, ref _chain.delayEnabled);
            _showDelay = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.delayTime = EditorGUILayout.Slider("Time (s)", _chain.delayTime, 0.01f, 1.5f);
            _chain.delayFeedback = EditorGUILayout.Slider("Feedback", _chain.delayFeedback, 0f, 0.9f);
            _chain.delayMix = EditorGUILayout.Slider("Mix", _chain.delayMix, 0f, 1f);
            _chain.delayPingPong = EditorGUILayout.Toggle("Ping-Pong", _chain.delayPingPong);
            EditorGUI.indentLevel--;
        }

        void DrawReverbSection()
        {
            bool open = Section("Reverb", _showReverb, ref _chain.reverbEnabled);
            _showReverb = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.reverbDecay = EditorGUILayout.Slider("Decay (s)", _chain.reverbDecay, 0.1f, 8f);
            _chain.reverbDamping = EditorGUILayout.Slider("Damping", _chain.reverbDamping, 0f, 0.95f);
            _chain.reverbMix = EditorGUILayout.Slider("Mix", _chain.reverbMix, 0f, 1f);
            _chain.reverbDryLevel = EditorGUILayout.Slider("Dry", _chain.reverbDryLevel, 0f, 1f);
            EditorGUI.indentLevel--;
        }

        void DrawPitchSection()
        {
            bool open = Section("Pitch Shift", _showPitch, ref _chain.pitchEnabled);
            _showPitch = open;
            if (!open) return;

            EditorGUI.indentLevel++;
            _chain.pitchSemitones = EditorGUILayout.IntSlider("Semitones", _chain.pitchSemitones, -24, 24);
            EditorGUI.indentLevel--;
        }

        void DrawPresetBar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("2070", "Futuristic but believable: gritty body, lifted chrome band, tight servo transient, close metallic room."), EditorStyles.miniButtonLeft))
                ApplyPreset(0);
            if (GUILayout.Button("Gritty", EditorStyles.miniButtonMid)) ApplyPreset(1);
            if (GUILayout.Button("Dark", EditorStyles.miniButtonMid)) ApplyPreset(2);
            if (GUILayout.Button("Industrial", EditorStyles.miniButtonMid)) ApplyPreset(3);
            if (GUILayout.Button("Metallic", EditorStyles.miniButtonMid)) ApplyPreset(4);
            if (GUILayout.Button("Submerged", EditorStyles.miniButtonMid)) ApplyPreset(5);
            if (GUILayout.Button("Radio", EditorStyles.miniButtonRight)) ApplyPreset(6);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Two independent columns of collapsible effect sections, so controls keep a
        /// usable width instead of stretching across the whole panel.
        /// </summary>
        void DrawEffectGrid()
        {
            EditorGUILayout.BeginHorizontal();

            // ── Column A ──
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawEqSection();
            DrawDistortionSection();
            DrawBitcrushSection();
            DrawCompressionSection();
            EditorGUILayout.EndVertical();

            GUILayout.Space(kColumnGap);

            // ── Column B ──
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawChorusSection();
            DrawFlangerSection();
            DrawWahSection();
            DrawDelaySection();
            DrawReverbSection();
            DrawPitchSection();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // ── Embedded preview player ────────────────────────────
        void DrawPreviewBar()
        {
            int selCount = 0;
            for (int i = 0; i < _selected.Length; i++) if (_selected[i]) selCount++;

            if (!EditorAudioPreview.IsAvailable)
            {
                EditorGUILayout.HelpBox(
                    "Embedded player unavailable. " + EditorAudioPreview.UnavailableReason +
                    "\nUse the AudioClip Inspector speaker icon to audition clips.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("PLAYER", EditorStyles.boldLabel, GUILayout.Width(52));

            // Time readout sits beside the title so the transport row stays compact
            float length = _previewClip != null ? _previewClip.length : 0f;
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{_previewPosition:0.00} / {length:0.00}s",
                EditorStyles.miniLabel, GUILayout.Width(96));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = EditorAudioPreview.IsAvailable;

            if (IconButton(TransportIcon.First, "First clip")) PlayFromSelection(0);
            if (IconButton(TransportIcon.Previous, "Previous clip")) PlayFromSelection(_previewIndex - 1);

            if (IconButton(_previewPlaying ? TransportIcon.Pause : TransportIcon.Play,
                _previewPlaying ? "Pause" : "Play", primary: true))
            {
                if (_previewPlaying) PausePreview();
                else ResumeOrPlay();
            }

            if (IconButton(TransportIcon.Stop, "Stop")) StopPreview();
            if (IconButton(TransportIcon.Next, "Next clip")) PlayFromSelection(_previewIndex + 1);
            if (IconButton(TransportIcon.Loop, "Loop", active: _loopPreview))
            {
                _loopPreview = !_loopPreview;
                EditorAudioPreview.SetLoop(_loopPreview);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            DrawWaveform();

            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(_previewSourcePath) ? "No clip loaded" : Path.GetFileName(_previewSourcePath),
                EditorStyles.miniLabel);

            bool wasOriginal = _showOriginal;
            _showOriginal = EditorGUILayout.ToggleLeft(
                _showOriginal
                    ? "Compare original - now hearing the RAW clip (click for processed)"
                    : "Compare original - now hearing the EFFECT CHAIN (click for raw)",
                _showOriginal);

            // A compare control should instantly A/B the clip, not silently change
            // what the next press of Play would do.
            if (_showOriginal != wasOriginal && _previewIndex >= 0)
                PreviewAt(_previewIndex);

            _autoAdvance = EditorGUILayout.ToggleLeft("Auto-advance to next selected", _autoAdvance);

            if (!string.IsNullOrEmpty(_previewError))
                EditorGUILayout.HelpBox(_previewError, MessageType.Error);
            else if (selCount == 0 && _previewClip == null)
                EditorGUILayout.LabelField("Tick clips on the left, then press Play.", EditorStyles.miniLabel);
        }

        /// <summary>Square icon button drawn with a vector transport glyph.</summary>
        bool IconButton(TransportIcon icon, string tooltip, bool primary = false, bool active = false, float size = 0f)
        {
            if (size <= 0f) size = primary ? kIconButton + 6f : kIconButton;
            Rect r = GUILayoutUtility.GetRect(size, size,
                GUILayout.Width(size), GUILayout.Height(size));

            bool hover = r.Contains(Event.current.mousePosition);

            Color bg = primary
                ? (hover ? kAccent : kAccentDim)
                : (active ? kAccentDim : (hover ? new Color(1f, 1f, 1f, 0.16f) : new Color(1f, 1f, 1f, 0.07f)));
            EditorGUI.DrawRect(r, bg);

            Color glyph = GUI.enabled
                ? (primary || active ? Color.white : new Color(0.88f, 0.85f, 0.95f, 1f))
                : new Color(1f, 1f, 1f, 0.3f);

            EditorTransportIcons.Draw(icon, r, glyph, icon == TransportIcon.Loop ? active : true);

            if (Event.current.type == EventType.Repaint)
            {
                string tip = string.IsNullOrEmpty(tooltip) ? EditorTransportIcons.Tooltip(icon) : tooltip;
                if (!string.IsNullOrEmpty(tip)) GUI.tooltip = tip;
            }

            if (GUI.enabled && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                GUI.changed = true;
                return true;
            }

            return false;
        }

        /// <summary>Resume from the paused position if there is one, otherwise start over.</summary>
        void ResumeOrPlay()
        {
            // Resume in place only when the clip we hold is the one the transport is
            // positioned in. After a mode switch or a chain change the held clip is a
            // different object, so resuming at the old offset would land past its end
            // and produce silence.
            if (_previewClip != null &&
                _previewIndex >= 0 &&
                _previewPosition > 0f &&
                _previewPosition < _previewClip.length - 0.01f)
            {
                int sample = Mathf.Clamp(
                    Mathf.RoundToInt(_previewPosition * _previewClip.frequency),
                    0, Mathf.Max(0, _previewClip.samples - 1));

                _previewPlaying = EditorAudioPreview.Play(_previewClip, sample, _loopPreview);
                if (_previewPlaying) return;
            }

            // Start the clip the user is actually pointing at, not merely the first
            // selected one.
            PlayFromSelection(_previewIndex >= 0 ? _previewIndex : 0);
        }

        void PausePreview()
        {
            EditorAudioPreview.Stop();
            _previewPlaying = false;
            Repaint();
        }

        /// <summary>
        /// Writes the clip to a temp asset in the project and selects it, so the clip can
        /// be auditioned with Unity's own Inspector transport. This is the guaranteed
        /// path if the editor's native preview graph refuses an in-memory clip.
        /// </summary>
        void WritePreviewAsset(AudioClip clip)
        {
            if (clip == null) return;

            const string folder = "Assets/Chopper/TempPreview";
            const string assetPath = folder + "/CyberpunkPreview.wav";

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Chopper", "TempPreview");

            try
            {
                string abs = Path.Combine(Application.dataPath, "Chopper", "TempPreview", "CyberpunkPreview.wav");
                Directory.CreateDirectory(Path.GetDirectoryName(abs));
                File.WriteAllBytes(abs, ClipToWav(clip));

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                var written = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                if (written != null)
                {
                    Selection.activeObject = written;
                    EditorGUIUtility.PingObject(written);

                    // Audition the imported asset, not the in-memory clip: imported
                    // assets are the only case confirmed to reach the audio device.
                    if (EditorAudioPreview.IsAvailable)
                    {
                        EditorAudioPreview.SetLoop(_loopPreview);
                        _previewPlaying = EditorAudioPreview.Play(written, 0, _loopPreview);

                        // Point the transport at what is actually playing so the
                        // playhead, scrubbing and length readout stay in sync. Free the
                        // in-memory clip we were holding first, then treat the imported
                        // asset as not-owned (project assets must never be destroyed).
                        if (_previewPlaying)
                        {
                            if (_previewOwnedClip && _previewClip != null && _previewClip != written)
                                DestroyImmediate(_previewClip);

                            _previewClip = written;
                            _previewOwnedClip = false;
                            _previewPosition = 0f;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _previewError = "Could not write preview clip: " + e.Message;
            }
        }

        /// <summary>Waveform of the processed clip with a clickable scrubbing playhead.</summary>
        void DrawWaveform()
        {
            // Taller view with a dedicated label gutter so amplitude and time can be
            // read at a glance instead of guessed from a bare 56px strip.
            const float gutter = 30f;   // left gutter for dB labels
            const float rulerH = 14f;   // bottom strip for time ticks
            const float topPad = 4f;

            float height = Mathf.Clamp(position.height * 0.20f, kWaveformMinHeight, kWaveformMaxHeight);

            Rect outer = EditorGUILayout.GetControlRect(false, height + rulerH);
            Rect area = new Rect(outer.x + gutter, outer.y + topPad, outer.width - gutter, height - topPad * 2f);
            Rect ruler = new Rect(area.x, area.yMax, area.width, rulerH);

            EditorGUI.DrawRect(new Rect(outer.x, outer.y, outer.width, height), new Color(0.10f, 0.09f, 0.14f, 1f));
            EditorGUI.DrawRect(ruler, new Color(0.13f, 0.12f, 0.18f, 1f));

            // ── Horizontal amplitude grid + gutter labels ──
            float mid = area.y + area.height * 0.5f;
            for (int g = 0; g <= 4; g++)
            {
                float frac = g / 4f;                    // 0 = bottom, 1 = top
                float gy = Mathf.Round(area.y + area.height * frac);
                float lineAlpha = g == 2 ? 0.22f : 0.07f;
                EditorGUI.DrawRect(new Rect(area.x, gy, area.width, 1f), new Color(1f, 1f, 1f, lineAlpha));

                float dbAtRow = (frac - 0.5f) * 2f * 24f; // −24 dB .. +24 dB across the window
                string txt = Mathf.Abs(dbAtRow) < 0.5f ? "0" : dbAtRow.ToString("+0;-0") + " dB";
                var lbl = new Rect(outer.x, gy - 7f, gutter - 4f, 14f);
                EditorGUI.LabelField(lbl, txt, EditorStyles.miniLabel);
            }

            if (_previewClip == null)
            {
                EditorGUI.LabelField(area, "  (no clip)", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (_waveform == null || _waveform.Length == 0)
            {
                EditorGUI.LabelField(area, "  (waveform unavailable)", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                int n = _waveform.Length;
                int pxW = Mathf.Max(1, Mathf.CeilToInt(area.width));
                float playedFrac = _previewPosition / Mathf.Max(_previewClip.length, 0.0001f);
                float halfH = area.height * 0.5f;

                Color waveCol = new Color(0.66f, 0.62f, 0.86f, 0.95f);
                Color peakCol = new Color(0.86f, 0.83f, 1f, 1f);

                for (int x = 0; x < pxW; x++)
                {
                    int a = Mathf.Clamp((int)((float)x / pxW * n), 0, n - 1);
                    int b = Mathf.Clamp((int)((float)(x + 1) / pxW * n), 0, n - 1);

                    float min = _waveform[a], max = _waveform[a];
                    for (int i = a; i <= b; i++)
                    {
                        if (_waveform[i] < min) min = _waveform[i];
                        if (_waveform[i] > max) max = _waveform[i];
                    }

                    float yTop = Mathf.Round(mid - max * halfH);
                    float yBottom = Mathf.Round(mid - min * halfH);
                    float h = Mathf.Max(1f, yBottom - yTop);

                    bool played = playedFrac > 0f && (float)x / pxW <= playedFrac;
                    EditorGUI.DrawRect(new Rect(area.x + x, yTop, 1f, h), played ? kAccent : waveCol);

                    // Bright cap on the peak so transients are easier to spot
                    if (h > 3f)
                        EditorGUI.DrawRect(new Rect(area.x + x, yTop, 1f, 1f), played ? Color.white : peakCol);
                }
            }

            // ── Time ruler ──
            float len = _previewClip.length;
            if (len > 0f)
            {
                int divisions = Mathf.Max(2, Mathf.RoundToInt(area.width / 70f));
                for (int d = 0; d <= divisions; d++)
                {
                    float t = (float)d / divisions;
                    float tx = Mathf.Round(area.x + area.width * t);
                    EditorGUI.DrawRect(new Rect(tx, area.yMax, 1f, 3f), new Color(1f, 1f, 1f, 0.25f));

                    string label = (t * len).ToString("0.00") + "s";
                    float w = 34f;
                    float lx = Mathf.Clamp(tx - w * 0.5f, outer.x + gutter, outer.xMax - w);
                    EditorGUI.LabelField(new Rect(lx, area.yMax + 2f, w, rulerH), label, EditorStyles.miniLabel);
                }
            }

            // ── Playhead ──
            if (len > 0f)
            {
                float t = Mathf.Clamp01(_previewPosition / len);
                float hx = Mathf.Round(area.x + area.width * t);
                EditorGUI.DrawRect(new Rect(hx - 1f, area.y, 2f, area.height), Color.white);
                EditorGUI.DrawRect(new Rect(hx - 3f, area.y, 6f, 3f), kAccent);
            }

            // Scrub (click or drag to seek). The whole outer region is hittable so the
            // taller panel is easy to grab without aiming at the thin wave body.
            Rect hit = new Rect(outer.x, outer.y, outer.width, height);
            if (len <= 0f) return;

            Event e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if (!hit.Contains(e.mousePosition)) return;

            float seekT = Mathf.Clamp01((e.mousePosition.x - area.x) / Mathf.Max(area.width, 1f));
            _previewPosition = seekT * len;

            // Restarting on every drag event would stutter the audio; only seek while
            // paused, and let a drag that started a playback re-launch once per press.
            if (_previewPlaying)
            {
                if (e.type == EventType.MouseDown)
                {
                    EditorAudioPreview.Stop();
                    EditorAudioPreview.Play(_previewClip, Mathf.RoundToInt(seekT * _previewClip.samples), _loopPreview);
                }
            }
            else
            {
                EditorAudioPreview.Seek(_previewClip, _previewPosition);
            }

            e.Use();
            Repaint();
        }

        static float[] BuildWaveform(float[] samples, int bucketCount)
        {
            if (samples == null || samples.Length == 0) return null;

            bucketCount = Mathf.Clamp(bucketCount, 32, 2048);
            float[] peaks = new float[bucketCount];
            float step = (float)samples.Length / bucketCount;

            for (int b = 0; b < bucketCount; b++)
            {
                int start = Mathf.FloorToInt(b * step);
                int end = Mathf.Min(samples.Length, Mathf.FloorToInt((b + 1) * step));
                float peak = 0f;
                for (int i = start; i < end; i++)
                {
                    float v = samples[i];
                    if (v < 0f) v = -v;
                    if (v > peak) peak = v;
                }
                peaks[b] = peak;
            }
            return peaks;
        }

        void PlayFromSelection(int startFrom)
        {
            if (_allFiles.Count == 0) return;

            int idx = -1;
            int n = _allFiles.Count;
            for (int step = 0; step < n; step++)
            {
                int c = ((startFrom + step) % n + n) % n;
                if (_selected[c] && IsVisible(c)) { idx = c; break; }
            }

            if (idx < 0)
            {
                // Nothing ticked: play whatever the window is currently pointing at, so
                // the transport always responds. Only complain when there is no
                // candidate at all.
                if (_previewIndex >= 0 && _previewIndex < n && IsVisible(_previewIndex))
                {
                    idx = _previewIndex;
                }
                else
                {
                    for (int step = 0; step < n; step++)
                    {
                        int c = ((startFrom + step) % n + n) % n;
                        if (IsVisible(c)) { idx = c; break; }
                    }
                }
            }

            if (idx < 0)
            {
                _previewError = "No clips to preview. Press Reload and check the source folder.";
                return;
            }

            _selected[idx] = true;   // keep the tick in sync with what is playing
            PreviewAt(idx);
        }

        void PreviewAt(int index)
        {
            _previewError = "";
            if (index < 0 || index >= _allFiles.Count)
            {
                _previewError = $"Cannot preview index {index} (clip count {_allFiles.Count}).";
                return;
            }

            string path = _allFiles[index];
            var source = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (source == null)
            {
                _previewError = "Cannot load clip: " + path;
                return;
            }

            AudioClip clip = source;
            if (!_showOriginal)
            {
                try
                {
                    clip = AudioEffectProcessor.ApplyEffects(source, _chain);

                    if (clip == null || clip.samples <= 0)
                    {
                        _previewError = "Effect chain produced an empty clip. Press Reset on the " +
                                        "chain, then Play again.";
                        return;
                    }
                }
                catch (Exception e)
                {
                    _previewError = "Effect chain failed: " + e.Message;
                    return;
                }
            }

            // Stop before swapping clips: the native preview keeps a reference to the
            // clip it was started with, so it must not be replaced underneath it.
            EditorAudioPreview.Stop();

            DisposeTempClip();

            _previewClip = clip;
            _previewOwnedClip = !_showOriginal;
            _previewIndex = index;
            _previewSourcePath = path;
            _previewPosition = 0f;

            // Waveform peaks (use left channel only to keep it cheap)
            var raw = new float[clip.samples * clip.channels];
            clip.GetData(raw, 0);
            if (clip.channels > 1)
            {
                var mono = new float[clip.samples];
                for (int i = 0; i < clip.samples; i++) mono[i] = raw[i * clip.channels];
                _waveform = BuildWaveform(mono, (int)Mathf.Max(128f, position.width * 0.5f));
            }
            else
            {
                _waveform = BuildWaveform(raw, (int)Mathf.Max(128f, position.width * 0.5f));
            }

            // Audition the processed clip through a temp asset.
            //
            // The editor's native preview graph reports the clip as playing for clips
            // created with AudioClip.Create, yet no audio reaches the device. Imported
            // assets do not have that problem, and this is the same mechanism the
            // Project window and AudioClip Inspector use, so it is the reliable path.
            if (!_showOriginal)
            {
                // Plays the imported asset; WritePreviewAsset starts the transport and
                // sets _previewPlaying, so the UI tracks position exactly like the
                // original-clip path.
                WritePreviewAsset(clip);
            }
            else if (EditorAudioPreview.IsAvailable)
            {
                EditorAudioPreview.SetLoop(_loopPreview);
                _previewPlaying = EditorAudioPreview.Play(clip, 0, _loopPreview);
            }
            else
            {
                WritePreviewAsset(clip);
            }

            if (!_previewPlaying)
                _previewError = "Preview did not start. Use the Inspector play button on the " +
                                "selected temp clip.";

            Repaint();
        }

        void StopPreview()
        {
            _previewPlaying = false;
            _previewPosition = 0f;
            EditorAudioPreview.Stop();
            Repaint();
        }

        // ── Export ─────────────────────────────────────────────
        void DrawExportBar()
        {
            int selectedCount = 0;
            for (int i = 0; i < _selected.Length; i++) if (_selected[i]) selectedCount++;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("EXPORT", EditorStyles.boldLabel, GUILayout.Width(70));

            GUI.enabled = !_isExporting && selectedCount > 0;
            if (GUILayout.Button($"Export selected ({selectedCount})", GUILayout.Width(180)))
                ExportPaths(CollectSelected());
            GUI.enabled = !_isExporting;
            if (GUILayout.Button("Export all", GUILayout.Width(110)))
                ExportPaths(new List<string>(_allFiles));
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_isExporting)
            {
                float t = _exportTotal > 0 ? (float)_exportProgress / _exportTotal : 0f;
                EditorGUILayout.LabelField($"Processing {_exportProgress}/{_exportTotal}");
                Rect r = EditorGUILayout.GetControlRect(false, 14f);
                EditorGUI.DrawRect(r, kRowOdd);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * t, r.height), kAccent);
            }

            if (!string.IsNullOrEmpty(_exportLog))
            {
                _exportLogScroll = EditorGUILayout.BeginScrollView(_exportLogScroll, GUILayout.Height(60));
                EditorGUILayout.LabelField(_exportLog, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        List<string> CollectSelected()
        {
            var list = new List<string>();
            for (int i = 0; i < _allFiles.Count; i++)
                if (_selected[i]) list.Add(_allFiles[i]);
            return list;
        }

        void ExportPaths(List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                _exportLog = "Nothing to export.";
                return;
            }

            string target = string.IsNullOrEmpty(_exportFolder) ? _sourceFolder : _exportFolder;
            string abs = ToAbsolute(target);
            Directory.CreateDirectory(abs);

            _exportTotal = files.Count;
            _exportProgress = 0;
            _exportLog = "";
            _isExporting = true;

            EditorCoroutine.Start(ExportRoutine(files, abs, target));
        }

        IEnumerator ExportRoutine(List<string> files, string absFolder, string assetFolder)
        {
            var processedPaths = new List<string>();

            for (int i = 0; i < files.Count; i++)
            {
                string src = files[i];
                string name = Path.GetFileNameWithoutExtension(src) + "_cyberpunk.wav";
                string dstAbs = Path.Combine(absFolder, name);

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(src);
                if (clip == null)
                {
                    _exportLog += "skip " + Path.GetFileName(src) + "\n";
                }
                else
                {
                    AudioClip outClip = null;
                    string failure = null;
                    try { outClip = AudioEffectProcessor.ApplyEffects(clip, _chain); }
                    catch (Exception e) { failure = e.Message; }

                    if (failure != null || outClip == null)
                    {
                        _exportLog += "fail " + Path.GetFileName(src) + ": " + failure + "\n";
                        _exportProgress++;
                        Repaint();
                        yield return null;
                        continue;
                    }

                    File.WriteAllBytes(dstAbs, ClipToWav(outClip));
                    DestroyImmediate(outClip);

                    processedPaths.Add("Assets" + dstAbs.Replace(Application.dataPath, "").Replace('\\', '/'));
                    _exportLog += "ok " + name + "\n";
                }

                _exportProgress++;
                Repaint();
                yield return null;
            }

            foreach (var p in processedPaths)
                AssetDatabase.ImportAsset(p);

            AssetDatabase.Refresh();
            _isExporting = false;
            _exportLog += $"\nDone: {processedPaths.Count}/{_exportTotal} written to {assetFolder}\n";
            Repaint();
        }

        // ── WAV encoder (16-bit PCM) ───────────────────────────
        static byte[] ClipToWav(AudioClip clip)
        {
            int channels = Mathf.Max(1, clip.channels);
            int rate = Mathf.Max(1, clip.frequency);
            int frames = clip.samples;

            float[] data = new float[frames * channels];
            clip.GetData(data, 0);

            const int bitDepth = 16;
            int dataLen = data.Length * bitDepth / 8;

            using var ms = new MemoryStream(44 + dataLen);
            using var bw = new BinaryWriter(ms);

            bw.Write((byte)'R'); bw.Write((byte)'I'); bw.Write((byte)'F'); bw.Write((byte)'F');
            bw.Write(36 + dataLen);
            bw.Write((byte)'W'); bw.Write((byte)'A'); bw.Write((byte)'V'); bw.Write((byte)'E');
            bw.Write((byte)'f'); bw.Write((byte)'m'); bw.Write((byte)'t'); bw.Write((byte)' ');
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)channels);
            bw.Write(rate);
            bw.Write(rate * channels * bitDepth / 8);
            bw.Write((short)(channels * bitDepth / 8));
            bw.Write((short)bitDepth);
            bw.Write((byte)'d'); bw.Write((byte)'a'); bw.Write((byte)'t'); bw.Write((byte)'a');
            bw.Write(dataLen);

            for (int i = 0; i < data.Length; i++)
            {
                float v = Mathf.Clamp(data[i], -1f, 1f);
                short s = (short)Mathf.RoundToInt(v * 32767f);
                bw.Write(s);
            }

            bw.Flush();
            return ms.ToArray();
        }

        // ── Chain persistence ──────────────────────────────────
        void SaveChain()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save effect chain", "CyberpunkChain", "asset", "Save chain");
            if (string.IsNullOrEmpty(path)) return;

            var existing = AssetDatabase.LoadAssetAtPath<AudioEffectChain>(path);
            if (existing != null) _chain = existing;
            else AssetDatabase.CreateAsset(_chain, path);

            EditorUtility.SetDirty(_chain);
            AssetDatabase.SaveAssets();
            Debug.Log("Chain saved: " + path);
        }

        void LoadChain()
        {
            string path = EditorUtility.OpenFilePanel("Load effect chain", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;

            var loaded = AssetDatabase.LoadAssetAtPath<AudioEffectChain>(ToRelative(path));
            if (loaded != null)
            {
                _chain = loaded;
                EditorUtility.SetDirty(_chain);
                Repaint();
            }
        }

        // ── Presets ────────────────────────────────────────────
        void ApplyPreset(int index)
        {
            _chain = CreateInstance<AudioEffectChain>();
            switch (index)
            {
                // ── 2070: futuristic, but still believable ─────────────────────
                // Intent: hardware that exists, pushed hard. The grit comes from
                // controlled saturation plus a modest distortion blend - not full
                // bitcrush. The "future" reads from a lifted 6 kHz mechanical band,
                // a very tight transient, a short ping-pong slap and a close damped
                // metal room. Nothing here dates it to a 1980s arcade.
                case 0:
                    // Body: keep weight, clear the mud, lift the chrome band.
                    // High shelf is deliberately strong: the damped reverb below eats
                    // top end, so the tilt has to be baked into the EQ.
                    _chain.eqLowEnabled = true;
                    _chain.eqLowFreq = 110f; _chain.eqLowGain = 2.5f;
                    _chain.eqMidEnabled = true;
                    _chain.eqMidFreq = 640f; _chain.eqMidGain = -4f; _chain.eqMidQ = 1.1f;
                    _chain.eqHighEnabled = true;
                    _chain.eqHighFreq = 5200f; _chain.eqHighGain = 9.5f;

                    // Grit: saturate the edges, keep the core dry. Both stages roll off
                    // the top end, so the mixes are kept low - the chrome comes from EQ.
                    _chain.distortionEnabled = true;
                    _chain.distortionDrive = 0.26f; _chain.distortionMix = 0.4f;
                    _chain.saturationEnabled = true;
                    _chain.saturationDrive = 2.4f; _chain.saturationMix = 0.55f;

                    // Quantisation only just audible in the tail, not on the attack
                    _chain.bitcrushEnabled = true;
                    _chain.bitcrushBits = 11; _chain.bitcrushSampleRate = 22050;

                    // Tight servo-like envelope: snap in, quick release, stays dry
                    _chain.compressionEnabled = true;
                    _chain.compressionThreshold = -22f; _chain.compressionRatio = 3.5f;
                    _chain.compressionAttack = 0.004f; _chain.compressionRelease = 0.07f;
                    _chain.compressionMix = 0.9f;

                    // Motion: subtle width and a faint metallic sweep, both under dry
                    _chain.chorusEnabled = true;
                    _chain.chorusRate = 0.7f; _chain.chorusDepth = 5f; _chain.chorusMix = 0.16f;
                    _chain.flangerEnabled = true;
                    _chain.flangerRate = 1.1f; _chain.flangerDepth = 1.2f;
                    _chain.flangerFeedback = 0.22f; _chain.flangerMix = 0.14f;

                    // No wah - it would date the sound to a 70s cop show
                    _chain.wahEnabled = false;

                    // Short slap-back for hard-surface proximity
                    _chain.delayEnabled = true;
                    _chain.delayTime = 0.062f; _chain.delayFeedback = 0.18f;
                    _chain.delayMix = 0.12f; _chain.delayPingPong = true;

                    // Close room that reads as hull plating, not a hall. Damping is
                    // moderate rather than heavy so the reflection keeps its edge.
                    _chain.reverbEnabled = true;
                    _chain.reverbDecay = 0.5f; _chain.reverbDamping = 0.45f;
                    _chain.reverbMix = 0.11f; _chain.reverbDryLevel = 1f;

                    // No pitch shift: one semitone would muddy, an octave would break it
                    _chain.pitchEnabled = false;
                    _chain.pitchSemitones = -1;
                    break;

                case 1: // Gritty cyberpunk
                    _chain.eqLowGain = 5f; _chain.eqMidGain = -3f; _chain.eqHighGain = 4f;
                    _chain.distortionEnabled = true; _chain.distortionDrive = 0.5f; _chain.distortionMix = 0.9f;
                    _chain.saturationEnabled = true; _chain.saturationDrive = 2f;
                    _chain.bitcrushEnabled = true; _chain.bitcrushBits = 9; _chain.bitcrushSampleRate = 16000;
                    _chain.compressionEnabled = true; _chain.compressionThreshold = -20f; _chain.compressionRatio = 4f;
                    _chain.delayEnabled = true; _chain.delayTime = 0.14f; _chain.delayMix = 0.22f;
                    _chain.reverbEnabled = true; _chain.reverbDecay = 1.6f; _chain.reverbMix = 0.25f;
                    _chain.pitchEnabled = false;
                    break;
                case 2: // Dark ambient
                    _chain.eqLowGain = 8f; _chain.eqHighGain = -8f;
                    _chain.distortionEnabled = false; _chain.bitcrushEnabled = false;
                    _chain.chorusEnabled = true; _chain.chorusRate = 0.4f; _chain.chorusDepth = 14f; _chain.chorusMix = 0.6f;
                    _chain.reverbEnabled = true; _chain.reverbDecay = 6f; _chain.reverbMix = 0.65f; _chain.reverbDamping = 0.6f;
                    _chain.delayEnabled = true; _chain.delayTime = 0.45f; _chain.delayFeedback = 0.45f; _chain.delayMix = 0.4f;
                    _chain.pitchEnabled = true; _chain.pitchSemitones = -5;
                    break;
                case 3: // Industrial
                    _chain.eqLowGain = 10f; _chain.eqMidGain = 6f; _chain.eqMidQ = 3f; _chain.eqHighGain = 7f;
                    _chain.distortionEnabled = true; _chain.distortionDrive = 0.85f;
                    _chain.saturationEnabled = true; _chain.saturationDrive = 5f;
                    _chain.bitcrushEnabled = true; _chain.bitcrushBits = 7; _chain.bitcrushSampleRate = 8000;
                    _chain.compressionEnabled = true; _chain.compressionThreshold = -14f; _chain.compressionRatio = 8f;
                    _chain.flangerEnabled = true; _chain.flangerRate = 4f; _chain.flangerDepth = 4f; _chain.flangerMix = 0.6f;
                    _chain.wahEnabled = true; _chain.wahRate = 3f; _chain.wahDepth = 1200f; _chain.wahMix = 0.5f;
                    break;
                case 4: // Metallic
                    _chain.eqLowGain = 4f; _chain.eqMidFreq = 3000f; _chain.eqMidGain = 8f; _chain.eqHighGain = 6f;
                    _chain.distortionEnabled = true; _chain.distortionDrive = 0.35f; _chain.distortionMix = 0.8f;
                    _chain.flangerEnabled = true; _chain.flangerRate = 2.5f; _chain.flangerDepth = 3f;
                    _chain.flangerFeedback = 0.55f; _chain.flangerMix = 0.65f;
                    _chain.delayEnabled = true; _chain.delayTime = 0.19f; _chain.delayMix = 0.35f;
                    _chain.reverbEnabled = true; _chain.reverbDecay = 2.2f; _chain.reverbMix = 0.3f;
                    break;
                case 5: // Submerged
                    _chain.eqLowGain = 10f; _chain.eqHighFreq = 1500f; _chain.eqHighGain = -12f;
                    _chain.distortionEnabled = false; _chain.bitcrushEnabled = false;
                    _chain.chorusEnabled = true; _chain.chorusRate = 0.6f; _chain.chorusDepth = 12f; _chain.chorusMix = 0.55f;
                    _chain.reverbEnabled = true; _chain.reverbDecay = 5f; _chain.reverbMix = 0.6f; _chain.reverbDamping = 0.75f;
                    _chain.pitchEnabled = true; _chain.pitchSemitones = -7;
                    break;
                case 6: // Radio
                    _chain.eqLowFreq = 800f; _chain.eqLowGain = -8f;
                    _chain.eqMidFreq = 2500f; _chain.eqMidGain = 5f;
                    _chain.eqHighFreq = 4000f; _chain.eqHighGain = -10f;
                    _chain.distortionEnabled = true; _chain.distortionDrive = 0.2f; _chain.distortionMix = 0.5f;
                    _chain.bitcrushEnabled = true; _chain.bitcrushBits = 12; _chain.bitcrushSampleRate = 16000;
                    _chain.compressionEnabled = true; _chain.compressionThreshold = -12f; _chain.compressionRatio = 6f;
                    _chain.reverbEnabled = true; _chain.reverbDecay = 0.6f; _chain.reverbMix = 0.15f;
                    break;
            }
            Repaint();
        }
    }

    // ── Edit-mode-safe coroutine runner ───────────────────────
    static class EditorCoroutine
    {
        static GameObject _host;

        static void EnsureHost()
        {
            if (_host != null) return;
            _host = new GameObject("__EditorCoroutineHost__");
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<CoroutineRunner>();
        }

        public static void Start(IEnumerator routine)
        {
            EnsureHost();
            _host.GetComponent<CoroutineRunner>().Run(routine);
        }

        class CoroutineRunner : MonoBehaviour
        {
            public void Run(IEnumerator routine) => StartCoroutine(RunRoutine(routine));

            IEnumerator RunRoutine(IEnumerator routine)
            {
                while (routine.MoveNext()) yield return routine.Current;
            }
        }
    }
}
