using System;
using UnityEngine;

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
        // Keep the raw realScene Rigidbody2D values documented for the eventual
        // prefab cleanup. The current playable pass uses the lighter overlay
        // immediately below: it was the last responsive EE6 baseline before the
        // raw drag was forced at runtime and the craft started feeling soupy.
        public const float PlayerLinearDamping = 0.35f;
        public const float PlayerAngularDamping = 3.25f;
        // Temporary presentable-flight overlay. Keep this named separately so
        // the later prefab-rip pass can switch back to the raw import in one
        // place after a side-by-side feel check.
        public const float PlayerFlightLinearDamping = 0f;
        // Keep linear motion frictionless, but give the craft enough rotational
        // damping to settle after a tap instead of carrying a lazy spin across
        // the whole room. This is the presentable bridge, not the raw prefab rip.
        public const float PlayerFlightAngularDamping = 1.1f;
        public const float PlayerMaxHealth = 5f;
        public const float PlayerInvulnerabilityDuration = 0.5f;
        // Copied from EE5's sniper prefab. Keep the physics hurtbox authored
        // on the root body; the enlarged child presentation must never change
        // the craft's gameplay footprint.
        public const float PlayerHitboxRadius = 0.112f;
        public static readonly Vector2 PlayerHitboxOffset =
            new Vector2(-0.004f, 0f);

        public const float ThrustForce = 55f;
        public const float RotationTorque = 0.4f;
        public const float StabilizationSpeed = 720f;
        public const float FlightAngularDamping = 0.85f;
        public const float RotationBoostMultiplier = 0.225f;
        // Presentable-flight bridge: retain the raw EE5 torque above for the
        // eventual prefab rip, while the current slice needs a little more
        // immediate authority and a bounded spin so the craft feels deliberate
        // instead of either soupy or accidentally uncontrollable.
        public const float PlayerPresentableRotationTorque = 0.72f;
        public const float PlayerPresentableMaxAngularVelocity = 270f;
        // EE5's sniper prefab has JetpackMotor and JetpackInput both enabled.
        // JetpackInput applies the same direct force/torque after the motor
        // step. Keep that observable compatibility quirk named and switchable.
        public const bool PlayerLegacyDirectPhysicsAssist = true;
        // The raw EE5 path has no hidden neutral-rotation correction, but the
        // current career-presentable bridge needs a small settle so a new player
        // can read the craft without losing the authored ability to flip. S/C
        // remains the full-strength explicit stabilize command.
        public const bool UprightAssistEnabled = true;
        public const float UprightAssistWindow = 20f;
        // Presentable bridge values; the later raw prefab-rip pass can disable
        // the assist without changing the underlying EE5 constants above.
        public const float UprightAssistSpeed = 96f;
        public const float UprightAssistAngularBrake = 120f;
        // Brief release delay keeps the settle from fighting the final turn tap.
        public const float UprightAssistReleaseDelay = 0.06f;
        // Only the low-speed neutral settle uses this ceiling; held turns remain free.
        public const float UprightAssistMaxAngularSpeed = 110f;
        // The keyboard bindings remain full-strength, while a drifting analog
        // stick must not become an invisible rotation command. These are input
        // hygiene values, not a replacement for the authored flight torque.
        public const float PlayerTurnDeadzone = 0.08f;
        public const float PlayerThrustDeadzone = 0.04f;
        public const bool PlayerRemoveVelocityIntoColliders = true;
        public const float PlayerBoostedExhaustLengthMultiplier = 1.25f;
        public const float PlayerBoostedExhaustWidthMultiplier = 1.15f;
        public const float PlayerBoostedExhaustYScale = 1.5f;
        public const float PlayerBoostedParticleEmissionMultiplier = 1.4f;
        // EE5 expands these root-space X anchors by 1.55 when its visual-size
        // hack is active. These are the resulting sniper positions.
        public static readonly Vector3 PlayerLeftExhaustAnchor =
            new Vector3(-0.246f * 1.55f, 0.087f, 0f);
        public static readonly Vector3 PlayerRightExhaustAnchor =
            new Vector3(0.24f * 1.55f, 0.074f, 0f);
        public static readonly Color PlayerBoostedExhaustCoreColor =
            new Color(0.85f, 1f, 1f, 1f);
        public static readonly Color PlayerBoostedExhaustMidColor =
            new Color(0.15f, 0.75f, 1f, 0.92f);
        public static readonly Color PlayerBoostedExhaustTipColor =
            new Color(0.02f, 0.12f, 1f, 0f);

        public const float PlayerFireCooldown = 1f;
        public const float PlayerRecoilForce = 12f;
        public const float PlayerProjectileSpeed = 30f;
        public const float PlayerProjectileLifetime = 2f;
        public const float PlayerProjectileDamage = 1f;
        public const float PlayerProjectileKnockback = 0f;
        public const bool PlayerProjectileDestroysOnUnknownCollision = true;
        public const float ProjectileNearMissDistance = 1.35f;
        // The EE5 realScene player does not own a generic impact-damage
        // component. Keep the reusable experiment available, but do not let
        // ordinary arena-wall contact alter the gold-standard hull loop.
        public const bool PlayerCollisionDamageEnabled = false;
        // EE5's brittle SpriteShape gives the craft a short physics-clock
        // pass-through window after the collider is disabled. These values
        // are intentionally named so the builder, wall prop, and motor share
        // one contact-handoff contract.
        public const float BrittleFollowThroughAssistDuration = 0.16f;
        public const float BrittleAngularVelocityRetention = 0.18f;

        // EE5 separates the readable wake-line envelope from the enemy's
        // actual six-unit activation trigger. The telegraph can enter the
        // frame early while the enemy remains dormant until WakeDistance.
        public const float EnemyWakeSignalDistanceMultiplier = 4f;
        public const float EnemyGunnerChaseSpeed = 2f;
        // This is the serialized moveSpeed from EE5's "Enemy 01 - close
        // bruiser" scene variant. It is deliberately slower than the generic
        // enemyFast prefab: the close bruiser closes distance with a readable
        // sword beat instead of tunneling into the player.
        public const float EnemyMeleeChaseSpeed = 2.3f;
        public const float EnemyGunnerFaceTurnSpeed = 5f;
        // EE5's close-bruiser scene variant used a slightly quicker facing
        // response than the white gunner. Keeping this role-specific prevents
        // the melee sprite from lagging behind without reintroducing jitter.
        public const float EnemyMeleeFaceTurnSpeed = 6.6f;
        // A kinematic melee body should not visibly rotate for sub-degree target
        // noise after it has reached its attack stop. This is below the authored
        // five-degree-per-step response and only removes render chatter.
        public const float EnemyFacingDeadbandDegrees = 0.5f;
        // Enemy art uses flipY to stay upright while the body turns. A small
        // dead band prevents a target hovering on the vertical threshold from
        // toggling the sprite orientation every render frame.
        public const float EnemyFacingFlipHysteresisDegrees = 8f;
        // EE5's purple melee intro uses EnemyAI's fixed invertScaleXDuringIntro
        // path. It does not chase the player's horizontal side while dormant;
        // the authored active sprite is restored when the intro hands off to
        // combat. Keeping this false prevents the idle strip from chattering
        // as the player crosses the enemy's horizontal midpoint.
        public const bool EnemyMeleeFacesDormantTarget = false;
        public const bool EnemyMeleeInvertsSpriteDuringIntro = true;
        public const float EnemyDormantFacingHysteresis = 0.2f;
        // Stop just outside the combined player/melee collider radii. This
        // keeps contact damage active without making a dynamic enemy repeatedly
        // push into the player's body and jitter at the attack threshold.
        public const float EnemyMeleeAttackRange = 1.25f;
        // EE5 deals melee damage on actual contact. The EE6 controller stops
        // just outside the colliders to avoid dynamic-body tug-of-war, so the
        // range hit must stay near the combined collider edge rather than
        // firing from the full state-transition radius.
        public const float EnemyMeleeContactRange = 0.95f;
        // Hold the melee attack state briefly after a confirmed hit so the
        // outward knockback reads as an attack beat instead of immediately
        // causing a chase/attack state tug-of-war.
        public const float EnemyMeleeAttackRecoveryDuration = 0.16f;
        // The purple EE5 melee craft presents one authored sword frame rather
        // than a separate swing strip. Hold that strike direction inside the
        // attack band so small player-physics corrections cannot make the
        // whole sprite popcorn between angles; refresh only after a real
        // positional re-aim is needed.
        public const float EnemyMeleeAttackFacingRefreshDegrees = 22f;
        // EE6's melee controller uses a kinematic navigation body and a
        // deterministic range hit. A trigger body preserves wall/target casts
        // without letting scripted motion shove the dynamic player.
        public const bool EnemyMeleeUsesTriggerBody = true;
        // These are the actual EE5 enemyFast/enemyGun BoxCollider2D values,
        // including their artwork-relative offsets. They are deliberately
        // separate from attack range: a hurtbox defines projectile contact,
        // while the controller's attack envelope defines readable behavior.
        public static readonly Vector2 EnemyMeleeHitboxOffset =
            new Vector2(-0.27931988f, 0.13768846f);
        public static readonly Vector2 EnemyMeleeHitboxSize =
            new Vector2(0.70768476f, 0.63822603f);
        public static readonly Vector2 EnemyGunnerHitboxOffset =
            new Vector2(-0.2606567f, 0.13837318f);
        public static readonly Vector2 EnemyGunnerHitboxSize =
            new Vector2(0.87690234f, 1.151379f);
        // A small exit band prevents a melee hunter from alternating between
        // Chasing and Attacking when the player hovers exactly on its brake
        // radius. The authored EE5 movement remains the reference; this band
        // only makes the EE6 state-machine handoff deterministic.
        public const float EnemyMeleeAttackExitRange = 1.45f;
        // realScene2's enemyGun uses MoveType.Wander: after its intro it
        // patrols a small authored radius and keeps firing, rather than
        // converting into a chase/orbit enemy when the player moves away.
        public const float EnemyGunnerWanderRadius = 2f;
        public const float EnemyGunnerWanderDurationMin = 1f;
        public const float EnemyGunnerWanderDurationMax = 3f;
        // EE5's enemyFast prefab mirrors the root transform on X while its
        // facing controller keeps the authored +X basis. Preserve that
        // convention instead of trying to compensate by flipping the sword
        // sprite or changing the target angle at runtime.
        public const float EnemyMeleeRootScaleX = -1f;
        public const float EnemyGunnerRootScaleX = 1f;
        public const float EnemyGunnerFireCooldown = 1f;
        public const float EnemyGunnerProjectileSpeed = 6f;
        // EnemyBullet's authored lifetime in the EE5 bullet prefab is two
        // seconds. The gunner's six-unit speed and one-second cadence make
        // that a readable pressure window without leaving stale shots around
        // the room for the entire encounter.
        public const float EnemyGunnerProjectileLifetime = 2f;
        public const float EnemyGunnerProjectileDamage = 1f;
        public const float EnemyGunnerProjectileKnockback = 2.5f;
        public const bool EnemyGunnerProjectileDestroysOnUnknownCollision = false;
        // Copied from the EE5 enemyGun prefab. The small positive Y offset is
        // part of the authored muzzle silhouette, and it must mirror when the
        // upright sprite crosses the vertical facing boundary.
        public static readonly Vector3 EnemyGunnerFirePointLocalPosition =
            new Vector3(-0.542f, 0.079f, 0f);
        public const bool EnemyGunnerMirrorFirePointYWithUprightFlip = true;
        public const bool EnemyGunnerRequiresAttackRange = false;
        public const bool EnemyGunnerRequiresLineOfSightToFire = false;
        public const bool EnemyGunnerDrawAimTelegraph = false;
        public const float EnemyInvulnerabilityDuration = 0f;
        public const float EnemyContactDamage = 1f;
        public const float EnemyContactCooldown = 0.75f;
        public const float EnemyContactKnockback = 8f;
        public const float EnemyDefeatTimeScale = 0.16f;
        public const float EnemyDefeatSlowdownDuration = 0.07f;
        public const float EnemyWakeSignalChargeDuration = 1.15f;
        public const float EnemyWakeSignalChargeDecay = 1.8f;
        // EE5 charges the wake line slowly at the edge of its four-times
        // envelope and accelerates as the player closes in. Keeping both
        // endpoints named makes the approach feel intentional instead of
        // hiding a second tuning curve inside EnemyController.
        public const float EnemyWakeSignalChargeSpeedAtEdge = 0.28f;
        public const float EnemyWakeSignalChargeSpeedAtClose = 1.85f;
        public const float EnemyWakeBuildupDuration = 1.35f;
        public const float EnemyWakeFinalWarningDuration = 0.35f;
        public const float EnemyWakeIdleDurationMin = 1.1f;
        public const float EnemyWakeIdleDurationMax = 2.25f;
        // The EE5 scream strips contain ten frames and play at fourteen FPS.
        // Keeping the authored duration here prevents the controller from
        // cutting the final presentation frame off at the state boundary.
        public const float EnemyWakeScreamDuration = 10f / 14f;

        // Objective handoff values are copied from the EE5 keyFollow and
        // wallFinal2 prefabs. Keep the motion source-of-truth here so a scene
        // rebuild cannot quietly turn the key into a different game feel.
        public const float EnergyGateLiftDistance = 12f;
        public const float EnergyGateLiftSpeed = 6f;
        public static readonly Vector3 EnergyKeyEnemyOffset =
            new Vector3(-0.5f, 0f, 0f);
        public const float EnergyKeyEnemyOrbitRadius = 3f;
        public const float EnergyKeyEnemyOrbitSpeed = 3f;
        public const float EnergyKeyEnemyOrbitSharpness = 6f;
        public const float EnergyKeyOrbitRadiusX = 4.4f;
        public const float EnergyKeyOrbitRadiusY = 1.9f;
        public const float EnergyKeyOrbitSpeed = 2f;
        public const float EnergyKeyOrbitSharpness = 8f;
        public const float EnergyKeyRadiusEase = 3.5f;
        public const float EnergyKeyCenterFollowSharpness = 5.5f;
        public const float EnergyKeyVisualScale = 1.65f;
        // energy_key.png keeps its artwork in the upper half of a transparent
        // 96x64 canvas; move the visual down so the gameplay root stays centered.
        public static readonly Vector3 EnergyKeyVisualOffset = new Vector3(0f, -0.13f, 0f);
        public const float EnergyKeyCollectDistance = 0.65f;
        public const float EnergyKeyGateUnlockRange = 2f;
        public const float EnergyKeyGateFlySpeed = 14f;
        public static readonly Vector3 EnergyKeyPlayerOffset =
            new Vector3(0.5f, 0.7f, 0f);
        public const float EnergyKeyPlayerFollowSharpness = 12f;

        // Generated FlightTest room landmarks. Keeping these together makes
        // a layout pass reviewable and prevents the builder's objective route,
        // instruction triggers, and collision lanes from drifting independently.
        public static readonly Vector2 VerticalSlicePlayerSpawn = new Vector2(0f, 0f);
        public static readonly Vector2 VerticalSliceMeleeSpawn = new Vector2(3.25f, 2.25f);
        public static readonly Vector2 VerticalSliceGunnerSpawn = new Vector2(5f, -2f);
        public static readonly Vector2 VerticalSliceGatePosition = new Vector2(5f, 0f);
        public static readonly Vector2 VerticalSliceGateColliderSize = new Vector2(0.35f, 3.8f);
        public static readonly Vector2 VerticalSliceGateKeyTarget = new Vector2(0f, 1.7f);
        // The gate art is a bottom-aligned strip on a square source canvas.
        // After the authored 90-degree rotation, this negative X offset
        // cancels the source-pivot displacement and centers the red strip on
        // the blocking collider.
        public static readonly Vector3 EnergyGateArtworkLocalPosition =
            new Vector3(-2.02f, -0.15f, 0f);
        public const float EnergyGateArtworkScale = 2.6f;
        public static readonly Vector2 VerticalSliceExitPosition = new Vector2(6.8f, 0f);
        public const float VerticalSliceExitRadius = 1.45f;
        public static readonly Vector2 VerticalSliceArenaHalfExtents = new Vector2(8f, 6f);
        public const float VerticalSliceBoundaryThickness = 0.5f;
        public const float VerticalSliceBoundaryOverscan = 2f;
        public static readonly Vector2 VerticalSliceUpperShelfPosition = new Vector2(0.8f, 4.15f);
        public static readonly Vector2 VerticalSliceUpperShelfSize = new Vector2(4.2f, 0.35f);
        public static readonly Vector2 VerticalSliceLowerShelfPosition = new Vector2(-0.6f, -4.15f);
        public static readonly Vector2 VerticalSliceLowerShelfSize = new Vector2(4.8f, 0.35f);
        public static readonly Vector2 VerticalSliceExtractionSpinePosition = new Vector2(6.2f, 2.35f);
        public static readonly Vector2 VerticalSliceExtractionSpineSize = new Vector2(0.35f, 2.5f);
        public static readonly Vector2 VerticalSliceHazardPosition = new Vector2(0f, -2.4f);
        public const float VerticalSliceHazardRadius = 1.15f;
        public static readonly Vector2 VerticalSliceHealthCachePosition = new Vector2(-5.8f, 4.4f);
        public static readonly Vector2 VerticalSliceFireRateCachePosition = new Vector2(-5.8f, -4.4f);
        public static readonly Vector2 VerticalSliceFlightInstructionPosition = new Vector2(0f, 0f);
        public static readonly Vector2 VerticalSliceFlightInstructionSize = new Vector2(4.2f, 3.4f);
        public static readonly Vector2 VerticalSliceKeyInstructionPosition = new Vector2(2.5f, 3.5f);
        public static readonly Vector2 VerticalSliceKeyInstructionSize = new Vector2(4.8f, 2.8f);
        public static readonly Vector2 VerticalSliceGateInstructionPosition = new Vector2(5f, 0f);
        public static readonly Vector2 VerticalSliceGateInstructionSize = new Vector2(2.2f, 5f);
        public static readonly Vector2 VerticalSliceExitInstructionPosition = new Vector2(6.8f, 0f);
        public static readonly Vector2 VerticalSliceExitInstructionSize = new Vector2(3f, 4f);

        public const float FlightStopperCenterY = -2f;
        public const float FlightStopperWidth = 12f;
        public const float FlightStopperHeight = 2f;

        public const float CameraFollowSpeed = 12f;
        // realScene2 and realScene3 use the tighter four-unit frame. Keep the
        // room framing in the profile so a builder pass cannot drift back to
        // the broader prototype camera by accident.
        public const float CameraOrthographicSize = 4f;
        public static readonly Color CameraBackgroundColor =
            new Color(0.015f, 0.012f, 0.035f, 1f);
        public const float CameraVelocityLead = 0.24f;
        public const float CameraMaxLeadDistance = 3.75f;
        public const float CameraFacingLead = 1.15f;
        public const float CameraLeadSmooth = 10f;
        public const float CameraCatchupDistance = 1.4f;
        public const float CameraCatchupBoost = 2.2f;
        public const float CameraHardCatchupDistance = 5f;
        public const float CameraCloseEnoughSnap = 0.04f;
        // realScene2/realScene3 begin the zoom curve as soon as the craft moves.
        // Keeping this in the shared profile prevents a scene rebuild from
        // silently restoring the more conservative prototype value.
        public const float CameraSpeedZoomStart = 0f;
        public const float CameraSpeedZoomFull = 18f;
        public const float CameraMaxZoomOut = 2.25f;
        public const float CameraZoomSmooth = 10f;
        // These deliberately long flip values are the authored realScene2/3
        // presentation: flipping widens the read of the room and settles back
        // over the maneuver instead of popping like a one-shot camera effect.
        public const float CameraFlipZoomOut = 4f;
        public const float CameraFlipZoomDuration = 10f;
        // realScene sends wall impact feedback through the flight motor even
        // when generic collision damage is disabled. These values keep that
        // readable slam response independent from the optional damage module.
        public const float CameraWallSlamMinSpeed = 4.5f;
        public const float CameraWallSlamMaxSpeed = 18f;
        public const float CameraWallSlamShakeStrength = 0.14f;
        public const float CameraWallSlamShakeDuration = 0.18f;
        public const float CameraWallSlamCooldown = 0.14f;
        // These are the serialized realScene2/3 layer strengths: the far
        // nebula follows almost completely and the generated star field lags
        // just enough to sell depth.
        public const float CameraFarParallaxStrength = 0.995f;
        public const float CameraMidParallaxStrength = 0.95f;
        public const float CameraNearParallaxStrength = 0.95f;
        public const float KeyReleasePulseDuration = 0.28f;
        public const float KeyReleasePulseScale = 1.28f;
        public const string WallTag = "Wall";

        /// <summary>
        /// Recognizes the wall identity used by both generated EE6 rooms and
        /// hand-authored/imported EE5 room pieces. Some EE5 scenes serialized
        /// the lowercase tag and some colliders inherit the tag from a parent,
        /// so gameplay code must not depend on one exact object layout.
        /// </summary>
        public static bool IsWallCollider(Collider2D collider, string configuredTag = null)
        {
            if (!collider)
                return false;

            Transform current = collider.transform;
            while (current)
            {
                string tag = current.tag;
                if ((!string.IsNullOrEmpty(configuredTag)
                        && string.Equals(tag, configuredTag, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(tag, WallTag, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, "wall", StringComparison.OrdinalIgnoreCase))
                    return true;

                current = current.parent;
            }

            return false;
        }
    }
}
