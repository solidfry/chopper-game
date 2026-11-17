using Unity.Mathematics;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Nose-gun round maths shared by the server (<see cref="HitscanFireSystem"/>, <see cref="BulletStepSystem"/>), the
    /// bots, the tracers, the gun lead reticle and tests. Straight flight, no gravity. Pure, Burst friendly.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>A gun with no round speed hits at once along one ray (hitscan).</summary>
        public static bool IsInstant(float bulletSpeed) => bulletSpeed <= 0f;

        /// <summary>Seconds a round flies before its range runs out. 0 for an instant gun.</summary>
        public static float FlightTime(float range, float bulletSpeed) => IsInstant(bulletSpeed) ? 0f : math.max(range, 0f) / bulletSpeed;

        /// <summary>A round keeps the chopper's velocity, plus the gun's speed along the nose.</summary>
        public static float3 LaunchVelocity(float3 shooterVelocity, float3 forward, float bulletSpeed) =>
            shooterVelocity + forward * bulletSpeed;

        /// <summary>
        /// One tick of a round: <paramref name="from"/> = where it was, <see cref="ActiveBullet.Position"/> = where it is now.
        /// The last step stops where the time (range) runs out. False: no time was left, so it did not move (remove it,
        /// no ray). Ray-cast the segment from <paramref name="from"/> to the new position.
        /// </summary>
        public static bool Step(ref ActiveBullet bullet, float deltaTime, out float3 from)
        {
            from = bullet.Position;
            var time = math.min(deltaTime, bullet.TimeLeft);
            if (time <= 0f)
            {
                bullet.TimeLeft = 0f;
                return false;
            }

            bullet.Position += bullet.Velocity * time;
            // Metres flown as the shooter sees them (bullet speed only, not the chopper's velocity it inherited), so
            // damage falloff and the log match the hangar numbers whatever the shooter's own speed.
            bullet.Travelled += ShooterFrameSpeed(bullet) * time;
            bullet.TimeLeft -= time;
            return true;
        }

        /// <summary>The round's range has run out: remove it.</summary>
        public static bool Expired(in ActiveBullet bullet) => bullet.TimeLeft <= 0f;

        /// <summary>Metres flown at a hit at <paramref name="hitPoint"/> on the step that ended at the round's position.</summary>
        public static float TravelledAt(in ActiveBullet bullet, float3 hitPoint)
        {
            var worldSpeed = math.length(bullet.Velocity);
            var back = worldSpeed > 1e-3f ? math.distance(hitPoint, bullet.Position) * ShooterFrameSpeed(bullet) / worldSpeed : 0f;
            return math.max(0f, bullet.Travelled - back);
        }

        /// <summary>The round's speed relative to the shooter (its muzzle speed); world speed when no gun speed is set.</summary>
        private static float ShooterFrameSpeed(in ActiveBullet bullet) =>
            bullet.Gun.BulletSpeed > 0f ? bullet.Gun.BulletSpeed : math.length(bullet.Velocity);

        /// <summary>
        /// Lead point: where to aim so a straight projectile at <paramref name="projectileSpeed"/> (on top of the shooter's
        /// velocity, as gun rounds and rockets fly) meets the target. Works relative to the shooter
        /// (<see cref="Guidance.InterceptTime"/>), so the point is target + relative velocity × time. False when it can
        /// never catch the target, or only after <paramref name="maxTime"/> (out of range), or the gun is instant.
        /// </summary>
        public static bool LeadPoint(float3 muzzle, float3 shooterVelocity, float3 targetPosition, float3 targetVelocity,
            float projectileSpeed, float maxTime, out float3 point, out float time)
        {
            point = targetPosition;
            time = 0f;
            if (IsInstant(projectileSpeed)) return false;
            var relativeVelocity = targetVelocity - shooterVelocity;
            if (!Guidance.InterceptTime(targetPosition - muzzle, relativeVelocity, projectileSpeed, out time) || time > maxTime)
                return false;
            point = targetPosition + relativeVelocity * time;
            return true;
        }

        /// <summary>
        /// Lead point for a projectile that falls at <paramref name="gravity"/> m/s² (rockets): the straight lead, raised by
        /// the drop over the flight (0.5 g t²). The intercept time is exact for the straight path; the raise changes the
        /// launch angle only a little, so the time is not solved again. Gun rounds use gravity 0.
        /// </summary>
        public static bool LeadPoint(float3 muzzle, float3 shooterVelocity, float3 targetPosition, float3 targetVelocity,
            float projectileSpeed, float gravity, float maxTime, out float3 point, out float time)
        {
            if (!LeadPoint(muzzle, shooterVelocity, targetPosition, targetVelocity, projectileSpeed, maxTime, out point, out time))
                return false;
            point.y += Drop(gravity, time);
            return true;
        }

        /// <summary>How far a projectile falls in <paramref name="time"/> seconds at <paramref name="gravity"/> m/s².</summary>
        public static float Drop(float gravity, float time) => gravity > 0f ? 0.5f * gravity * time * time : 0f;
    }
}
