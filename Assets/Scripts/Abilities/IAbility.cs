using UnityEngine;

namespace Abilities
{
    public interface IAbility
    {
        public void OnStart();
        public void OnUpdate();
        public void OnFixedUpdate();
        public void DoAbility();
    }
}