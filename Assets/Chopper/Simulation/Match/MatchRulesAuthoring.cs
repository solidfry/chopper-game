using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Match
{
    /// <summary>Optional: put one in the gameplay subscene to change the Free For All rules.</summary>
    [DisallowMultipleComponent]
    public sealed class MatchRulesAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds.")] private float timeLimit = 600f;
        [SerializeField] private int scoreLimit = 20;
        [SerializeField, Tooltip("Seconds.")] private float postGameDuration = 10f;
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
                    PostGameDuration = authoring.postGameDuration,
                    KillCreditSeconds = authoring.killCreditSeconds,
                });
            }
        }
    }
}
