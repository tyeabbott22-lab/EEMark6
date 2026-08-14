using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.U2D;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Enemy;
using ExtraterrestrialExhaust.Player;
using Object = UnityEngine.Object;

namespace ExtraterrestrialExhaust.Tests.PlayMode
{
    /// <summary>
    /// Exercises the public scene's real gameplay handoffs. Direct damage is
    /// used only after one live projectile has proved the weapon/hitbox path;
    /// this keeps the test deterministic without replacing gameplay systems.
    /// </summary>
    public sealed class PublicResumeSlicePlayModeTests : InputTestFixture
    {
        const string ScenePath = "Assets/Scenes/FlightTest.unity";
        Keyboard testKeyboard;

        [UnitySetUp]
        public IEnumerator LoadPublicSlice()
        {
            Time.timeScale = 1f;
            AsyncOperation load = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, $"Could not load {ScenePath}.");
            while (!load.isDone)
                yield return null;

            // Give scene-owned Awake, OnEnable, Start, and one physics step a
            // chance to establish their normal production references.
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        [UnityTearDown]
        public IEnumerator RestoreGlobalTime()
        {
            testKeyboard = null;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator PlayerKeyboardInput_DrivesFlightMotorAndCamera()
        {
            PlayerCharacter player = Object.FindFirstObjectByType<PlayerCharacter>();
            PlayerCameraFollow cameraFollow = Object.FindFirstObjectByType<PlayerCameraFollow>();
            Camera gameplayCamera = Camera.main;

            Assert.That(player, Is.Not.Null, "FlightTest has no PlayerCharacter.");
            Assert.That(player.FlightInput, Is.Not.Null, "Player input adapter is not composed.");
            Assert.That(player.FlightMotor, Is.Not.Null, "Player flight motor is not composed.");
            Assert.That(player.FlightState.CurrentState, Is.EqualTo(PlayerFlightState.FreeFlight));
            Assert.That(cameraFollow, Is.Not.Null, "FlightTest has no gameplay camera follow.");
            Assert.That(cameraFollow.Target, Is.EqualTo(player), "The gameplay camera is not bound to the player.");
            Assert.That(gameplayCamera, Is.Not.Null, "FlightTest has no MainCamera.");
            ParticleSystem[] exhaustParticles = player.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(exhaustParticles.Length, Is.GreaterThanOrEqualTo(2),
                "The player has no composed left/right exhaust particle systems.");

            Rigidbody2D body = player.FlightMotor.Body;
            Assert.That(body, Is.Not.Null);

            // The virtual keyboard exercises the production InputAction asset
            // and EE5 compatibility adapter. Physics is isolated from gravity
            // for this short contract test so only authored flight force is
            // responsible for the measured displacement.
            testKeyboard = InputSystem.AddDevice<Keyboard>("EE6 PlayMode Test Keyboard");
            player.Health.ConfigureDamageRules(1000f, 0.05f);
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.None;
            player.FlightMotor.ResetFacingForRespawn();
            PlaceBody(body, Vector2.zero, 0f);

            // Let the camera settle onto the reset pose before measuring its
            // response to player movement.
            for (int i = 0; i < 12; i++)
                yield return null;

            Vector2 thrustStart = body.position;
            Vector3 cameraStart = gameplayCamera.transform.position;
            Press(testKeyboard.wKey);
            yield return null;

            Assert.That(Keyboard.current, Is.EqualTo(testKeyboard),
                "Another keyboard replaced the deterministic test device.");
            Assert.That(Keyboard.current.wKey.isPressed, Is.True,
                "The current keyboard lost its W state before gameplay sampled it.");
            Assert.That(player.FlightInput.isActiveAndEnabled, Is.True,
                "PlayerFlightInput became disabled during the scene settle.");
            Assert.That(player.FlightState.CurrentState, Is.EqualTo(PlayerFlightState.FreeFlight),
                "The player left FreeFlight before keyboard input could be sampled.");
            Assert.That(Object.FindFirstObjectByType<GameStateMachine>().CurrentState,
                Is.EqualTo(GameState.Playing),
                "The game left Playing before keyboard input could be sampled.");
            Assert.That(player.FlightInput.Move.y, Is.GreaterThan(0.9f),
                "W did not reach the production PlayerFlightInput adapter.");

            for (int i = 0; i < 12; i++)
                yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(player.FlightMotor.AppliedFlightInput.y, Is.GreaterThan(0.9f),
                "The flight motor did not consume the thrust command.");
            Assert.That(player.FlightMotor.ControlMode, Is.EqualTo(PlayerFlightControlMode.Thrusting));
            Assert.That(body.position.y - thrustStart.y, Is.GreaterThan(0.1f),
                "W did not move the real Rigidbody2D along the craft's thrust axis.");
            Assert.That(gameplayCamera.transform.position.y - cameraStart.y, Is.GreaterThan(0.05f),
                "The gameplay camera did not follow the thrusting player.");
            bool exhaustWasPlaying = false;
            for (int i = 0; i < exhaustParticles.Length; i++)
            {
                if (!exhaustParticles[i].isPlaying || exhaustParticles[i].particleCount <= 0)
                    continue;

                exhaustWasPlaying = true;
                break;
            }
            Assert.That(exhaustWasPlaying, Is.True,
                "Thrust did not start any visible exhaust particles.");

            // Verify turning separately so thrust displacement cannot hide a
            // missing torque binding or a frozen Rigidbody2D constraint.
            Release(testKeyboard.wKey);
            yield return null;
            PlaceBody(body, Vector2.zero, 0f);
            Press(testKeyboard.dKey);
            yield return null;

            Assert.That(Keyboard.current, Is.EqualTo(testKeyboard),
                "Another keyboard replaced the deterministic test device.");
            Assert.That(player.FlightInput.Move.x, Is.GreaterThan(0.9f),
                "D did not reach the production PlayerFlightInput adapter.");

            for (int i = 0; i < 10; i++)
                yield return new WaitForFixedUpdate();

            Assert.That(player.FlightMotor.AppliedFlightInput.x, Is.GreaterThan(0.9f),
                "The flight motor did not consume the turn command.");
            Assert.That(player.FlightMotor.ControlMode, Is.EqualTo(PlayerFlightControlMode.Turning));
            Assert.That(Mathf.Abs(body.rotation), Is.GreaterThan(1f),
                "D did not rotate the real player Rigidbody2D.");
        }

        [UnityTest]
        [Timeout(45000)]
        public IEnumerator BrittlePassage_RetainsARealSpriteShapeDentBeforeBreaking()
        {
            GameObject passage = GameObject.Find("Brittle Launch Gate - SpriteShape");
            PlayerCharacter player = Object.FindFirstObjectByType<PlayerCharacter>();
            Assert.That(passage, Is.Not.Null, "The public room has no staged brittle passage.");
            Assert.That(player, Is.Not.Null);

            BrittleWall brittle = passage.GetComponent<BrittleWall>();
            SpriteShapeController spriteShape = passage.GetComponent<SpriteShapeController>();
            PolygonCollider2D collider = passage.GetComponent<PolygonCollider2D>();
            Assert.That(brittle, Is.Not.Null, "The brittle passage has no impact behavior.");
            Assert.That(spriteShape, Is.Not.Null, "The brittle passage is not a SpriteShape.");
            Assert.That(collider, Is.Not.Null, "The brittle passage has no gameplay contour.");

            int pointCount = spriteShape.spline.GetPointCount();
            Vector2[] beforeSpline = new Vector2[pointCount];
            Vector2[] beforeCollider = collider.points;
            for (int i = 0; i < pointCount; i++)
                beforeSpline[i] = spriteShape.spline.GetPosition(i);

            Rigidbody2D body = player.FlightMotor.Body;
            player.Health.ConfigureDamageRules(1000f, 0.05f);
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            PlaceBody(body, new Vector2(collider.bounds.min.x - 0.4f, collider.bounds.center.y), 0f);
            body.linearVelocity = Vector2.right * 3f;
            Physics2D.SyncTransforms();

            yield return WaitForCondition(
                () => brittle.DeformationMagnitude > 0f,
                3f,
                "A normal contact did not leave a persistent brittle-wall dent.");

            bool splineChanged = false;
            for (int i = 0; i < pointCount; i++)
            {
                if (Vector2.Distance(beforeSpline[i], spriteShape.spline.GetPosition(i)) <= 0.001f)
                    continue;

                splineChanged = true;
                break;
            }
            Assert.That(splineChanged, Is.True,
                "The brittle chip did not alter the authored SpriteShape contour.");
            Vector2[] afterCollider = collider.points;
            bool colliderChanged = afterCollider.Length != beforeCollider.Length;
            if (!colliderChanged)
            {
                for (int i = 0; i < afterCollider.Length; i++)
                {
                    if (Vector2.Distance(beforeCollider[i], afterCollider[i]) <= 0.001f)
                        continue;

                    colliderChanged = true;
                    break;
                }
            }
            Assert.That(colliderChanged, Is.True,
                "The brittle collider did not follow its newly dented contour.");
            Assert.That(brittle.IsBroken, Is.False,
                "A low-speed demonstration contact should dent the passage before breaking it.");
        }

        [UnityTest]
        [Timeout(45000)]
        public IEnumerator BrittlePassage_BreaksOnSecondDirectPassAndPreservesMomentum()
        {
            GameObject passage = GameObject.Find("Brittle Launch Gate - SpriteShape");
            PlayerCharacter player = Object.FindFirstObjectByType<PlayerCharacter>();
            BrittleWall brittle = passage ? passage.GetComponent<BrittleWall>() : null;
            PolygonCollider2D collider = passage ? passage.GetComponent<PolygonCollider2D>() : null;
            Assert.That(brittle, Is.Not.Null);
            Assert.That(collider, Is.Not.Null);
            Assert.That(player, Is.Not.Null);

            Rigidbody2D body = player.FlightMotor.Body;
            player.Health.ConfigureDamageRules(1000f, 0.05f);
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;

            Vector2 approachPoint = new Vector2(
                collider.bounds.min.x - 0.4f,
                collider.bounds.center.y);
            PlaceBody(body, approachPoint, 0f);
            body.linearVelocity = Vector2.right * 3f;
            Physics2D.SyncTransforms();
            yield return WaitForCondition(
                () => brittle.ChipHits >= 1,
                3f,
                "The opening brittle pass did not register a readable chip.");
            Assert.That(brittle.IsBroken, Is.False,
                "The first teaching-pass impact should leave a dent, not skip straight to destruction.");

            yield return new WaitForSeconds(0.12f);
            PlaceBody(body, approachPoint, 0f);
            body.linearVelocity = Vector2.right * 3f;
            Physics2D.SyncTransforms();
            yield return WaitForCondition(
                () => brittle.IsBroken,
                3f,
                "The second direct brittle pass did not open the route.");

            Assert.That(body.linearVelocity.x, Is.GreaterThan(2.4f),
                "The brittle break removed the craft's forward momentum instead of handing it through the opened lane.");
        }

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator OneHitMelee_PresentsASpinningHealthReadout()
        {
            EnemyController melee = null;
            foreach (EnemyController enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            {
                if (enemy.IsMelee)
                {
                    melee = enemy;
                    break;
                }
            }

            Assert.That(melee, Is.Not.Null, "The public room has no close-bruiser role.");
            EnemyHealthDisplay display = melee.GetComponent<EnemyHealthDisplay>();
            HealthComponent health = melee.GetComponent<HealthComponent>();
            Assert.That(display, Is.Not.Null, "The close bruiser has no health-readout system.");
            Assert.That(health, Is.Not.Null);
            Assert.That(health.MaxHealth, Is.LessThanOrEqualTo(1.01f));
            yield return WaitForCondition(
                () => display.IsVisible && display.DisplayRenderer.sprite,
                2f,
                "The close bruiser's health readout never became visible.");

            float startAngle = display.DisplayRenderer.transform.localEulerAngles.z;
            yield return new WaitForSeconds(0.25f);
            float endAngle = display.DisplayRenderer.transform.localEulerAngles.z;
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(startAngle, endAngle)),
                Is.GreaterThan(20f),
                "The visible close-bruiser health readout is not using its EE5-style spin tell.");
        }

