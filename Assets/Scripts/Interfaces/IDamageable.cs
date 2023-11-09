namespace Interfaces
{
    /// <summary>
    ///  Interface for objects that can be attacked and take damage.
    /// </summary>
    public interface IDamageable
    {
        public void TakeDamage(int damageAmount, ulong damagerId);
        public void Die();
        public ulong LastPlayerToDamage { get; }
    }
}