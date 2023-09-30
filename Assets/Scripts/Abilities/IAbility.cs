using UnityEngine;

namespace Abilities
{
    public interface IAbility
    {
        public void OnStart(Rigidbody rigidbody = null);
        public void OnUpdate();
        public void OnFixedUpdate();
        public void DoAbility();
    }
}