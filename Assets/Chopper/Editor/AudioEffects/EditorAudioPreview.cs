using System;
using System.Reflection;
using UnityEngine;

namespace Chopper.Editor.AudioEffects
{
    /// <summary>
    /// Thin wrapper over Unity's internal <c>UnityEditor.AudioUtil</c> preview API.
    /// This is the same native audio path the AudioClip Inspector speaker icon uses,
    /// so it plays while the editor is in edit mode - no play mode required.
    ///
    /// Unity has renamed these methods across versions, so every call is resolved
    /// by reflection and degrades gracefully (returns false) if the version differs.
    /// </summary>
    public static class EditorAudioPreview
    {
        static readonly Type s_AudioUtil = Type.GetType("UnityEditor.AudioUtil, UnityEditor");

        static readonly MethodInfo s_Play = Find("PlayPreviewClip", 3);
        static readonly MethodInfo s_Stop = Find("StopAllPreviewClips", 0);
        static readonly MethodInfo s_IsPlaying = Find("IsPreviewClipPlaying", 0);
        static readonly MethodInfo s_GetPosition = Find("GetPreviewClipPosition", 0);
        static readonly MethodInfo s_SetPosition = Find("SetPreviewClipSamplePosition", 2);
        static readonly MethodInfo s_Loop = Find("LoopPreviewClip", 1);

        static MethodInfo Find(string name, int argCount)
        {
            if (s_AudioUtil == null) return null;

            foreach (var m in s_AudioUtil.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                if (m.GetParameters().Length != argCount) continue;
                return m;
            }
            return null;
        }

        /// <summary>True when this Unity version exposes a usable preview API.</summary>
        public static bool IsAvailable => s_AudioUtil != null && s_Play != null && s_Stop != null;

        /// <summary>Human-readable detail shown in the window when the API is missing.</summary>
        public static string UnavailableReason
        {
            get
            {
                if (s_AudioUtil == null) return "UnityEditor.AudioUtil was not found in this Unity version.";
                if (s_Play == null) return "AudioUtil.PlayPreviewClip was not found.";
                if (s_Stop == null) return "AudioUtil.StopAllPreviewClips was not found.";
                return string.Empty;
            }
        }

        public static bool Play(AudioClip clip, int startSample = 0, bool loop = false)
        {
            if (!IsAvailable || clip == null) return false;

            try
            {
                s_Stop.Invoke(null, null);
                s_Play.Invoke(null, new object[] { clip, startSample, loop });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cyberpunk Audio] Preview play failed: " + e.Message);
                return false;
            }
        }

        public static void Stop()
        {
            if (s_Stop == null) return;
            try { s_Stop.Invoke(null, null); }
            catch (Exception e) { Debug.LogWarning("[Cyberpunk Audio] Preview stop failed: " + e.Message); }
        }

        public static bool IsPlaying()
        {
            if (s_IsPlaying == null) return false;
            try { return (bool)s_IsPlaying.Invoke(null, null); }
            catch { return false; }
        }

        /// <summary>Playback position in seconds.</summary>
        public static float GetPosition()
        {
            if (s_GetPosition == null) return 0f;
            try { return (float)s_GetPosition.Invoke(null, null); }
            catch { return 0f; }
        }

        public static void Seek(AudioClip clip, float seconds)
        {
            if (s_SetPosition == null || clip == null) return;
            try
            {
                int sample = Mathf.Clamp(
                    Mathf.RoundToInt(seconds * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
                s_SetPosition.Invoke(null, new object[] { clip, sample });
            }
            catch { /* seek is best-effort */ }
        }

        public static void SetLoop(bool loop)
        {
            if (s_Loop == null) return;
            try { s_Loop.Invoke(null, new object[] { loop }); }
            catch { /* optional feature */ }
        }
    }
}
