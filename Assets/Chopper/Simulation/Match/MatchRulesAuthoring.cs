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
        [SerializeField, Tooltip("Seconds.")] private float postGameDuration = 20f;
        [SerializeField, Tooltip("Seconds.")] private float killCreditSeconds = 5f;

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
                    PostGameDuration = authoring.postGameDuration,
                    KillCreditSeconds = authoring.killCreditSeconds,
                });
            }
        }
    }
}
