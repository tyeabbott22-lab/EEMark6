namespace ExtraterrestrialExhaust.Combat
{
    public interface IDamageable
    {
        bool IsAlive { get; }
        bool TryTakeDamage(DamageInfo damage);
    }
}
