using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Capture the Nuke pick-up zone (owner, 20 Nov: "an area indicator for pick up ... each player should see
    /// their own pick up zone size"): a looping shell round the nuke while nobody holds it, its radius the local chopper's
    /// own hook range (<see cref="NukeHookConfig.Range"/>: 12 m, the Heavy Winch 18 m), the same 3D distance the server
    /// tests, so the ground cuts it to a dome. Dimmer while the local chopper is inside it (a bright shell seen from inside
    /// fills the view; the HUD line reads HOLD POSITION TO HOOK). A light beam on the nuke
    /// shows where it is from far away. Both are pooled prefabs from <see cref="CombatEffectsSettings"/> (picked in the
    /// effects bench, 24 Nov: Sci-Fi Arsenal Hex Shield and Loot Rays).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeZoneSystem : PresentationSystemBase
    {
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");

        /// <summary>One free nuke's visuals, and the zone's renderers with their own colours (for the in-range boost).</summary>
        private sealed class Visual
        {
            public GameObject Zone;
            public GameObject Beacon;
            public Renderer[] Renderers;
            public Color[] Colours;
            public bool Boosted;
            public Renderer[] BeaconRenderers;
            public bool BeaconHidden;
        }

        private readonly Dictionary<Entity, Visual> _visuals = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private readonly List<ParticleSystem> _systems = new();
        private PresentationContext _context;
        private MaterialPropertyBlock _block;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var effects = context.Effects;
            var settings = effects.Settings;
            _seen.Clear();
            if (settings != null && context.Match.Mode == MatchMode.CaptureTheNuke && (settings.NukeZone != null || settings.NukeBeacon != null))
            {
                var range = NukeHookConfig.Base.Range;
                var self = float3.zero;
                var hasSelf = false;
                byte selfTeam = TeamRules.None;
                foreach (var (pose, chopper) in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<GhostOwnerIsLocal, NukeCarry>().WithEntityAccess())
                {
                    if (SystemAPI.HasComponent<NukeHookConfig>(chopper)) range = SystemAPI.GetComponent<NukeHookConfig>(chopper).Range;
                    self = pose.ValueRO.Position;
                    if (SystemAPI.HasComponent<Team>(chopper)) selfTeam = SystemAPI.GetComponent<Team>(chopper).Value;
                    hasSelf = true;
                }

                foreach (var (nuke, pose, entity) in SystemAPI.Query<RefRO<NukeState>, RefRO<LocalToWorld>>().WithEntityAccess())
                {
                    // Only where the local team may hook it (pad, drop, the enemy's dock, a planted nuke to disarm).
                    if (!NukeRules.CanHook(nuke.ValueRO, selfTeam)) continue;
                    _seen.Add(entity);
                    Vector3 centre = pose.ValueRO.Position;
                    if (!_visuals.TryGetValue(entity, out var visual))
                    {
                        visual = Spawn(effects, settings, centre);
                        _visuals[entity] = visual;
                    }
                    if (visual.Zone != null)
                    {
                        visual.Zone.transform.position = centre;
                        visual.Zone.transform.localScale = Vector3.one * (range / settings.NukeZoneRadius);
                    }
                    var inside = hasSelf && math.distance(self, pose.ValueRO.Position) <= range;
                    if (visual.Zone != null) Boost(visual, inside ? settings.NukeZoneInRangeScale : 1f);
                    if (visual.Beacon != null)
                    {
                        visual.Beacon.transform.position = centre;
                        // The beam is for finding the nuke from far away; up close its core is a glare in the middle of the view.
                        HideBeacon(visual, inside);
                    }
                }
            }

            // Hooked, armed, gone, or no longer Capture the Nuke: give the visuals back.
            _gone.Clear();
            foreach (var entity in _visuals.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                Release(effects, _visuals[entity]);
                _visuals.Remove(entity);
            }
        }

        private Visual Spawn(CombatEffects effects, CombatEffectsSettings settings, Vector3 centre)
        {
            var visual = new Visual
            {
                Zone = Pooled(effects, settings.NukeZone, centre, Vector3.one),
                Beacon = Pooled(effects, settings.NukeBeacon, centre, settings.NukeBeaconScale),
            };
            if (visual.Beacon != null) visual.BeaconRenderers = visual.Beacon.GetComponentsInChildren<Renderer>(true);
            if (visual.Zone != null)
            {
                visual.Renderers = visual.Zone.GetComponentsInChildren<ParticleSystemRenderer>(true);
                visual.Colours = new Color[visual.Renderers.Length];
                for (var i = 0; i < visual.Renderers.Length; i++)
                {
                    var material = visual.Renderers[i].sharedMaterial;
                    visual.Colours[i] = material != null && material.HasProperty(BaseColourId) ? material.GetColor(BaseColourId) : Color.white;
                }
            }
            return visual;
        }

        /// <summary>
        /// A pooled instance in the prefab's own rotation (the pack's shells and beams are built lying down, turned upright
        /// by the root), its particles following the transform's scale (the pack's systems use Local scaling).
        /// </summary>
        private GameObject Pooled(CombatEffects effects, GameObject prefab, Vector3 position, Vector3 scale)
        {
            if (prefab == null) return null;
            var instance = effects.SpawnPooled(prefab, position, prefab.transform.rotation, muteAudio: true);
            if (instance == null) return null;
            instance.transform.localScale = scale;
            instance.GetComponentsInChildren(true, _systems);
            foreach (var system in _systems)
            {
                var main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            _systems.Clear();
            return instance;
        }

        private void Boost(Visual visual, float boost)
        {
            var boosted = !Mathf.Approximately(boost, 1f);
            if (visual.Renderers == null || boosted == visual.Boosted) return;
            visual.Boosted = boosted;
            _block ??= new MaterialPropertyBlock();
            for (var i = 0; i < visual.Renderers.Length; i++)
            {
                var renderer = visual.Renderers[i];
                if (renderer == null) continue;
                if (!boosted)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }
                _block.Clear();
                var colour = visual.Colours[i];
                _block.SetColor(BaseColourId, new Color(colour.r * boost, colour.g * boost, colour.b * boost, colour.a));
                renderer.SetPropertyBlock(_block);
            }
        }

        private static void HideBeacon(Visual visual, bool hidden)
        {
            if (visual.BeaconRenderers == null || hidden == visual.BeaconHidden) return;
            visual.BeaconHidden = hidden;
            foreach (var renderer in visual.BeaconRenderers)
                if (renderer != null) renderer.enabled = !hidden;
        }

        private void Release(CombatEffects effects, Visual visual)
        {
            HideBeacon(visual, false);
            if (visual.Zone != null)
            {
                Boost(visual, 1f);
                visual.Zone.transform.localScale = Vector3.one;
                effects.Release(visual.Zone);
            }
            if (visual.Beacon != null)
            {
                visual.Beacon.transform.localScale = Vector3.one;
                effects.Release(visual.Beacon);
            }
        }

        /// <summary>New context (scene change): old visuals went with the old pool.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _visuals.Clear();
            _context = context;
        }
    }
}
