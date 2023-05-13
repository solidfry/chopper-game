using UnityEngine;

namespace Abilities
{
    public interface IAbility
    {
        public void OnStart(Transform transform);
        public void OnUpdate();
        public void OnFixedUpdate();
        public void DoAbility();
    }
}