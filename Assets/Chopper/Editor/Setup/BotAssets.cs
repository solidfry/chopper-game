using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Config;
using Chopper.Presentation.Hangar;
using Chopper.Simulation.Bots;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Bot assets: the default profile, and for server bots the settings (count, roster) with an easy and a hard
    /// profile. Creates what is missing; never overwrites tuned values. The server bot part was written without the
    /// editor (13 Nov 2026).
    /// </summary>
    internal static class BotAssets
    {
        public const string Folder = "Assets/Chopper/Data/Bots";
        public const string ProfilePath = Folder + "/BotProfile_Default.asset";
        public const string RookiePath = Folder + "/BotProfile_Rookie.asset";
        public const string AcePath = Folder + "/BotProfile_Ace.asset";
        public const string SettingsPath = Folder + "/BotSettings.asset";

        /// <summary>The first roster. Row 2, 4, 6, 8 get a heat seeker on the first pylon when the loadout stays valid.</summary>
        private static readonly string[] Names = { "RAZOR", "GLITCH", "NEON", "STATIC", "CHROME", "FUSE", "VECTOR", "ROACH" };

        public static BotProfile EnsureProfile() => EnsureProfile(ProfilePath, null);

        private static BotProfile EnsureProfile(string path, System.Action<BotSkillTuning> tune)
        {
            var profile = AssetDatabase.LoadAssetAtPath<BotProfile>(path);
            if (profile != null) return profile;

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Chopper/Data", "Bots");
            profile = ScriptableObject.CreateInstance<BotProfile>();
            tune?.Invoke(profile.Skill);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>Slow to react, poor aim, forgets gadgets.</summary>
        private static BotProfile EnsureRookie() => EnsureProfile(RookiePath, skill =>
        {
            skill.ReactionSeconds = 0.5f;
            skill.AimErrorDegrees = 6f;
            skill.AimSettleSeconds = 2.2f;
            skill.FlareTimingScale = 1.4f;
            skill.MistakeChance = 0.3f;
        });

        /// <summary>Fast, steady aim, few mistakes.</summary>
        private static BotProfile EnsureAce() => EnsureProfile(AcePath, skill =>
        {
            skill.ReactionSeconds = 0.2f;
            skill.AimErrorDegrees = 2f;
            skill.AimSettleSeconds = 1f;
            skill.FlareTimingScale = 0.9f;
            skill.MistakeChance = 0.05f;
        });

        /// <summary>
        /// The server bot settings. A new asset gets 8 rows: names, a mix of the default, rookie and ace profiles, the
        /// catalog's chassis and pilots in turn, and a heat seeker on every second bot.
        /// </summary>
        public static BotSettings EnsureSettings(LoadoutCatalog catalog)
        {
            var settings = AssetDatabase.LoadAssetAtPath<BotSettings>(SettingsPath);
            if (settings != null) return settings;

            EnsureProfile();
            var profiles = new[] { null, EnsureRookie(), EnsureAce(), null };
            var seeker = catalog != null ? catalog.Modules.FirstOrDefault(m => m != null && m.Id == GuidedWeaponAssets.HomingMissilesId) : null;
            var rows = new List<BotRosterRow>();
            for (var i = 0; i < Names.Length; i++)
            {
                var row = new BotRosterRow { Name = Names[i], Profile = profiles[i % profiles.Length] };
                if (catalog != null)
                {
                    if (catalog.Pilots.Count > 0) row.Pilot = catalog.Pilots[i % catalog.Pilots.Count];
                    if (i % 2 == 1) GiveSeeker(row, catalog, i, seeker);
                }
                rows.Add(row);
            }

            settings = ScriptableObject.CreateInstance<BotSettings>();
            settings.Initialise(rows);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>The row's chassis with its default modules, and the seeker on the first pylon (only when valid).</summary>
        private static void GiveSeeker(BotRosterRow row, LoadoutCatalog catalog, int slot, ModuleDefinition seeker)
        {
            var chassis = row.ChassisFor(catalog, slot);
            if (chassis == null || seeker == null) return;
            var pylon = -1;
            for (var i = 0; i < chassis.Slots.Count && pylon < 0; i++)
                if (chassis.Slots[i] == SlotType.Pylon) pylon = i;
            if (pylon < 0) return;

            var modules = new List<ModuleDefinition>();
            for (var i = 0; i < chassis.Slots.Count; i++)
                modules.Add(i == pylon ? seeker : i < chassis.DefaultModules.Count ? chassis.DefaultModules[i] : null);
            var trial = new BotRosterRow { Chassis = chassis, Modules = modules };
            if (!Valid(trial, catalog, slot, out _)) return;
            row.Chassis = chassis;
            row.Modules = modules;
        }

        private static bool Valid(BotRosterRow row, LoadoutCatalog catalog, int slot, out LoadoutResult result)
        {
            result = default;
            var chassis = row.ChassisFor(catalog, slot);
            if (chassis == null) return false;
            var code = row.ToCode(catalog, slot);
            result = LoadoutRules.Validate(code.ToSelection(chassis.Slots.Count), catalog, new AllUnlocked());
            return result.IsValid;
        }

        /// <summary>Validate: the settings exist, each row's profile and loadout are usable, and the cost note.</summary>
        public static IEnumerable<(string Message, Object Context)> Problems(GameConfig config)
        {
            var settings = AssetDatabase.LoadAssetAtPath<BotSettings>(SettingsPath);
            if (settings == null)
            {
                yield return ("No bot settings asset (server bots). Press '1. Create / repair config'.", null);
                yield break;
            }
            if (config != null && config.Bots != null && config.Bots != settings)
                yield return ($"The GameConfig uses bot settings '{config.Bots.name}', not {SettingsPath}: step 4 gives the spawners the second one.", config);

            var catalog = config != null ? config.LoadoutCatalog : null;
            if (catalog == null || settings.MaxBots == 0) yield break;
            if (AssetDatabase.LoadAssetAtPath<BotProfile>(ProfilePath) == null)
                yield return ("No default bot profile: rows without a profile cannot fly. Press '4. Set up gameplay subscene'.", settings);

            for (var i = 0; i < settings.Roster.Count; i++)
            {
                var row = settings.Roster[i];
                if (row == null) continue;
                if (row.ChassisFor(catalog, i) == null)
                {
                    yield return ($"{settings.name}: roster row {i + 1} ({row.Name}) has no chassis and the catalog has none.", settings);
                    continue;
                }
                if (!Valid(row, catalog, i, out var result))
                    yield return ($"{settings.name}: roster row {i + 1} ({row.Name}) has a loadout that is not valid ({result.Error}" +
                                  (result.Slot >= 0 ? $", slot {result.Slot + 1}" : "") + "). The server gives it the chassis default.", settings);
            }
        }
    }
}
