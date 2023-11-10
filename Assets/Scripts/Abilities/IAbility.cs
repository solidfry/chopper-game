using UnityEngine;

namespace Abilities
{
    public interface IAbility
    {
        public void OnStart(Rigidbody rb);
        public void OnUpdate();
        public void OnFixedUpdate();
        public void DoAbility();
    }
}