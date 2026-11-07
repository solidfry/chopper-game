using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The starting pilot roster (plan section 5b) in <c>Data/Loadout/Pilots</c>, linked into the loadout catalog
    /// (step 1). Creates only missing pilots; an existing asset keeps its tuning. Abilities are text only until build
    /// step 4.
    /// </summary>
    internal static class PilotAssets
    {
        private const string Folder = LoadoutAssets.Folder + "/Pilots";

        private sealed class Spec
        {
            public ushort Id;
            public string CallSign, Role, Passive, Ability, AbilityText;
            public Color Accent;
            public PilotModifiers Modifiers;
        }

        private static IEnumerable<Spec> Roster()
        {
            yield return new Spec
            {
                Id = 1, CallSign = "APOTHECARY", Role = "FIELD MEDIC", Accent = new Color(0f, 1f, 0.58f),
                Passive = "Repairs 40% faster, and a hit locks the repair for half as long. The boost tank refills a little slower.",
                Ability = "NANITE CLOUD", AbilityText = "Heal 30% of the hull over 5 s. Hits do not stop it.",
                Modifiers = new PilotModifiers { RepairTimeMultiplier = 0.6f, RepairLockMultiplier = 0.5f, BoostRechargeMultiplier = 1.15f },
            };
            yield return new Spec
            {
                Id = 2, CallSign = "VIPER", Role = "POISON ROUNDS", Accent = new Color(0.55f, 1f, 0.2f),
                Passive = "Gun hits corrode: 30% of each hit again over 3 s. Corrosion also stops enemy repairs. Gun hits are a little weaker.",
                Ability = "VENOM BELT", AbilityText = "8 s of double corrosion.",
                Modifiers = new PilotModifiers { BulletDotShare = 0.3f, BulletDotSeconds = 3f, GunDamageMultiplier = 0.95f },
            };
            yield return new Spec
            {
                Id = 3, CallSign = "PYRE", Role = "INCENDIARY", Accent = new Color(1f, 0.45f, 0.1f),
                Passive = "Rocket and missile hits burn: 25% of each hit again over 4 s. Fewer rounds per pylon magazine.",
                Ability = "NAPALM SALVO", AbilityText = "The next salvo leaves fire on the ground for 6 s.",
                Modifiers = new PilotModifiers { ExplosiveDotShare = 0.25f, ExplosiveDotSeconds = 4f, MagazineMultiplier = 0.75f },
            };
            yield return new Spec
            {
                Id = 4, CallSign = "WRAITH", Role = "STEALTH", Accent = new Color(0.6f, 0.4f, 1f),
                Passive = "A cold engine: enemy seekers take 40% longer to lock you and prefer hotter targets. Less hull.",
                Ability = "GHOST", AbilityText = "4 s where seekers cannot lock you; missiles in flight lose you.",
                Modifiers = new PilotModifiers { EnemyLockTimeMultiplier = 1.4f, HeatMultiplier = 0.6f, HealthMultiplier = 0.9f },
            };
            yield return new Spec
            {
                Id = 5, CallSign = "BULWARK", Role = "TANK", Accent = new Color(0.25f, 0.55f, 1f),
                Passive = "Takes 15% less damage from everything. Turns a little slower.",
                Ability = "AEGIS", AbilityText = "A 4 s shield that takes the next 50 damage.",
                Modifiers = new PilotModifiers { DamageTakenMultiplier = 0.85f, TurnRateMultiplier = 0.9f },
            };
            yield return new Spec
            {
                Id = 6, CallSign = "HOTWIRE", Role = "ENGINES", Accent = new Color(1f, 0.26f, 0.54f),
                Passive = "The boost tank refills 35% faster. Crashes hurt more.",
                Ability = "OVERCLOCK", AbilityText = "4 s of endless boost.",
                Modifiers = new PilotModifiers { BoostRechargeMultiplier = 0.65f, CrashDamageMultiplier = 1.2f },
            };
            yield return new Spec
            {
                Id = 7, CallSign = "OVERWATCH", Role = "SENSORS", Accent = new Color(0f, 0.94f, 1f),
                Passive = "Seeker locks build 30% faster. Gun hits are weaker.",
                Ability = "PING", AbilityText = "Shows every enemy through walls for 5 s.",
                Modifiers = new PilotModifiers { LockTimeMultiplier = 0.7f, GunDamageMultiplier = 0.9f },
            };
            yield return new Spec
            {
                Id = 8, CallSign = "HEX", Role = "ELECTRONIC WAR", Accent = new Color(0.85f, 0.2f, 1f),
                Passive = "Every hit glitches the victim's HUD. Gun hits are a little weaker.",
                Ability = "BLACKOUT", AbilityText = "A 150 m pulse: enemy locks drop and cannot start for 3 s.",
                Modifiers = new PilotModifiers { JamOnHit = true, GunDamageMultiplier = 0.95f },
            };
            yield return new Spec
            {
                Id = 9, CallSign = "SCRAPPER", Role = "SALVAGE", Accent = new Color(1f, 0.87f, 0.42f),
                Passive = "A kill gives back 25% of the hull. Field repair is 20% slower.",
                Ability = "STRIP DOWN", AbilityText = "Drop flares and armour for a short burst of speed.",
                Modifiers = new PilotModifiers { KillHeal = 0.25f, RepairTimeMultiplier = 1.2f },
            };
            yield return new Spec
            {
                Id = 10, CallSign = "RAMJET", Role = "BRAWLER", Accent = new Color(1f, 0.26f, 0.27f),
                Passive = "Rams do double damage to the other chopper and half to you (crashes too). Climbs slower.",
                Ability = "BATTERING RAM", AbilityText = "A 2 s armoured dash; impacts do not hurt you.",
                Modifiers = new PilotModifiers { RamDamageMultiplier = 2f, CrashDamageMultiplier = 0.5f, LiftMultiplier = 0.92f },
            };
        }

        public static void Ensure(LoadoutCatalog catalog)
        {
            if (catalog == null) return;
            Directory.CreateDirectory(Folder);
            var pilots = catalog.Pilots.Where(p => p != null).ToList();
            var changed = false;
            foreach (var spec in Roster())
            {
                if (pilots.Any(p => p.Id == spec.Id)) continue;
                var path = $"{Folder}/Pilot_{spec.CallSign}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<PilotDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<PilotDefinition>();
                    asset.Initialise(spec.Id, spec.CallSign, spec.Role, spec.Passive, spec.Ability, spec.AbilityText, spec.Accent, spec.Modifiers);
                    AssetDatabase.CreateAsset(asset, path);
                }
                pilots.Add(asset);
                changed = true;
            }
            if (!changed) return;
            catalog.SetPilots(pilots.OrderBy(p => p.Id).ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
