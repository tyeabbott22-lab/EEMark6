namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// The authored EE5 realScene tuning that defines the playable vertical
    /// slice. Runtime systems may opt out for experiments, but the gold path
    /// has one named source of truth instead of scattered inspector literals.
    /// </summary>
    public static class Ee5SliceProfile
    {
        public const float PlayerMass = 8f;
        public const float PlayerGravityScale = 0.285f;
        public const float PlayerLinearDamping = 0.35f;
        public const float PlayerAngularDamping = 3.25f;

        public const float ThrustForce = 55f;
        public const float RotationTorque = 0.4f;
        public const float StabilizationSpeed = 720f;
        public const float FlightAngularDamping = 0.85f;
        public const float RotationBoostMultiplier = 0.225f;
        public const bool PlayerRemoveVelocityIntoColliders = true;

        public const float PlayerFireCooldown = 1f;
        public const float PlayerRecoilForce = 12f;
        public const float PlayerProjectileSpeed = 30f;

        // EE5 separates the readable wake-line envelope from the enemy's
        // actual six-unit activation trigger. The telegraph can enter the
        // frame early while the enemy remains dormant until WakeDistance.
        public const float EnemyWakeSignalDistanceMultiplier = 4f;
        public const float EnemyGunnerChaseSpeed = 2f;
        public const float EnemyMeleeChaseSpeed = 3f;
        public const float EnemyGunnerFireCooldown = 1f;
        public const float EnemyGunnerProjectileSpeed = 6f;
        public const float EnemyContactDamage = 1f;
        public const float EnemyContactCooldown = 0.75f;
        public const float EnemyContactKnockback = 8f;

        // Objective handoff values are copied from RealSceneEncounterTool and
        // kept here so the builder and runtime defaults cannot drift apart.
        public const float EnergyGateLiftDistance = 10f;
        public const float EnergyGateLiftSpeed = 7f;
        public const float EnergyKeyCarrierSpawnOffsetY = 1.35f;
        public const float EnergyKeyVisualScale = 1.65f;
        public const float EnergyKeyPlayerFollowSharpness = 12f;

        public const float FlightStopperCenterY = -2f;
        public const float FlightStopperWidth = 12f;
        public const float FlightStopperHeight = 2f;

        public const float CameraFollowSpeed = 12f;
        public const float CameraVelocityLead = 0.24f;
        public const float CameraMaxLeadDistance = 3.75f;
        public const float CameraFacingLead = 1.15f;
        public const float CameraLeadSmooth = 10f;
        public const float CameraCatchupDistance = 1.4f;
        public const float CameraCatchupBoost = 2.2f;
        public const float CameraHardCatchupDistance = 5f;
        public const float CameraCloseEnoughSnap = 0.04f;
        public const float CameraSpeedZoomStart = 6f;
        public const float CameraSpeedZoomFull = 18f;
        public const float CameraMaxZoomOut = 2.25f;
        public const float CameraZoomSmooth = 10f;
        public const float CameraFlipZoomOut = 1.4f;
        public const float CameraFlipZoomDuration = 0.45f;
        public const float CameraFarParallaxStrength = 0.06f;
        public const float CameraMidParallaxStrength = 0.14f;
        public const float CameraNearParallaxStrength = 0.24f;
        public const float KeyReleasePulseDuration = 0.28f;
        public const float KeyReleasePulseScale = 1.28f;
        public const string WallTag = "Wall";
    }
}
