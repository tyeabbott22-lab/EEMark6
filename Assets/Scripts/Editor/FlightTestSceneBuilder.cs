using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Player;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Enemy;

namespace ExtraterrestrialExhaust.Editor
{
    /// <summary>
    /// Rebuilds the deterministic vertical-slice scene used to validate the player
    /// foundation and the EE5-parity gameplay loop. Generated composition keeps
    /// the scene reproducible while authored art stays centrally configured.
    /// </summary>
    public static class FlightTestSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/FlightTest.unity";
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        const string ProjectilePrefabPath = "Assets/Prefabs/PlayerProjectile.prefab";
        const string PlayerPrefabPath = "Assets/Prefabs/PlayerCraft.prefab";
        const string EnemyGunnerPrefabPath = "Assets/Prefabs/EnemyGunner.prefab";
        const string EnemyMeleePrefabPath = "Assets/Prefabs/EnemyMelee.prefab";
        const string ShipSpritePath = "Assets/Art/Player/sprSnipe.png";
        const string HealthSpritePath = "Assets/Art/Player/health.png";
        const string BulletSpritePath = "Assets/Art/Player/bullet.png";
        const string ThrustAudioPath = "Assets/Audio/Player/sfxThrust.wav";
        const string EnemySpritePath = "Assets/Art/Reference/Enemies/sprAlienWhiteGunner.png";
        const string EnemyIdleSpritePath = "Assets/Art/Reference/Enemies/sprAlienWhiteSleep.png";
        const string EnemyDefeatSpritePath = "Assets/Art/Reference/Enemies/sprAlienWhiteScream.png";
        const string MeleeSpritePath = "Assets/Art/Reference/Enemies/sprPurpleEat.png";
        const string MeleeDefeatSpritePath = "Assets/Art/Reference/Enemies/sprAlienPurpleScream.png";
        const string KeySpritePath = "Assets/Art/Reference/Objectives/keyfinal.png";
        const string GateSpritePath = "Assets/Art/Reference/Objectives/buttonFInal1.png";
        const string WallSpritePath = "Assets/Art/Reference/Environment/wallFinal.png";
        const string StarfieldSpritePath = "Assets/Art/Reference/Environment/sprStars.png";
        const string EnemyBurstSpritePath = "Assets/Art/Reference/Effects/sprExplode.png";
        const string EnemyBurstAudioPath = "Assets/Audio/Reference/sfxExplode.wav";

