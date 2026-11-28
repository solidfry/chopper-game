using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// The match objective as the bots see it (Capture the Nuke). Filled by the server bot system; empty in other modes.
    /// </summary>
    public struct BotObjectiveInfo
    {
        public bool Active;
        public float3 NukePosition;
        public Match.NukePhase Phase;
        /// <summary>The chopper that holds the nuke (Entity.Null when none).</summary>
        public Entity Carrier;
        public byte CarrierTeam;
        /// <summary>The team that armed the nuke (0 = not armed).</summary>
        public byte ArmedBy;
        /// <summary>Team whose base dock holds the nuke (docked or planted), else 0.</summary>
        public byte DockTeam;
        /// <summary>Base docks (ground points).</summary>
        public float3 Base1;
        public float3 Base2;
    }

    /// <summary>A live gun turret, as the bots see it (a ground target).</summary>
    public struct BotTurretInfo
    {
        public Entity Entity;
        /// <summary>Aim point (the gun head).</summary>
        public float3 Position;
        public float Health01;
        /// <summary><see cref="Combat.TurretConfig.Team"/> (0 = shoots everyone).</summary>
        public byte Team;
        /// <summary>Its gun range, metres.</summary>
        public float Reach;
    }

    /// <summary>
    /// Pure objective choice for a bot (Capture the Nuke, plan section 7 step 5): where to fly when no fight needs it.
    /// Carrier: home base until armed, then the enemy base. Nuke free: hover over it to hook it. Team mate carries:
    /// escort. Enemy carries: chase the carrier.
    /// </summary>
    public static class BotObjective
    {
        /// <summary>Hover this high over a free nuke: inside the base hook range (24 m), clear of the ground.</summary>
        public const float HookHoverHeight = 8f;
        /// <summary>Arrive radius over the nuke: close enough to stay in hook range.</summary>
        public const float HookArriveRadius = 3f;
        /// <summary>Fly this high over a base zone centre.</summary>
        public const float BaseHeight = 25f;
        /// <summary>Carrier hover over a dock: the nuke hangs low enough to set down (NukeZones.DockHeight).</summary>
        public const float DockHoverHeight = Match.NukeZones.HangBelow + 4f;
        /// <summary>An enemy further than this does not pull a bot off the objective, metres.</summary>
        public const float FocusRange = 250f;
        /// <summary>This close to a free nuke, a bot keeps hooking even in a fight, metres.</summary>
        public const float HookCommitRange = 60f;
        /// <summary>
        /// With no turret covering the nuke, a bot this close leaves a fight to hook, metres. Played 26 Nov: with all four
        /// turrets down both teams dogfought 70-150 m from the nuke for minutes and no one hooked (60 m was too close).
        /// </summary>
        public const float OpenHookCommitRange = 150f;

        /// <summary>
        /// A dropped nuke goes back to the pad after 30 s, so bots this close leave a fight for it, metres. Played 28 Nov:
        /// after each carrier kill every bot kept fighting 160-560 m away and the drop always reset (3 hooks, no dock).
        /// </summary>
        public const float DroppedHookCommitRange = 400f;

        /// <summary>
        /// Over the nuke (this close to it, metres) a hooking bot stays to finish the hook in any fight, even a missile
        /// evade (it still fires flares): leaving resets the 10 s hook. Played 28 Nov: the hook flickered on and off every
        /// 1-2 s over a dropped nuke and never took.
        /// </summary>
        public const float HookHoldRange = 30f;

        /// <summary>Distance to the hook point inside which a bot keeps to the hook in a fight.</summary>
        public static float CommitRange(bool turretsCover, bool dropped = false) =>
            turretsCover ? HookCommitRange : dropped ? DroppedHookCommitRange : OpenHookCommitRange;

        /// <summary>
        /// Target score bonus, metres, for an enemy that carries the nuke or hovers to hook it. Played 28 Nov: the first
        /// docked nuke was 69 % armed when one enemy hooked it out of the dock in 10 s; the two defenders fought others.
        /// </summary>
        public const float ObjectiveTargetBonus = 300f;

        /// <summary>This close to a nuke nobody holds, an enemy counts as hooking it (the base hook reach, plus a margin).</summary>
        public const float HookThreatRange = Match.NukeHookConfig.BaseRange * 1.5f;

        /// <summary>
        /// Carrier run speed, times cruise (owner, 28 Nov: "the carrier should try to get back to the base as a priority
        /// with haste"). It also may boost and holds its course through missile evades (flares still fire).
        /// </summary>
        public const float CarrierSpeedFactor = 1.5f;

        /// <summary>A chaser in gun reach of the carrier's path this far out can stop it, metres.</summary>
        public const float InterceptReach = 200f;

        /// <summary>A chaser flies this much faster than the slowed carrier (the nuke cuts its top speed to 0.75).</summary>
        public const float InterceptSpeedRatio = 1.1f;

        /// <summary>Points checked along the carrier's way to its dock.</summary>
        private const int InterceptSamples = 6;

        /// <summary>Where the carrier is going: its home dock to arm, or the enemy dock once armed.</summary>
        public static float3 CarrierGoal(in BotObjectiveInfo info)
        {
            var armed = info.ArmedBy != 0 && info.ArmedBy == info.CarrierTeam;
            var home = info.CarrierTeam == 1 ? info.Base1 : info.Base2;
            var enemy = info.CarrierTeam == 1 ? info.Base2 : info.Base1;
            return armed ? enemy : home;
        }

        /// <summary>
        /// True when a chopper at <paramref name="from"/> can reach the carrier's way to its dock in time: some point on
        /// the way that it gets into gun reach of before the carrier passes (owner, 28 Nov: "attack the carrier if they
        /// are reasonably able to stop them getting back to the base, otherwise they prioritise the next best target").
        /// </summary>
        public static bool CanIntercept(in BotObjectiveInfo info, float3 from)
        {
            if (info.Carrier == Entity.Null) return false;
            var start = info.NukePosition;
            var goal = CarrierGoal(info);
            for (var i = 0; i <= InterceptSamples; i++)
            {
                var point = math.lerp(start, goal, i / (float)InterceptSamples);
                var carrierTime = math.distance(start, point);
                var chaserTime = math.max(0f, math.distance(from, point) - InterceptReach) / InterceptSpeedRatio;
                if (chaserTime <= carrierTime) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the chopper is the first target for a bot at <paramref name="observer"/>: it carries the nuke and the
        /// bot can stop it (<see cref="CanIntercept"/>), or it is close enough to a free or docked nuke to be hooking it.
        /// </summary>
        public static bool OnObjective(in BotObjectiveInfo info, Entity chopper, float3 position, float3 observer)
        {
            if (!info.Active || info.Phase == Match.NukePhase.Detonated) return false;
            if (info.Carrier != Entity.Null) return info.Carrier == chopper && CanIntercept(info, observer);
            return math.distance(position, info.NukePosition) < HookThreatRange;
        }

        /// <summary>True when a hooking bot is over the nuke and must stay to finish the hook, whatever else happens.</summary>
        public static bool HoldsHook(Kind objective, float distanceToNuke) => objective == Kind.Hook && distanceToNuke < HookHoldRange;
        /// <summary>
        /// Target score cost of a turret against a chopper, metres: an enemy chopper this much further away is still
        /// picked first (owner, 20 Nov: "prioritise the turrets or nearby enemies").
        /// </summary>
        public const float TurretScorePenalty = 150f;
        /// <summary>
        /// With turrets in view, an enemy chopper further than <see cref="FocusRange"/> scores this much lower, so turrets
        /// win over far enemies ("turrets or nearby enemies"). Played 25 Nov: both teams sat at their siege points and
        /// picked each other at 350 m (too far to reach from there) while the last turret stood.
        /// </summary>
        public const float FarChopperPenalty = 400f;
        /// <summary>
        /// Siege point distance from the nuke, metres: on the bot's home side, outside the pad turrets' reach (260 m gun
        /// + 44 m ring). While turrets cover the nuke, bots hold here and shoot them from stand-off range
        /// (<see cref="BotBrain.StrafeRange"/>); team mates meet here, so they go in together (owner, 20 Nov: group assault).
        /// </summary>
        public const float StagingDistance = 330f;
        /// <summary>Escorts fly this far above the carrier.</summary>
        public const float EscortHeight = 15f;

        public enum Kind : byte
        {
            None,
            Hook,
            ArmAtHome,
            Deliver,
            Escort,
            Chase,
            /// <summary>Our nuke arming in our dock, or our planted nuke counting down: stay near and keep enemies off it.</summary>
            Guard,
        }

        /// <summary>
        /// A guard further than this from the nuke goes back to it, even in a fight, metres; inside it, it fights (owner,
        /// 28 Nov: "bots should stay to guard"). Played 28 Nov: every docked nuke was hooked out 23-31 s in, before the
        /// 28 s arm ended, while the defenders fought elsewhere.
        /// </summary>
        public const float GuardRadius = 150f;

        /// <summary>
        /// A scout guards or escorts only while an enemy is this close to the nuke, metres; else it roams and hunts (owner,
        /// 28 Nov: "scouts types should roam more or defend depending on enemy proximity").
        /// </summary>
        public const float ScoutDefendRange = 450f;

        /// <summary>Distance from <paramref name="point"/> to the nearest live enemy of <paramref name="team"/> (MaxValue: none).</summary>
        public static float NearestEnemy(in NativeArray<BotChopperInfo> choppers, byte team, float3 point)
        {
            var nearest = float.MaxValue;
            for (var i = 0; i < choppers.Length; i++)
            {
                if (Match.TeamRules.AreAllies(team, choppers[i].Team)) continue;
                nearest = math.min(nearest, math.distance(choppers[i].Position, point));
            }
            return nearest;
        }

        /// <summary>True when a scout leaves a guard or escort duty to roam: no enemy near what it would defend.</summary>
        public static bool ScoutRoams(Loadout.ChassisClass chassisClass, Kind objective, float nearestEnemy) =>
            chassisClass == Loadout.ChassisClass.Scout && (objective == Kind.Guard || objective == Kind.Escort) &&
            nearestEnemy > ScoutDefendRange;

        /// <summary>True when a guard has strayed too far from the nuke and must go back, fight or not.</summary>
        public static bool GuardReturns(Kind objective, float distanceToNuke) => objective == Kind.Guard && distanceToNuke > GuardRadius;

        /// <summary>
        /// True when a live enemy turret reaches the nuke (its gun range from the nuke): hovering there to hook is death.
        /// Played 25 Nov: bots that went in with the turrets up died in 3-5 s, 75-150 m from them.
        /// </summary>
        public static bool TurretsCover(in BotObjectiveInfo info, in NativeArray<BotTurretInfo> turrets, byte team)
        {
            for (var i = 0; i < turrets.Length; i++)
            {
                var turret = turrets[i];
                if (!Combat.TurretRules.IsEnemy(turret.Team, team)) continue;
                if (math.distance(turret.Position.xz, info.NukePosition.xz) < turret.Reach) return true;
            }
            return false;
        }

        /// <summary>
        /// Siege: true (and the siege point) while turrets cover the nuke. The bot holds there, out of their reach, and
        /// shoots them; once none covers the nuke it goes in to hook.
        /// </summary>
        public static bool Stage(in BotObjectiveInfo info, byte team, bool turretsCover, out float3 staging)
        {
            var home = team == 1 ? info.Base1 : info.Base2;
            var toHome = math.normalizesafe(new float3(home.x - info.NukePosition.x, 0f, home.z - info.NukePosition.z));
            staging = info.NukePosition + toHome * StagingDistance + new float3(0f, BaseHeight, 0f);
            return turretsCover;
        }

        public static Kind Choose(in BotObjectiveInfo info, Entity self, byte team, out float3 goal, out float arriveRadius)
        {
            goal = default;
            arriveRadius = 0f;
            if (!info.Active || team == 0 || info.Phase == Match.NukePhase.Detonated) return Kind.None;

            var home = team == 1 ? info.Base1 : info.Base2;
            var enemy = team == 1 ? info.Base2 : info.Base1;
            if (info.Carrier == self && self != Entity.Null)
            {
                var armed = info.ArmedBy == team;
                goal = (armed ? enemy : home) + new float3(0f, DockHoverHeight, 0f);
                arriveRadius = 4f;
                return armed ? Kind.Deliver : Kind.ArmAtHome;
            }
            if (info.Carrier != Entity.Null)
            {
                var ally = info.CarrierTeam == team;
                goal = info.NukePosition + new float3(0f, ally ? EscortHeight + Match.NukeZones.HangBelow : Match.NukeZones.HangBelow, 0f);
                arriveRadius = ally ? 20f : 10f;
                return ally ? Kind.Escort : Kind.Chase;
            }
            var free = new Match.NukeState { Phase = info.Phase, ArmedBy = info.ArmedBy, DockTeam = info.DockTeam };
            if (!Match.NukeRules.CanHook(free, team))
            {
                // Our own nuke arming in our dock, or our planted nuke counting down: guard it.
                goal = info.NukePosition + new float3(0f, EscortHeight + Match.NukeZones.HangBelow, 0f);
                arriveRadius = 20f;
                return Kind.Guard;
            }
            goal = info.NukePosition + new float3(0f, HookHoverHeight, 0f);
            arriveRadius = HookArriveRadius;
            return Kind.Hook;
        }
    }
}
