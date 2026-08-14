using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
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
    public sealed class PublicResumeSlicePlayModeTests
    {
        const string ScenePath = "Assets/Scenes/FlightTest.unity";

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
            Time.timeScale = 1f;
            yield return null;
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
            Assert.That(exit, Is.Not.Null);
            Assert.That(objective, Is.Not.Null);
            Assert.That(stopper, Is.Not.Null, "The lower basin has no explicit no-flight zone.");
            Assert.That(stopper.IsTriggerVolume, Is.True, "The no-flight zone must remain a trigger volume.");
            Assert.That(GameObject.Find("Playable Low Basin - SpriteShape (8)"), Is.Not.Null);
            Assert.That(GameObject.Find("Upper Crater Shelf - SpriteShape"), Is.Not.Null);
            Assert.That(GameObject.Find("Lower Crater Shelf - SpriteShape"), Is.Not.Null);

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
            Vector2 shotOrigin = weapon.FirePoint.position;
            Vector2 shotDirection = (shotOrigin - playerBody.position).normalized;
            if (shotDirection.sqrMagnitude < 0.001f)
                shotDirection = Vector2.right;
            Vector2 desiredHitboxCenter = shotOrigin + shotDirection * 0.8f;
            Vector2 hitboxCorrection = desiredHitboxCenter - (Vector2)gunnerHitbox.bounds.center;
            PlaceBody(gunnerBody, gunnerBody.position + hitboxCorrection, 0f);
            Assert.That(
                Vector2.Distance(gunnerHitbox.bounds.center, desiredHitboxCenter),
                Is.LessThan(0.05f),
                "The test could not align the gunner's authored hitbox with the shot lane.");

            float gunnerHealthBeforeShot = gunnerHealth.CurrentHealth;
            int firedCount = 0;
            weapon.Fired += HandleFired;
            Assert.That(weapon.TryFire(), Is.True, "The composed player weapon refused its opening shot.");
            yield return WaitForCondition(
                () => gunnerHealth.CurrentHealth < gunnerHealthBeforeShot,
                2f,
                "A live player projectile did not damage the gunner hitbox.");
            weapon.Fired -= HandleFired;
            Assert.That(firedCount, Is.EqualTo(1), "The weapon did not publish exactly one firing event.");

            yield return WaitForCondition(
                () => gunner.State != EnemyState.Dormant,
                4f,
                "The nearby gunner never left its dormant state.");

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
            Assert.That(gunnerHealth.TryTakeDamage(finishingDamage), Is.True);
            yield return WaitForCondition(
                () => encounter.IsComplete
                    && melee.State == EnemyState.Defeated
                    && gunner.State == EnemyState.Defeated,
                3f,
                "Enemy death did not complete the authored encounter roster.");

            yield return WaitForCondition(
                () => key && key.State == EnergyKeyState.OrbitingPlayer,
                3f,
                "The carrier key did not release after the encounter.");
            Assert.That(objective.CurrentState, Is.EqualTo(SliceObjectiveState.CollectEnergyKey));

            float collectDeadline = Time.realtimeSinceStartup + 5f;
            while (key && !key.IsCollected && Time.realtimeSinceStartup < collectDeadline)
            {
                PlaceBody(playerBody, key.GameplayPosition, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(key, Is.Not.Null, "The key disappeared before collection.");
            Assert.That(key.IsCollected, Is.True, "Physical player/key proximity did not collect the key.");
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
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            Physics2D.SyncTransforms();
        }
    }
}
