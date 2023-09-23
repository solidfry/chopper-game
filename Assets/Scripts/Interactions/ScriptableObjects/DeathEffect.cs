using Interfaces;
using UnityEngine;

namespace Interactions.ScriptableObjects
{
    [CreateAssetMenu(fileName = "DeathEffect", menuName = "DeathEffects", order = 0)]
    public class DeathEffect : ScriptableObject, IDeathEffect
    {
        [SerializeField] private IDeathEffect.DeathEffectType effectType;

        public void DoDeathEffect()
        {
            Debug.Log(effectType.ToString());
        }
    }
}