        [MenuItem("Extraterrestrial Exhaust/Build Flight Test Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            PlayerProjectile projectilePrefab = CreateProjectilePrefab();

            CreateBackdrop();
            CreateGameStateMachine(inputAsset);
            new GameObject("Score System").AddComponent<ScoreSystem>();
            PlayerCharacter player = CreatePlayer(inputAsset, projectilePrefab);
            CreateCamera(player);
            EnemyController meleeEnemy = CreateEnemy(
                projectilePrefab,
                "Purple Melee Hunter",
                new Vector2(3.25f, 2.25f),
                false,
                EnemyMeleePrefabPath,
                MeleeSpritePath,
                MeleeSpritePath,
                MeleeDefeatSpritePath);
            EnemyController gunnerEnemy = CreateEnemy(
                projectilePrefab,
                "White Gunner",
                new Vector2(5f, -2f),
                true,
                EnemyGunnerPrefabPath,
                EnemySpritePath,
                EnemyIdleSpritePath,
                EnemyDefeatSpritePath);
            CreateEncounterAndExit(meleeEnemy, gunnerEnemy);
            CreateHud();

            CreateArenaBoundaries();

            // The gold-standard slice is intentionally focused on the EE5 loop:
            // encounter -> key -> gate -> extraction. Pickup and hazard scripts
            // remain reusable runtime systems, but are not silently mixed into
            // this authored test room as prototype-era sandbox clutter.

            EditorSceneManager.SaveScene(scene, ScenePath);
            ConfigureBuildSettings();
            Selection.activeGameObject = GameObject.Find("Player Craft");
            Debug.Log($"Built {ScenePath}. Use W/S to thrust or stabilize and A/D to rotate.");
        }

        static void CreateBackdrop()
        {
            GameObject backdrop = new GameObject("Starfield Backdrop");
            backdrop.transform.position = new Vector3(0f, 0f, 4f);
            backdrop.transform.localScale = Vector3.one * 4f;

            SpriteRenderer renderer = backdrop.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadFirstSprite(StarfieldSpritePath);
            renderer.sortingOrder = -100;
            renderer.color = new Color(0.55f, 0.62f, 0.8f, 1f);
        }

        static void CreateGameStateMachine(InputActionAsset inputAsset)
        {
            GameObject game = new GameObject("Game State");
            GameStateMachine stateMachine = game.AddComponent<GameStateMachine>();
            stateMachine.ConfigureInputAsset(inputAsset);
            SerializedObject serialized = new SerializedObject(stateMachine);
            serialized.FindProperty("initialState").enumValueIndex = (int)GameState.Playing;
            serialized.FindProperty("enableEnemyDefeatSlowdown").boolValue = true;
            serialized.FindProperty("enemyDefeatTimeScale").floatValue = 0.16f;
            serialized.FindProperty("enemyDefeatSlowdownDuration").floatValue = 0.07f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static PlayerCharacter CreatePlayer(InputActionAsset inputAsset, PlayerProjectile projectilePrefab)
        {
            GameObject player = new GameObject("Player Craft");
            player.tag = "Player";
            player.transform.position = Vector3.zero;

            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.mass = 8f;
            body.gravityScale = 0.285f;
            body.linearDamping = 0.35f;
            body.angularDamping = 3.25f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D playerCollider = player.AddComponent<CircleCollider2D>();
            playerCollider.radius = 0.112f;
            playerCollider.offset = new Vector2(-0.004f, 0f);
            PlayerCharacter character = player.AddComponent<PlayerCharacter>();
            PlayerFlightMotor motor = player.GetComponent<PlayerFlightMotor>();
            SerializedObject serializedMotor = new SerializedObject(motor);
            serializedMotor.FindProperty("thrustForce").floatValue = 55f;
            serializedMotor.FindProperty("rotationTorque").floatValue = 0.4f;
            serializedMotor.FindProperty("rotationAddsThrust").boolValue = true;
            serializedMotor.FindProperty("rotationBoostMultiplier").floatValue = 0.225f;
            serializedMotor.FindProperty("stabilizationSpeed").floatValue = 720f;
            serializedMotor.FindProperty("angularDamping").floatValue = 0.85f;
            serializedMotor.FindProperty("stabilizationAngle").floatValue = 0f;
            serializedMotor.ApplyModifiedPropertiesWithoutUndo();
            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            SerializedObject serializedPlayerHealth = new SerializedObject(playerHealth);
            serializedPlayerHealth.FindProperty("maxHealth").floatValue = 5f;
            serializedPlayerHealth.FindProperty("invulnerabilityDuration").floatValue = 1f;
            serializedPlayerHealth.ApplyModifiedPropertiesWithoutUndo();
            PlayerFlightInput input = player.GetComponent<PlayerFlightInput>();
            if (inputAsset)
                input.ConfigureInputAsset(inputAsset);
            player.AddComponent<PlayerRespawnController>();

            PlayerWeaponInput weaponInput = player.AddComponent<PlayerWeaponInput>();
            if (inputAsset)
                weaponInput.ConfigureInputAsset(inputAsset);

            PlayerWeapon weapon = player.AddComponent<PlayerWeapon>();
            SerializedObject serializedWeapon = new SerializedObject(weapon);
            serializedWeapon.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
            serializedWeapon.FindProperty("fireCooldown").floatValue = 0.12f;
            serializedWeapon.FindProperty("recoilForce").floatValue = 2f;
            serializedWeapon.FindProperty("drawAimLine").boolValue = true;
            serializedWeapon.FindProperty("aimLineMaxDistance").floatValue = 120f;
            serializedWeapon.FindProperty("aimLineWidth").floatValue = 0.035f;
            serializedWeapon.FindProperty("aimLineColor").colorValue = new Color(1f, 1f, 1f, 0.32f);
            serializedWeapon.FindProperty("aimLineEnemyColor").colorValue = new Color(1f, 0.08f, 0.04f, 0.58f);
            serializedWeapon.FindProperty("aimLineSortingOrder").intValue = 5000;
            serializedWeapon.ApplyModifiedPropertiesWithoutUndo();

            PlayerFlightPresentation presentation = player.AddComponent<PlayerFlightPresentation>();
            AudioSource thrustAudio = player.AddComponent<AudioSource>();
            thrustAudio.playOnAwake = false;
            thrustAudio.loop = true;
            thrustAudio.volume = 0.18f;
            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("thrustAudio").objectReferenceValue = thrustAudio;
            serializedPresentation.FindProperty("thrustClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(ThrustAudioPath);
            serializedPresentation.FindProperty("exhaustLength").floatValue = 0.55f;
            serializedPresentation.FindProperty("turnExhaustAmount").floatValue = 1f;
            serializedPresentation.FindProperty("squashScale").vector2Value = new Vector2(1.25f, 0.75f);
            serializedPresentation.FindProperty("squashDuration").floatValue = 0.12f;
            serializedPresentation.FindProperty("squashReturnSpeed").floatValue = 14f;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();

            CreateHealthDisplay(character);

            GameObject firePoint = new GameObject("Fire Point");
            firePoint.transform.SetParent(player.transform, false);
            firePoint.transform.localPosition = new Vector3(0.55f, 0f, 0f);
            serializedWeapon = new SerializedObject(weapon);
            serializedWeapon.FindProperty("firePoint").objectReferenceValue = firePoint.transform;
            serializedWeapon.ApplyModifiedPropertiesWithoutUndo();

            CreateCraftVisual(player.transform);
            player.AddComponent<PlayerDamageFeedback>();
            player.AddComponent<PlayerCollisionDamage>();
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                player,
                PlayerPrefabPath,
                InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
            return character;
        }

        static void CreateHealthDisplay(PlayerCharacter character)
        {
            GameObject displayObject = new GameObject("Health Display");
            displayObject.transform.SetParent(character.transform, false);
            displayObject.transform.localPosition = Vector3.zero;
            displayObject.transform.localScale = Vector3.one * 2f;

            SpriteRenderer renderer = displayObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 30;
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(HealthSpritePath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .ToArray();

            PlayerHealthDisplay display = character.gameObject.AddComponent<PlayerHealthDisplay>();
            SerializedObject serialized = new SerializedObject(display);
            serialized.FindProperty("displayRenderer").objectReferenceValue = renderer;
            SerializedProperty spriteProperty = serialized.FindProperty("healthSprites");
            spriteProperty.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                spriteProperty.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static PlayerProjectile CreateProjectilePrefab()
        {
            EnsureFolder("Assets/Prefabs");

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);
            bool editingExisting = existing;
            GameObject projectile = editingExisting
                ? PrefabUtility.LoadPrefabContents(ProjectilePrefabPath)
                : new GameObject("Player Projectile");

            Rigidbody2D body = projectile.GetComponent<Rigidbody2D>();
            if (!body)
                body = projectile.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D collider = projectile.GetComponent<CircleCollider2D>();
            if (!collider)
                collider = projectile.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.12f;

            PlayerProjectile projectileComponent = projectile.GetComponent<PlayerProjectile>();
            if (!projectileComponent)
                projectileComponent = projectile.AddComponent<PlayerProjectile>();
            SerializedObject serializedProjectile = new SerializedObject(projectileComponent);
            serializedProjectile.FindProperty("speed").floatValue = 45f;
            serializedProjectile.FindProperty("lifetime").floatValue = 2f;
            serializedProjectile.FindProperty("damage").floatValue = 1f;
            serializedProjectile.FindProperty("knockback").floatValue = 0f;
            serializedProjectile.FindProperty("destroyOnUnrecognizedCollision").boolValue = true;
            serializedProjectile.ApplyModifiedPropertiesWithoutUndo();

            SpriteRenderer sprite = projectile.GetComponent<SpriteRenderer>();
            if (!sprite)
                sprite = projectile.AddComponent<SpriteRenderer>();
            sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BulletSpritePath);
            sprite.sortingOrder = 21;

            LineRenderer line = projectile.GetComponent<LineRenderer>();
            if (!line)
                line = projectile.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(-0.3f, 0f));
            line.SetPosition(1, new Vector3(0.3f, 0f));
            line.startWidth = 0.08f;
            line.endWidth = 0.08f;
            line.startColor = Color.white;
            line.endColor = new Color(1f, 0.1f, 0.1f);
            line.sortingOrder = 20;
            line.material = new Material(Shader.Find("Sprites/Default"));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(projectile, ProjectilePrefabPath);
            if (editingExisting)
                PrefabUtility.UnloadPrefabContents(projectile);
            else
                Object.DestroyImmediate(projectile);

            AssetDatabase.SaveAssets();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);
            return prefab.GetComponent<PlayerProjectile>();
        }

        static EnemyController CreateEnemy(
            PlayerProjectile projectilePrefab,
            string objectName,
            Vector2 position,
            bool ranged,
            string prefabPath,
            string activeSpritePath,
            string dormantSpritePath,
            string defeatedSpritePath)
        {
            GameObject enemy = new GameObject(objectName);
            enemy.transform.position = position;

            Rigidbody2D body = enemy.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.linearDamping = 2f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D collider = enemy.AddComponent<CircleCollider2D>();
            // The melee hitbox is slightly larger than its visual body so that
            // contact damage becomes reliable before the chase state brakes.
            collider.radius = ranged ? 0.55f : 0.72f;
            HealthComponent health = enemy.AddComponent<HealthComponent>();
            SerializedObject serializedHealth = new SerializedObject(health);
            serializedHealth.FindProperty("maxHealth").floatValue = ranged ? 5f : 3f;
            serializedHealth.FindProperty("invulnerabilityDuration").floatValue = 0.1f;
            serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            EnemyController controller = enemy.AddComponent<EnemyController>();
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("detectionRange").floatValue = 12f;
            serializedController.FindProperty("wakeDistance").floatValue = 6f;
            serializedController.FindProperty("wakeDuration").floatValue = 1.35f;
            serializedController.FindProperty("attackRange").floatValue = ranged ? 7f : 0.8f;
            // Match the two authored EE5 roles: the purple close bruiser is
            // slightly slower than the orbiting white gunner, but both remain
            // fast enough to pressure a moving pilot.
            serializedController.FindProperty("chaseSpeed").floatValue = ranged ? 2.7f : 2.8f;
            serializedController.FindProperty("wallTag").stringValue = "Wall";
            serializedController.FindProperty("wallBuffer").floatValue = 0.03f;
            serializedController.FindProperty("steerAroundWalls").boolValue = true;
            serializedController.FindProperty("steeringCommitTime").floatValue = 0.28f;
            serializedController.FindProperty("steeringProbeAngle").floatValue = 55f;
            serializedController.FindProperty("steeringProbeDistance").floatValue = 1.15f;
            serializedController.FindProperty("steeringPlayerBias").floatValue = 0.35f;
            serializedController.FindProperty("wallSlideCommitTime").floatValue = 0.42f;
            serializedController.FindProperty("wallSlideNormalPush").floatValue = 0.22f;
            serializedController.FindProperty("stuckSampleTime").floatValue = 0.28f;
            serializedController.FindProperty("stuckMinProgress").floatValue = 0.06f;
            serializedController.FindProperty("stuckEscapeCommitTime").floatValue = 0.5f;
            serializedController.FindProperty("blockOtherEnemies").boolValue = true;
            serializedController.FindProperty("otherEnemyBuffer").floatValue = 0.025f;
            serializedController.FindProperty("orbitWhileAttacking").boolValue = ranged;
            serializedController.FindProperty("orbitRadius").floatValue = 1.5f;
            serializedController.FindProperty("orbitMoveSpeed").floatValue = 2f;
            serializedController.FindProperty("orbitAngularSpeed").floatValue = ranged ? 135f : 100f;
            serializedController.FindProperty("orbitDirection").floatValue = 1f;
            serializedController.FindProperty("faceTurnSpeed").floatValue = ranged ? 7.4f : 7.6f;
            serializedController.FindProperty("keepSpriteUpright").boolValue = true;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            if (!ranged)
            {
                // The gunner's threat is ranged pressure; giving it the melee
                // contact contract as well makes the two roster roles collapse
                // into the same punishment pattern.
                EnemyContactDamage contactDamage = enemy.AddComponent<EnemyContactDamage>();
                SerializedObject serializedContact = new SerializedObject(contactDamage);
                serializedContact.FindProperty("damage").floatValue = 1f;
                serializedContact.FindProperty("cooldown").floatValue = 0.64f;
                serializedContact.FindProperty("knockback").floatValue = 9f;
                serializedContact.ApplyModifiedPropertiesWithoutUndo();
            }
            enemy.AddComponent<DamageFlashFeedback>();
            if (ranged)
            {
                EnemyWeapon weapon = enemy.AddComponent<EnemyWeapon>();
                GameObject firePoint = new GameObject("Enemy Fire Point");
                firePoint.transform.SetParent(enemy.transform, false);
                firePoint.transform.localPosition = new Vector3(-0.65f, 0f, 0f);
                SerializedObject serializedWeapon = new SerializedObject(weapon);
                serializedWeapon.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
                serializedWeapon.FindProperty("firePoint").objectReferenceValue = firePoint.transform;
                serializedWeapon.FindProperty("attackRange").floatValue = 7f;
                serializedWeapon.FindProperty("fireCooldown").floatValue = 0.5f;
                serializedWeapon.FindProperty("projectileSpeed").floatValue = 9.5f;
                serializedWeapon.FindProperty("projectileKnockback").floatValue = 2.5f;
                serializedWeapon.FindProperty("projectileTint").colorValue = new Color(0.05f, 1f, 0.16f, 1f);
                serializedWeapon.ApplyModifiedPropertiesWithoutUndo();
            }

            LineRenderer line = enemy.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            line.SetPositions(new[]
            {
                new Vector3(-0.55f, -0.55f), new Vector3(-0.55f, 0.55f),
                new Vector3(0.55f, 0.55f), new Vector3(0.55f, -0.55f)
            });
            line.startWidth = 0.1f;
            line.endWidth = 0.1f;
            line.startColor = ranged
                ? new Color(1f, 0.08f, 0.18f)
                : new Color(0.8f, 0.12f, 1f);
            line.endColor = ranged
                ? new Color(1f, 0.65f, 0.1f)
                : new Color(1f, 0.2f, 0.65f);
            line.material = new Material(Shader.Find("Sprites/Default"));

            SpriteRenderer sprite = enemy.AddComponent<SpriteRenderer>();
            sprite.sprite = LoadFirstSprite(activeSpritePath);
            sprite.sortingOrder = 10;

            EnemySpritePresentation presentation = enemy.AddComponent<EnemySpritePresentation>();
            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("spriteRenderer").objectReferenceValue = sprite;
            SetSpriteArray(serializedPresentation, "dormantSprites", LoadSprites(dormantSpritePath));
            SetSpriteArray(serializedPresentation, "alertSprites", LoadSprites(defeatedSpritePath));
            SetSpriteArray(serializedPresentation, "activeSprites", LoadSprites(activeSpritePath));
            SetSpriteArray(serializedPresentation, "defeatedSprites", LoadSprites(defeatedSpritePath));
            serializedPresentation.FindProperty("animationFramesPerSecond").floatValue = 10f;
            serializedPresentation.FindProperty("wakeFramesPerSecond").floatValue = 14f;
            serializedPresentation.FindProperty("defeatDisplayDuration").floatValue = 0.3f;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            CreateEnemyHealthDisplay(enemy.transform, ranged);

            EnemyWakePresentation wakePresentation = enemy.AddComponent<EnemyWakePresentation>();
            SerializedObject serializedWake = new SerializedObject(wakePresentation);
            serializedWake.FindProperty("chargingColor").colorValue = new Color(0.45f, 0.1f, 1f, 0.22f);
            serializedWake.FindProperty("readyColor").colorValue = new Color(0.08f, 1f, 0.34f, 0.9f);
            serializedWake.FindProperty("minWidth").floatValue = 0.014f;
            serializedWake.FindProperty("maxWidth").floatValue = 0.11f;
            serializedWake.FindProperty("sortingOrder").intValue = 16;
            serializedWake.ApplyModifiedPropertiesWithoutUndo();

            EnemyDeathPresentation deathPresentation = enemy.AddComponent<EnemyDeathPresentation>();
            SerializedObject serializedDeath = new SerializedObject(deathPresentation);
            SetSpriteArray(serializedDeath, "burstFrames", LoadSprites(EnemyBurstSpritePath));
            serializedDeath.FindProperty("defeatAudio").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(EnemyBurstAudioPath);
            serializedDeath.FindProperty("burstScale").floatValue = 3f;
            serializedDeath.FindProperty("burstSortingOrder").intValue = 40;
            serializedDeath.FindProperty("audioVolume").floatValue = 0.65f;
            serializedDeath.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAssetAndConnect(
                enemy,
                prefabPath,
                InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static void CreateEnemyHealthDisplay(Transform enemy, bool ranged)
        {
            GameObject displayObject = new GameObject("Health Display");
            displayObject.transform.SetParent(enemy, false);
            // EE5 overlays the health sheet on the enemy body. Keeping this
            // as a child preserves the authored scale while the enemy moves.
            displayObject.transform.localPosition = Vector3.zero;
            displayObject.transform.localScale = Vector3.one * 2f;

            SpriteRenderer renderer = displayObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 30;
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(HealthSpritePath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .ToArray();

            EnemyHealthDisplay display = enemy.gameObject.AddComponent<EnemyHealthDisplay>();
            SerializedObject serialized = new SerializedObject(display);
            serialized.FindProperty("displayRenderer").objectReferenceValue = renderer;
            SerializedProperty spriteProperty = serialized.FindProperty("healthSprites");
            spriteProperty.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                spriteProperty.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            serialized.FindProperty("showOnStartDuration").floatValue = ranged ? 0.8f : 0.66f;
            serialized.FindProperty("showOnHitDuration").floatValue = ranged ? 0.6f : 0.66f;
            serialized.FindProperty("fadeSpeed").floatValue = 6f;
            serialized.FindProperty("rotationSpeed").floatValue = ranged ? 90f : 270f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CreateEncounterAndExit(
            EnemyController meleeEnemy,
            EnemyController gunnerEnemy)
        {
            GameObject encounterObject = new GameObject("Combat Encounter");
            EncounterController encounter = encounterObject.AddComponent<EncounterController>();
            SerializedObject serializedEncounter = new SerializedObject(encounter);
            SerializedProperty encounterEnemies = serializedEncounter.FindProperty("encounterEnemies");
            encounterEnemies.arraySize = 2;
            encounterEnemies.GetArrayElementAtIndex(0).objectReferenceValue = meleeEnemy;
            encounterEnemies.GetArrayElementAtIndex(1).objectReferenceValue = gunnerEnemy;
            serializedEncounter.ApplyModifiedPropertiesWithoutUndo();

            GameObject gate = new GameObject("Energy Gate");
            gate.transform.position = new Vector3(5f, 4f, 0f);
            BoxCollider2D gateCollider = gate.AddComponent<BoxCollider2D>();
            gateCollider.size = new Vector2(0.35f, 2.8f);
            EnergyGate energyGate = gate.AddComponent<EnergyGate>();
            CreateSquareOutline(gate.transform, new Vector2(0.35f, 2.8f), new Color(0.2f, 0.55f, 1f));
            CreateGateVisual(gate.transform);

            GameObject key = new GameObject("Energy Key");
            key.transform.position = new Vector3(2.5f, 4f, 0f);
            CircleCollider2D keyCollider = key.AddComponent<CircleCollider2D>();
            keyCollider.isTrigger = true;
            EnergyKey energyKey = key.AddComponent<EnergyKey>();
            SerializedObject serializedKey = new SerializedObject(energyKey);
            serializedKey.FindProperty("requiredEncounter").objectReferenceValue = encounter;
            serializedKey.FindProperty("enemyTarget").objectReferenceValue = meleeEnemy;
            serializedKey.FindProperty("targetGate").objectReferenceValue = energyGate;
            serializedKey.FindProperty("enemyOrbitRadius").floatValue = 1f;
            serializedKey.FindProperty("enemyOrbitSpeed").floatValue = 4f;
            serializedKey.FindProperty("enemyOrbitSharpness").floatValue = 8f;
            serializedKey.FindProperty("playerFollowSharpness").floatValue = 14f;
            serializedKey.ApplyModifiedPropertiesWithoutUndo();
            SpriteRenderer keySprite = key.AddComponent<SpriteRenderer>();
            keySprite.sprite = LoadFirstSprite(KeySpritePath);
            keySprite.sortingOrder = 10;
            key.transform.localScale = Vector3.one * 0.7f;
            CreateSquareOutline(key.transform, Vector2.one * 0.5f, new Color(1f, 0.8f, 0.1f));

            GameObject exit = new GameObject("Level Exit");
            exit.transform.position = new Vector3(6f, 4f, 0f);
            CircleCollider2D collider = exit.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 2f;
            LevelExit levelExit = exit.AddComponent<LevelExit>();
            SerializedObject serializedExit = new SerializedObject(levelExit);
            serializedExit.FindProperty("requiredGate").objectReferenceValue = energyGate;
            serializedExit.ApplyModifiedPropertiesWithoutUndo();
            ExtractionPortalPresentation portal = exit.AddComponent<ExtractionPortalPresentation>();
            SerializedObject serializedPortal = new SerializedObject(portal);
            serializedPortal.FindProperty("portalDiameter").floatValue = 3.8f;
            serializedPortal.FindProperty("ringSegments").intValue = 80;
            serializedPortal.ApplyModifiedPropertiesWithoutUndo();
            CreateSquareOutline(exit.transform, Vector2.one * 0.9f, new Color(0.2f, 1f, 0.85f));
        }

        static void CreateHud()
        {
            GameObject canvasObject = new GameObject("Gameplay HUD");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject labelObject = new GameObject("Status Label");
            labelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = labelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -24f);
            rect.sizeDelta = new Vector2(560f, 100f);

            Text label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 24;
            label.lineSpacing = 1.1f;
            label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            GameplayHud hud = canvasObject.AddComponent<GameplayHud>();
            SerializedObject serialized = new SerializedObject(hud);
            serialized.FindProperty("statusLabel").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(scene => scene.path == ScenePath))
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));

            EditorBuildSettings.scenes = scenes.ToArray();
            PlayerSettings.productName = "Extraterrestrial Exhaust";
            PlayerSettings.companyName = "Extraterrestrial Exhaust";
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = System.IO.Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, folder);
        }

