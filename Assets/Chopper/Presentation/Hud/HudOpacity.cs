using System;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>The HUD parts the player can fade. Warnings, the repair line, banners and the scoreboard are not in a group: they always show in full.</summary>
    public enum HudGroup
    {
        Horizon = 0,
        FlightRing = 1,
        Readouts = 2,
        Weapons = 3,
        Targeting = 4,
        MatchInfo = 5,
    }

    /// <summary>
    /// Player option: how solid each <see cref="HudGroup"/> is, 0 (hidden) to 1 (solid, the default). Saved.
    /// One per <see cref="Bridge.PresentationContext"/>: the options menu writes it, the HUD view shows it.
    /// </summary>
    public sealed class HudOpacity
    {
        private const string KeyPrefix = "Chopper.Hud.Opacity.";
        private static readonly int GroupCount = Enum.GetValues(typeof(HudGroup)).Length;

        private readonly float[] _values = new float[GroupCount];

        public HudOpacity()
        {
            for (var i = 0; i < GroupCount; i++)
                _values[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(Key((HudGroup)i), 1f));
        }

        public event Action Changed;

        public float Get(HudGroup group) => _values[(int)group];

        public void Set(HudGroup group, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_values[(int)group], value)) return;
            _values[(int)group] = value;
            PlayerPrefs.SetFloat(Key(group), value);
            Changed?.Invoke();
        }

        /// <summary>The saved-value key of a group.</summary>
        public static string Key(HudGroup group) => KeyPrefix + group;
    }
}
