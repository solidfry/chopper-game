using System;
using System.Text;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Hangar
{
    /// <summary>
    /// The loadout and skin the player picked in the hangar, saved in PlayerPrefs (Cloud Save later, Phase 5). Text
    /// form: <c>chassis:module,module,...</c> (0 = empty slot). The server validates whatever arrives.
    /// </summary>
    public sealed class LoadoutChoiceStore
    {
        private const string LoadoutKey = "Chopper.Hangar.Loadout";
        private const string SkinKey = "Chopper.Hangar.Skin";

        public bool TryLoad(out LoadoutSelection selection, out ushort skinId)
        {
            skinId = (ushort)Mathf.Clamp(PlayerPrefs.GetInt(SkinKey, 0), 0, ushort.MaxValue);
            return TryDecode(PlayerPrefs.GetString(LoadoutKey, ""), out selection);
        }

        public void Save(in LoadoutSelection selection, ushort skinId)
        {
            PlayerPrefs.SetString(LoadoutKey, Encode(selection));
            PlayerPrefs.SetInt(SkinKey, skinId);
            PlayerPrefs.Save();
        }

        public static string Encode(in LoadoutSelection selection)
        {
            var text = new StringBuilder();
            text.Append(selection.ChassisId).Append(':');
            for (var i = 0; i < selection.Modules.Count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(selection.Modules[i]);
            }
            return text.ToString();
        }

        /// <summary>False for empty or broken text, chassis 0, or more slots than a <see cref="LoadoutCode"/> holds.</summary>
        public static bool TryDecode(string text, out LoadoutSelection selection)
        {
            selection = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(':');
            if (parts.Length != 2 || !ushort.TryParse(parts[0], out var chassisId) || chassisId == 0) return false;

            var pieces = parts[1].Length == 0 ? Array.Empty<string>() : parts[1].Split(',');
            if (pieces.Length > LoadoutCode.MaxSlots) return false;
            var modules = new ushort[pieces.Length];
            for (var i = 0; i < pieces.Length; i++)
                if (!ushort.TryParse(pieces[i], out modules[i])) return false;

            selection = new LoadoutSelection(chassisId, modules);
            return true;
        }
    }

    /// <summary>
    /// Client only (not thin clients: they pick their own). When the client world starts, puts the saved hangar choice
    /// in the <see cref="LoadoutChoice"/> singleton, which <see cref="GoInGameClientSystem"/> sends with the join request.
    /// No saved choice: no singleton, so the server gives the default chopper.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class LoadoutChoiceSystem : SystemBase
    {
        protected override void OnCreate()
        {
            if (!new LoadoutChoiceStore().TryLoad(out var selection, out var skinId)) return;
            EntityManager.CreateSingleton(new LoadoutChoice { Code = LoadoutCode.From(selection), SkinId = skinId }, "LoadoutChoice");
        }

        protected override void OnUpdate() => Enabled = false;
    }
}
