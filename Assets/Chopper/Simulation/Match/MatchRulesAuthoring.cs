using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Match
{
    /// <summary>Optional: put one in the gameplay subscene to change the match rules (both modes).</summary>
    [DisallowMultipleComponent]
    public sealed class MatchRulesAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds.")] private float timeLimit = 600f;
        [SerializeField, Tooltip("Free For All: one player's kills.")] private int scoreLimit = 20;
        [SerializeField, Tooltip("Team Deathmatch: one team's kills.")] private int teamScoreLimit = 40;
        [SerializeField, Tooltip("Team modes: number of teams (2 to 8).")] private int teamCount = 2;
        [SerializeField, Tooltip("Seconds.")] private float postGameDuration = 20f;
        [SerializeField, Tooltip("Seconds.")] private float killCreditSeconds = 5f;
        [SerializeField, Tooltip("Free For All respawn: base + per death, capped; wave 0 = no waves.")]
        private RespawnRule freeForAllRespawn = RespawnRule.FreeForAll;
        [SerializeField, Tooltip("Team modes respawn: base + per death, capped; wave 0 = no waves.")]
        private RespawnRule teamRespawn = RespawnRule.Team;

        private sealed class Baker : Baker<MatchRulesAuthoring>
        {
            public override void Bake(MatchRulesAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new MatchRules
                {
                    TimeLimit = authoring.timeLimit,
                    ScoreLimit = authoring.scoreLimit,
                    TeamScoreLimit = authoring.teamScoreLimit,
                    TeamCount = TeamRules.ClampTeamCount(authoring.teamCount),
                    PostGameDuration = authoring.postGameDuration,
                    KillCreditSeconds = authoring.killCreditSeconds,
                    FreeForAllRespawn = authoring.freeForAllRespawn,
                    TeamRespawn = authoring.teamRespawn,
                });
            }
        }
    }
}