        static void CreateCraftVisual(Transform parent)
        {
            GameObject visual = new GameObject("Craft Visual");
            visual.transform.SetParent(parent, false);
            visual.transform.localScale = Vector3.one * 1.5f;

            SpriteRenderer sprite = visual.AddComponent<SpriteRenderer>();
            sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShipSpritePath);
            sprite.sortingOrder = 10;

            LineRenderer line = visual.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            line.SetPositions(new[]
            {
                new Vector3(0f, 0.7f),
                new Vector3(-0.45f, -0.45f),
                new Vector3(0f, -0.2f),
                new Vector3(0.45f, -0.45f)
            });
            line.startWidth = 0.08f;
            line.endWidth = 0.08f;
            line.startColor = new Color(0.2f, 0.85f, 1f);
            line.endColor = new Color(0.8f, 0.2f, 1f);
            line.sortingOrder = 5;
            line.material = new Material(Shader.Find("Sprites/Default"));
        }

        static void CreateCamera(PlayerCharacter target)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.backgroundColor = new Color(0.015f, 0.02f, 0.06f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            PlayerCameraFollow follow = cameraObject.AddComponent<PlayerCameraFollow>();
            SerializedObject serializedFollow = new SerializedObject(follow);
            serializedFollow.FindProperty("target").objectReferenceValue = target;
            serializedFollow.FindProperty("followSpeed").floatValue = 12f;
            serializedFollow.FindProperty("velocityLead").floatValue = 0.24f;
            serializedFollow.FindProperty("maxLeadDistance").floatValue = 3.75f;
            serializedFollow.FindProperty("facingLead").floatValue = 1.15f;
            serializedFollow.FindProperty("leadSmooth").floatValue = 10f;
            serializedFollow.FindProperty("catchupDistance").floatValue = 1.4f;
            serializedFollow.FindProperty("catchupBoost").floatValue = 2.2f;
            serializedFollow.FindProperty("hardCatchupDistance").floatValue = 5f;
            serializedFollow.FindProperty("closeEnoughSnap").floatValue = 0.04f;
            // These values mirror the EE5 SimpleCameraFollow profile. Keeping
            // them explicit prevents an inspector experiment from becoming the
            // serialized gold-standard scene tuning by accident.
            serializedFollow.FindProperty("speedZoomStart").floatValue = 6f;
            serializedFollow.FindProperty("speedZoomFull").floatValue = 18f;
            serializedFollow.FindProperty("maxZoomOut").floatValue = 2.25f;
            serializedFollow.FindProperty("zoomSmooth").floatValue = 10f;
            serializedFollow.FindProperty("flipZoomOut").floatValue = 1.4f;
            serializedFollow.FindProperty("flipZoomDuration").floatValue = 0.45f;
            serializedFollow.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CreateWall(string name, Vector2 position, Vector2 size)
        {
            GameObject wall = new GameObject(name);
            wall.tag = "Wall";
            wall.transform.position = position;

            BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
            collider.size = size;

            CreateWallVisual(wall.transform, size);
        }

        static void CreateArenaBoundaries()
        {
            // Keep the playable rectangle in one place so movement tuning and
            // future scene variants cannot drift apart from the authored room.
            CreateWall("Left Wall", new Vector2(-8f, 0f), new Vector2(0.5f, 14f));
            CreateWall("Right Wall", new Vector2(8f, 0f), new Vector2(0.5f, 14f));
            CreateWall("Floor", new Vector2(0f, -6f), new Vector2(16f, 0.5f));
            CreateWall("Ceiling", new Vector2(0f, 6f), new Vector2(16f, 0.5f));
        }

        static void CreateWallVisual(Transform parent, Vector2 size)
        {
            Sprite wallSprite = LoadFirstSprite(WallSpritePath);
            if (!wallSprite)
            {
                CreateSquareOutline(parent, size, new Color(0.3f, 0.35f, 0.6f));
                return;
            }

            // wallFinal is the authored EE5 wall strip: its visible artwork is
            // offset inside a large transparent sprite. Keep the art and the
            // collider aligned while allowing the builder to size the boundary.
            const float artworkCenterOffset = 6.2f;
            const float artworkAcrossScale = 1.45f;
            bool vertical = size.y >= size.x;
            float artworkLengthScale = (vertical ? size.y : size.x) / wallSprite.bounds.size.y;

            GameObject visual = new GameObject("Wall Visual");
            visual.transform.SetParent(parent, false);
            visual.transform.localRotation = vertical
                ? Quaternion.identity
                : Quaternion.Euler(0f, 0f, 90f);
            // The source sprite is always scaled in its native axes first;
            // horizontal boundaries then rotate that finished strip.
            visual.transform.localScale = new Vector3(
                artworkAcrossScale,
                artworkLengthScale,
                1f);
            visual.transform.localPosition = new Vector3(
                -artworkCenterOffset * artworkAcrossScale,
                0f,
                0f);

            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = wallSprite;
            renderer.color = new Color(0.52f, 0.63f, 0.9f, 0.9f);
            renderer.sortingOrder = -10;
        }

        static void CreateGateVisual(Transform parent)
        {
            Sprite gateSprite = LoadFirstSprite(GateSpritePath);
            if (!gateSprite)
                return;

            GameObject visual = new GameObject("Gate Artwork");
            visual.transform.SetParent(parent, false);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            visual.transform.localScale = Vector3.one * 1.35f;

            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = gateSprite;
            renderer.color = Color.white;
            renderer.sortingOrder = 5;
        }

        static void CreateSquareOutline(Transform parent, Vector2 size, Color color)
        {
            LineRenderer line = parent.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 4;
            Vector2 half = size * 0.5f;
            line.SetPositions(new[]
            {
                new Vector3(-half.x, -half.y), new Vector3(-half.x, half.y),
                new Vector3(half.x, half.y), new Vector3(half.x, -half.y)
            });
            line.startWidth = 0.08f;
            line.endWidth = 0.08f;
            line.startColor = color;
            line.endColor = color;
            line.material = new Material(Shader.Find("Sprites/Default"));
        }

        static Sprite LoadFirstSprite(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .FirstOrDefault();
        }

        static Sprite[] LoadSprites(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .ToArray();
        }

        static void SetSpriteArray(SerializedObject serialized, string propertyName, Sprite[] sprites)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            property.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }
    }
}