        [UnityTest]
        [Timeout(45000)]
        public IEnumerator PublicSlice_CompletesCombatKeyGateAndExtractionRoute()
        {
            PlayerCharacter player = Object.FindFirstObjectByType<PlayerCharacter>();
            PlayerWeapon weapon = player ? player.GetComponent<PlayerWeapon>() : null;
            GameStateMachine gameState = Object.FindFirstObjectByType<GameStateMachine>();
            EncounterController encounter = Object.FindFirstObjectByType<EncounterController>();
            EnergyKey key = Object.FindFirstObjectByType<EnergyKey>();
            EnergyGate gate = Object.FindFirstObjectByType<EnergyGate>();
            ProgrammableLaserGate laserGate = Object.FindFirstObjectByType<ProgrammableLaserGate>();
            LevelExit exit = Object.FindFirstObjectByType<LevelExit>();
            SliceObjectiveDirector objective = Object.FindFirstObjectByType<SliceObjectiveDirector>();
            FlightStopperZone stopper = Object.FindFirstObjectByType<FlightStopperZone>();

            Assert.That(player, Is.Not.Null, "FlightTest has no PlayerCharacter.");
            Assert.That(player.FlightMotor, Is.Not.Null, "Player flight motor is not composed.");
            Assert.That(player.FlightInput, Is.Not.Null, "Player input adapter is not composed.");
            Assert.That(player.FlightState, Is.Not.Null, "Player flight state machine is not composed.");
            Assert.That(player.Health, Is.Not.Null, "Player health contract is not composed.");
            Assert.That(weapon, Is.Not.Null, "Player weapon is not composed.");
            Assert.That(weapon.FirePoint, Is.Not.Null, "Player weapon has no authored fire point.");
            Assert.That(gameState, Is.Not.Null);
            Assert.That(gameState.CurrentState, Is.EqualTo(GameState.Playing));
            Assert.That(encounter, Is.Not.Null);
            Assert.That(key, Is.Not.Null);
            Assert.That(gate, Is.Not.Null);
            Assert.That(laserGate, Is.Not.Null);
            Assert.That(laserGate.BeamCount, Is.EqualTo(1), "The public gate must use the single Laser Glow barrier.");
            Assert.That(laserGate.HasEmberEmitter, Is.True,
                "The EE5-style laser wall is missing its base ember emitter.");
            Assert.That(exit, Is.Not.Null);
            Assert.That(objective, Is.Not.Null);
            Assert.That(stopper, Is.Not.Null, "The lower basin has no explicit no-flight zone.");
            Assert.That(stopper.IsTriggerVolume, Is.True, "The no-flight zone must remain a trigger volume.");
            Assert.That(GameObject.Find("Playable Low Basin - SpriteShape (8)"), Is.Not.Null);
            Assert.That(GameObject.Find("Upper Crater Shelf - SpriteShape"), Is.Not.Null);
            Assert.That(GameObject.Find("Lower Crater Shelf - SpriteShape"), Is.Not.Null);
            Assert.That(GameObject.Find("Brittle Launch Gate - SpriteShape"), Is.Not.Null);
            Assert.That(GameObject.Find("Laser Wall - Vertical Forever Gate"), Is.Not.Null,
                "The public route is missing its named EE5 laser wall landmark.");

            EnemyController[] enemies = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            Assert.That(enemies, Has.Length.EqualTo(2), "The public encounter must contain exactly two roles.");
            EnemyController melee = null;
            EnemyController gunner = null;
            foreach (EnemyController enemy in enemies)
            {
                if (enemy.IsMelee)
                    melee = enemy;
                else
                    gunner = enemy;
            }

            Assert.That(melee, Is.Not.Null, "The close bruiser role is missing.");
            Assert.That(gunner, Is.Not.Null, "The ranged gunner role is missing.");
            Assert.That(melee.GetComponent<EnemyWeapon>(), Is.Null, "The bruiser must remain a contact role.");
            Assert.That(gunner.GetComponent<EnemyWeapon>(), Is.Not.Null, "The gunner has no ranged weapon.");

            Rigidbody2D playerBody = player.FlightMotor.Body;
            Rigidbody2D meleeBody = melee.GetComponent<Rigidbody2D>();
            Rigidbody2D gunnerBody = gunner.GetComponent<Rigidbody2D>();
            HealthComponent meleeHealth = melee.GetComponent<HealthComponent>();
            HealthComponent gunnerHealth = gunner.GetComponent<HealthComponent>();
            Assert.That(playerBody, Is.Not.Null);
            Assert.That(meleeBody, Is.Not.Null);
            Assert.That(gunnerBody, Is.Not.Null);
            Assert.That(meleeHealth, Is.Not.Null);
            Assert.That(gunnerHealth, Is.Not.Null);
            Collider2D gunnerHitbox = gunner.GameplayHitbox;
            Assert.That(gunnerHitbox, Is.Not.Null);
            Assert.That(gunnerHitbox.enabled, Is.True);
            Assert.That(gunnerBody.simulated, Is.True);

            // Keep the harness from dying while the real enemies wake and
            // attack. No enemy behavior or collision component is disabled.
            player.Health.ConfigureDamageRules(1000f, 0.05f);
            playerBody.gravityScale = 0f;
            playerBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            player.FlightMotor.ResetFacingForRespawn();
            PlaceBody(playerBody, Vector2.zero, 0f);
            weapon.ResetForRespawn();

            // Align the authored, offset EE5 hitbox—not merely the enemy root—
            // with the real fire pose. That keeps this a projectile/collider
            // test while remaining independent of imported root scale/pivots.
            // Keep the authored gunner in its staged encounter position and
            // move the player into a short, unobstructed firing lane. This
            // exercises the real projectile/hitbox pair without asking a live
            // kinematic enemy controller to accept a test-only teleport.
            Vector2 initialMuzzleOffset = (Vector2)weapon.FirePoint.position - playerBody.position;
            Vector2 initialShotDirection = initialMuzzleOffset.normalized;
            if (initialShotDirection.sqrMagnitude < 0.001f)
                initialShotDirection = Vector2.right;
            Vector2 desiredShotOrigin = (Vector2)gunnerHitbox.bounds.center - initialShotDirection * 0.8f;
            PlaceBody(playerBody, desiredShotOrigin - initialMuzzleOffset, 0f);
            weapon.ResetForRespawn();

            Vector2 shotOrigin = weapon.FirePoint.position;
            Vector2 shotDirection = (shotOrigin - playerBody.position).normalized;
            if (shotDirection.sqrMagnitude < 0.001f)
                shotDirection = Vector2.right;
            Assert.That(
                Vector2.Distance(gunnerHitbox.bounds.center, shotOrigin + shotDirection * 0.8f),
                Is.LessThan(0.05f),
                "The test could not align the real player firing lane with the gunner's authored hitbox.");
            RaycastHit2D shotLane = Physics2D.Raycast(shotOrigin, shotDirection, 1.2f);
            Assert.That(shotLane.collider, Is.EqualTo(gunnerHitbox),
                "The real player firing lane is obstructed before it reaches the gunner hitbox.");

            // Do not stop at a single proof-of-contact. The player report that
            // motivated this test was a gunner that accepted damage until its
            // final pip, then visually returned to idle and kept the key
            // locked. Exercise every authored one-damage player shot through
            // the live projectile / authored hitbox path so a late-hit failure
            // cannot be hidden by the objective shortcut further below.
            int firedCount = 0;
            weapon.Fired += HandleFired;
            for (int shot = 0; shot < Mathf.CeilToInt(gunnerHealth.MaxHealth); shot++)
            {
                // Re-align after recoil and the live gunner's wander step.
                // This leaves both actors and all their real components
                // enabled; it only keeps the test lane deterministic.
                Vector2 muzzleOffset = (Vector2)weapon.FirePoint.position - playerBody.position;
                Vector2 muzzleDirection = muzzleOffset.sqrMagnitude > 0.001f
                    ? muzzleOffset.normalized
                    : Vector2.right;
                // The enlarged gunner art extends beyond its compact EE5 box.
                // The opening shot deliberately skims just above that box to
                // cover the projectile-only visual hit assist; later shots
                // remain on the canonical authored center line.
                Vector2 shotTarget = shot == 0
                    ? (Vector2)gunnerHitbox.bounds.center
                        + Vector2.up * (gunnerHitbox.bounds.extents.y + 0.2f)
                    : (Vector2)gunnerHitbox.bounds.center;
                Vector2 desiredMuzzle = shotTarget - muzzleDirection * 0.8f;
                PlaceBody(playerBody, desiredMuzzle - muzzleOffset, 0f);
                playerBody.linearVelocity = Vector2.zero;
                weapon.ResetForRespawn();
                yield return new WaitForFixedUpdate();

                float healthBeforeShot = gunnerHealth.CurrentHealth;
                Assert.That(weapon.TryFire(), Is.True,
                    $"The composed player weapon refused gunner shot {shot + 1}.");
                yield return WaitForCondition(
                    () => gunnerHealth.CurrentHealth < healthBeforeShot,
                    2f,
                    $"Live player projectile {shot + 1} did not damage the gunner hitbox.");
            }
            weapon.Fired -= HandleFired;
            Assert.That(firedCount, Is.EqualTo(Mathf.CeilToInt(gunnerHealth.MaxHealth)),
                "The player weapon did not publish one firing event per gunner health pip.");

            yield return WaitForCondition(
                () => gunner.State == EnemyState.Defeated,
                2f,
                "The final live projectile removed the gunner's last health pip but did not defeat it.");

            yield return WaitForCondition(
                () => key && (key.IsAvailable || key.IsCollected),
                3f,
                "The gunner's defeated carrier did not release a collectible energy key.");
            Assert.That(objective.CurrentState, Is.Not.EqualTo(SliceObjectiveState.ClearEncounter),
                "The objective director kept the route on CLEAR ENCOUNTER after the key carrier died.");

            PlaceBody(playerBody, meleeBody.position + Vector2.left, 0f);
            yield return WaitForCondition(
                () => melee.State != EnemyState.Dormant,
                4f,
                "The nearby close bruiser never left its dormant state.");

            DamageInfo finishingDamage = new DamageInfo(
                100f,
                DamageType.Projectile,
                player.gameObject,
                direction: Vector2.right);
            Assert.That(meleeHealth.TryTakeDamage(finishingDamage), Is.True);
            yield return WaitForCondition(
                () => encounter.IsComplete
                    && melee.State == EnemyState.Defeated,
                3f,
                "The remaining close bruiser did not complete the authored encounter roster.");

            Assert.That(key, Is.Not.Null, "The key disappeared before collection.");
            yield return WaitForCondition(
                () => key && key.IsCollected,
                3f,
                "The released key did not converge into the player's collection range.");
            yield return WaitForCondition(
                () => objective.CurrentState == SliceObjectiveState.OpenExtractionGate,
                2f,
                "The objective director did not advance to the gate handoff.");

            PlaceBody(playerBody, (Vector2)gate.KeyTarget.position + Vector2.left, 0f);
            yield return WaitForCondition(
                () => gate.IsRouteClear,
                6f,
                "The collected key did not fly to the gate and clear the route.");
            Assert.That(gate.State, Is.EqualTo(EnergyGateState.Open));
            yield return WaitForCondition(
                () => objective.CurrentState == SliceObjectiveState.ReachExtraction,
                2f,
                "The objective director did not advance to extraction.");

            Collider2D exitCollider = exit.GetComponent<Collider2D>();
            Assert.That(exitCollider, Is.Not.Null);
            Assert.That(exitCollider.isTrigger, Is.True);
            PlaceBody(playerBody, exitCollider.bounds.center, 0f);
            yield return WaitForCondition(
                () => exit.IsCapturing,
                2f,
                "Physical overlap with the unlocked portal did not start capture.");
            yield return WaitForCondition(
                () => exit.IsComplete,
                5f,
                "The extraction capture did not reach completion.");

            Assert.That(objective.CurrentState, Is.EqualTo(SliceObjectiveState.ExtractionComplete));
            Assert.That(gameState.CurrentState, Is.EqualTo(GameState.GameOver));
            Assert.That(gameState.LastGameOverReason, Is.EqualTo(GameOverReason.ExtractionComplete));

            void HandleFired(Vector2 _, Vector2 __) => firedCount++;
        }

        static IEnumerator WaitForCondition(Func<bool> condition, float timeout, string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(condition(), Is.True, failureMessage);
        }

        static void PlaceBody(Rigidbody2D body, Vector2 position, float rotation)
        {
            // Rigidbody2D.position updates the simulation pose, but an
            // interpolated body can keep child transforms on its previous
            // render pose until the next physics step. Tests that read a
            // fire point or authored hitbox immediately after a teleport need
            // both representations synchronized.
            body.transform.SetPositionAndRotation(
                new Vector3(position.x, position.y, body.transform.position.z),
                Quaternion.Euler(0f, 0f, rotation));
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            Physics2D.SyncTransforms();
        }
    }
}
