using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.U2D;
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
        // These semantic destinations describe the imported EE5 roles. The
        // legacy paths remain as fallbacks until the explicit rename menu is
        // run, so an existing checkout never loses its visual wiring.
        const string PlayerCraftSpriteAssetPath = "Assets/Art/Player/player_craft.png";
        const string LegacyPlayerCraftSpriteAssetPath = "Assets/Art/Player/sprSnipe.png";
        // The purple UFO sheet from EE5 is a boss craft, not the player. It is
        // stored under the enemy reference library and deliberately never
        // enters the player presentation contract.
        const string PlayerHealthSpriteAssetPath = "Assets/Art/Player/health_sheet.png";
        const string LegacyPlayerHealthSpriteAssetPath = "Assets/Art/Player/health.png";
        const string PlayerProjectileSpriteAssetPath = "Assets/Art/Player/player_projectile.png";
        const string LegacyPlayerProjectileSpriteAssetPath = "Assets/Art/Player/bullet.png";
        const string ThrustAudioPath = "Assets/Audio/Player/sfxThrust.wav";
        const string EnemySpritePath = "Assets/Art/Reference/Enemies/sprAlienWhiteGunner.png";
        const string EnemyIdleSpritePath = "Assets/Art/Reference/Enemies/sprAlienWhiteSleep.png";
        const string EnemyDefeatSpritePath = "Assets/Art/Reference/Enemies/sprAlienWhiteScream.png";
        const string MeleeSpritePath = "Assets/Art/Reference/Enemies/sprPurpleEat.png";
        const string MeleeDefeatSpritePath = "Assets/Art/Reference/Enemies/sprAlienPurpleScream.png";
        const string EnergyKeySpriteAssetPath = "Assets/Art/Reference/Objectives/energy_key.png";
        const string LegacyEnergyKeySpriteAssetPath = "Assets/Art/Reference/Objectives/keyfinal.png";
        const string EnergyGateSpriteAssetPath = "Assets/Art/Reference/Objectives/energy_gate.png";
        const string LegacyEnergyGateSpriteAssetPath = "Assets/Art/Reference/Objectives/buttonFInal1.png";
        const string BoundaryWallSpriteAssetPath = "Assets/Art/Reference/Environment/boundary_wall.png";
        const string LegacyBoundaryWallSpriteAssetPath = "Assets/Art/Reference/Environment/wallFinal.png";
        const string MoonTerrainFillProfileAssetPath =
            "Assets/Art/Reference/Environment/Moon/MoonTerrainFillProfile.asset";
        const string StarfieldSpritePath = "Assets/Art/Reference/Environment/starfield_backdrop.png";
        const string LegacyStarfieldSpritePath = "Assets/Art/Reference/Environment/sprStars.png";
        const string EnemyBurstSpritePath = "Assets/Art/Reference/Effects/enemy_defeat_burst.png";
        const string LegacyEnemyBurstSpritePath = "Assets/Art/Reference/Effects/sprExplode.png";
        const string EnemyBurstAudioPath = "Assets/Audio/Reference/sfxExplode.wav";

        [MenuItem("Extraterrestrial Exhaust/Build Flight Test Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            PlayerProjectile projectilePrefab = CreateProjectilePrefab();

            Transform[] backdrops = CreateBackdrop();
            GameStateMachine gameState = CreateGameStateMachine(inputAsset);
            GameObject scoreObject = new GameObject("Score System");
            ScoreSystem scoreSystem = scoreObject.AddComponent<ScoreSystem>();
            SerializedObject serializedScore = new SerializedObject(scoreSystem);
            serializedScore.FindProperty("speedCreditThreshold").floatValue = 11f;
            serializedScore.FindProperty("speedCreditCooldown").floatValue = 0.45f;
            serializedScore.FindProperty("speedCreditPoints").intValue = 25;
            serializedScore.FindProperty("flipPoints").intValue = 100;
            serializedScore.FindProperty("enemyDamagePoints").intValue = 75;
            serializedScore.FindProperty("enemyDefeatPoints").intValue = 300;
            serializedScore.FindProperty("nearMissPoints").intValue = 150;
            serializedScore.FindProperty("objectiveCollectedPoints").intValue = 175;
            serializedScore.FindProperty("gateDeactivatedPoints").intValue = 250;
            serializedScore.FindProperty("wallBrokenPoints").intValue = 200;
            serializedScore.FindProperty("levelCompletedPoints").intValue = 500;
            serializedScore.FindProperty("startingComboSeconds").floatValue = 4f;
            serializedScore.FindProperty("minimumComboSeconds").floatValue = 1.25f;
            serializedScore.FindProperty("comboCreditsToMinimum").intValue = 18;
            serializedScore.FindProperty("timeMultiplierPerSecond").floatValue = 0.85f;
            serializedScore.FindProperty("exponentialMultiplierGrowth").floatValue = 1.34f;
            serializedScore.FindProperty("chainMultiplierGrowth").floatValue = 1.16f;
            serializedScore.FindProperty("lateChainSurgeGrowth").floatValue = 1.08f;
            serializedScore.FindProperty("lateChainSurgeStartsAt").intValue = 20;
            serializedScore.FindProperty("maximumMultiplier").floatValue = 50f;
            serializedScore.FindProperty("gameState").objectReferenceValue = gameState;
            serializedScore.ApplyModifiedPropertiesWithoutUndo();
            PlayerCharacter player = CreatePlayer(inputAsset, projectilePrefab, gameState);
            CreateCamera(player, backdrops);
            EnemyController meleeEnemy = CreateEnemy(
                gameState,
                projectilePrefab,
                "Purple Melee Hunter",
                new Vector2(3.25f, 2.25f),
                false,
                EnemyMeleePrefabPath,
                MeleeSpritePath,
                MeleeSpritePath,
                MeleeDefeatSpritePath);
            EnemyController gunnerEnemy = CreateEnemy(
                gameState,
                projectilePrefab,
                "White Gunner",
                new Vector2(5f, -2f),
                true,
                EnemyGunnerPrefabPath,
                EnemySpritePath,
                EnemyIdleSpritePath,
                EnemyDefeatSpritePath);
            SliceObjectiveDirector objectiveDirector = CreateEncounterAndExit(
                gameState,
                meleeEnemy,
                gunnerEnemy);
            CreateHud(objectiveDirector, gameState);
            CreateInstructionTriggers();

            CreateArenaBoundaries();
            CreateEnvironmentalPressure();

            // The gold-standard slice is intentionally focused on the EE5 loop:
            // encounter -> key -> gate -> extraction. Pickup and hazard scripts
            // remain optional pressure and recovery beats rather than becoming
            // hidden objective requirements.

            EditorSceneManager.SaveScene(scene, ScenePath);
            ConfigureBuildSettings();
            Selection.activeGameObject = GameObject.Find("Player Craft");
            Debug.Log($"Built {ScenePath}. Use W/S to thrust or stabilize and A/D to rotate.");
        }

        [MenuItem("Extraterrestrial Exhaust/Normalize Imported Sprite Names")]
        public static void NormalizeImportedSpriteNames()
        {
            // AssetDatabase.RenameAsset preserves each .meta GUID, which keeps
            // serialized EE5-derived references intact. This is deliberately a
            // separate command so importing the project never mutates artwork.
            int renamed = 0;
            renamed += RenameImportedAsset(
                LegacyPlayerCraftSpriteAssetPath,
                PlayerCraftSpriteAssetPath);
            renamed += RenameImportedAsset(
                LegacyPlayerHealthSpriteAssetPath,
                PlayerHealthSpriteAssetPath);
            renamed += RenameImportedAsset(
                LegacyPlayerProjectileSpriteAssetPath,
                PlayerProjectileSpriteAssetPath);
            renamed += RenameImportedAsset(
                LegacyEnergyKeySpriteAssetPath,
                EnergyKeySpriteAssetPath);
            renamed += RenameImportedAsset(
                LegacyEnergyGateSpriteAssetPath,
                EnergyGateSpriteAssetPath);
            renamed += RenameImportedAsset(
                LegacyBoundaryWallSpriteAssetPath,
                BoundaryWallSpriteAssetPath);
            renamed += RenameImportedAsset(
                LegacyStarfieldSpritePath,
                StarfieldSpritePath);
            renamed += RenameImportedAsset(
                LegacyEnemyBurstSpritePath,
                EnemyBurstSpritePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(renamed == 0
                ? "Imported sprite names already use the semantic EE6 paths, or need manual conflict review."
                : $"Renamed {renamed} imported sprite asset(s); EE5 GUID references were preserved. Enemy strip names were left untouched.");
        }

        [MenuItem("Extraterrestrial Exhaust/Repair Player Craft Sprite Wiring")]
        public static void RepairPlayerCraftSpriteWiring()
        {
            Sprite[] craftSprites = LoadSprites(
                PlayerCraftSpriteAssetPath,
                LegacyPlayerCraftSpriteAssetPath);
            if (craftSprites.Length == 0)
            {
                Debug.LogError(
                    $"Could not repair the player craft because no sprite was found at {PlayerCraftSpriteAssetPath}.");
                return;
            }

            GameObject prefabContents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool prefabRepaired = RepairPlayerCraftRoot(prefabContents, craftSprites);
            if (prefabContents)
            {
                if (prefabRepaired)
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, PlayerPrefabPath);
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            bool sceneRepaired = false;
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.path == ScenePath)
            {
                GameObject scenePlayer = GameObject.Find("Player Craft");
                if (scenePlayer)
                {
                    sceneRepaired = RepairPlayerCraftRoot(scenePlayer, craftSprites);
                    if (sceneRepaired)
                    {
                        EditorUtility.SetDirty(scenePlayer);
                        EditorSceneManager.MarkSceneDirty(activeScene);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Player craft sprite wiring repaired from {AssetDatabase.GetAssetPath(craftSprites[0])}. "
                + $"Prefab: {prefabRepaired}. Active FlightTest scene: {sceneRepaired}. "
                + "The purple UFO boss sprite is no longer a valid player presentation source.");
        }

        [MenuItem("Extraterrestrial Exhaust/Validate Player Craft Sprite Wiring")]
        public static void ValidatePlayerCraftSpriteWiring()
        {
            string expectedPath = ResolveAssetPath(
                PlayerCraftSpriteAssetPath,
                LegacyPlayerCraftSpriteAssetPath);
            Sprite expectedSprite = LoadFirstSprite(expectedPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            List<string> issues = GetPlayerCraftSpriteIssues(prefab, expectedSprite, "Prefab");

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.path == ScenePath)
            {
                GameObject scenePlayer = GameObject.Find("Player Craft");
                issues.AddRange(GetPlayerCraftSpriteIssues(
                    scenePlayer,
                    expectedSprite,
                    "Active FlightTest scene"));
            }

            if (issues.Count == 0)
            {
                Debug.Log(
                    $"Player craft sprite contract is valid for {expectedPath}: "
                    + "renderer, flight frames, and thrust frames all use the verified player asset.");
                return;
            }

            Debug.LogWarning(
                "Player craft sprite contract is stale: " + string.Join("; ", issues)
                + ". Run Extraterrestrial Exhaust > Repair Player Craft Sprite Wiring, "
                + "then save FlightTest.",
                prefab);
        }

        [MenuItem("Extraterrestrial Exhaust/Repair Enemy Prefab Profiles")]
        public static void RepairEnemyPrefabProfiles()
        {
            bool meleeRepaired = RepairEnemyPrefabProfile(EnemyMeleePrefabPath, false);
            bool gunnerRepaired = RepairEnemyPrefabProfile(EnemyGunnerPrefabPath, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "Repaired the EE5 enemy prefab profiles. "
                + $"Purple melee: {meleeRepaired}; white gunner: {gunnerRepaired}. "
                + "The gunner now mirrors toward the player during its dormant/wake intro, "
                + "and both prefabs use the four-times wake-line envelope. "
                + "Rebuild FlightTest afterward to propagate the same profile into the generated scene.");
        }

        [MenuItem("Extraterrestrial Exhaust/Validate Enemy Prefab Profiles")]
        public static void ValidateEnemyPrefabProfiles()
        {
            List<string> issues = new List<string>();
            ValidateEnemyPrefabProfile(EnemyMeleePrefabPath, false, issues);
            ValidateEnemyPrefabProfile(EnemyGunnerPrefabPath, true, issues);

            if (issues.Count == 0)
            {
                Debug.Log(
                    "Enemy prefab profiles match the EE5 intro contract: "
                    + "four-times wake-line range and role-appropriate dormant facing.");
                return;
            }

            Debug.LogWarning(
                "Enemy prefab profiles need the EE5 intro migration: "
                + string.Join("; ", issues)
                + ". Run Extraterrestrial Exhaust > Repair Enemy Prefab Profiles.");
        }

        [MenuItem("Extraterrestrial Exhaust/Repair Active FlightTest Player Profile")]
        public static void RepairActiveFlightTestPlayerProfile()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.path != ScenePath)
            {
                Debug.LogError(
                    $"Open {ScenePath} before repairing the active FlightTest player profile.");
                return;
            }

            List<string> sceneContractIssues = GetGeneratedSceneContractIssues();
            if (sceneContractIssues.Count > 0)
            {
                Debug.LogWarning(
                    "The active FlightTest is still a legacy or incomplete scene: "
                    + string.Join(", ", sceneContractIssues)
                    + ". Run Extraterrestrial Exhaust > Build Flight Test Scene before applying a player-only repair.");
                return;
            }

            GameObject playerObject = GameObject.Find("Player Craft");
            GameStateMachine gameState = UnityEngine.Object.FindFirstObjectByType<GameStateMachine>();
            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            PlayerProjectile projectilePrefab = AssetDatabase.LoadAssetAtPath<PlayerProjectile>(ProjectilePrefabPath);
            Sprite[] craftSprites = LoadSprites(
                PlayerCraftSpriteAssetPath,
                LegacyPlayerCraftSpriteAssetPath);

            if (!playerObject || !gameState || !inputAsset || !projectilePrefab || craftSprites.Length == 0)
            {
                Debug.LogError(
                    "FlightTest player repair needs Player Craft, Game State, input actions, projectile prefab, and the verified orange craft sprite.");
                return;
            }

            RepairPlayerCraftRoot(playerObject, craftSprites);
            ApplyGoldStandardPlayerProfile(
                playerObject,
                gameState,
                inputAsset,
                projectilePrefab);
            EditorUtility.SetDirty(playerObject);
            EditorSceneManager.MarkSceneDirty(activeScene);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "Repaired the active FlightTest player to the EE5 profile: "
                + "55 thrust, 0.35 linear damping, one-second player shots, "
                + "12 recoil, orange craft sprite, and room-reset death flow. "
                + "Save the scene to persist the migration.");
        }

        [MenuItem("Extraterrestrial Exhaust/Validate Active FlightTest Scene Contract")]
        public static void ValidateActiveFlightTestSceneContract()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.path != ScenePath)
            {
                Debug.LogWarning(
                    $"Active scene is not {ScenePath}. Open the generated FlightTest scene to validate its vertical-slice contract.");
                return;
            }

            List<string> issues = GetGeneratedSceneContractIssues();
            if (issues.Count == 0)
            {
                Debug.Log(
                    "Active FlightTest contains the generated EE5 vertical-slice contract: player, encounter, key, gate, exit, HUD, and enemy roster.");
                return;
            }

            Debug.LogWarning(
                "Active FlightTest is not the generated vertical slice. Missing or incomplete systems: "
                + string.Join(", ", issues)
                + ". Run Extraterrestrial Exhaust > Build Flight Test Scene.");
        }

        [MenuItem("Extraterrestrial Exhaust/Validate Active FlightTest Player Profile")]
        public static void ValidateActiveFlightTestPlayerProfile()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject playerObject = activeScene.IsValid() && activeScene.path == ScenePath
                ? GameObject.Find("Player Craft")
                : null;
            if (!playerObject)
            {
                Debug.LogWarning(
                    $"Active scene is not the generated FlightTest scene. Open {ScenePath} to validate the gold-standard player profile.");
                return;
            }

            List<string> mismatches = new List<string>();
            Rigidbody2D body = playerObject.GetComponent<Rigidbody2D>();
            if (!body || !Mathf.Approximately(body.mass, Ee5SliceProfile.PlayerMass))
                mismatches.Add($"mass={(body ? body.mass : -1f)} (expected {Ee5SliceProfile.PlayerMass})");
            if (!body || !Mathf.Approximately(body.gravityScale, Ee5SliceProfile.PlayerGravityScale))
                mismatches.Add($"gravityScale={(body ? body.gravityScale : -1f)} (expected {Ee5SliceProfile.PlayerGravityScale})");
            if (!body || !Mathf.Approximately(body.linearDamping, Ee5SliceProfile.PlayerLinearDamping))
                mismatches.Add($"linearDamping={(body ? body.linearDamping : -1f)} (expected {Ee5SliceProfile.PlayerLinearDamping})");

            PlayerWeapon weapon = playerObject.GetComponent<PlayerWeapon>();
            if (weapon)
            {
                SerializedObject serializedWeapon = new SerializedObject(weapon);
                float cooldown = serializedWeapon.FindProperty("fireCooldown").floatValue;
                float recoil = serializedWeapon.FindProperty("recoilForce").floatValue;
                if (!Mathf.Approximately(cooldown, Ee5SliceProfile.PlayerFireCooldown))
                    mismatches.Add($"fireCooldown={cooldown} (expected {Ee5SliceProfile.PlayerFireCooldown})");
                if (!Mathf.Approximately(recoil, Ee5SliceProfile.PlayerRecoilForce))
                    mismatches.Add($"recoilForce={recoil} (expected {Ee5SliceProfile.PlayerRecoilForce})");
            }
            else
            {
                mismatches.Add("PlayerWeapon is missing");
            }

            string message = mismatches.Count == 0
                ? "Active FlightTest player matches the EE5 gold-standard profile."
                : "Active FlightTest player profile is stale: " + string.Join(", ", mismatches)
                    + ". Run Extraterrestrial Exhaust > Repair Active FlightTest Player Profile.";
            if (mismatches.Count == 0)
                Debug.Log(message);
            else
                Debug.LogWarning(message, playerObject);
        }

        static void ApplyGoldStandardPlayerProfile(
            GameObject playerObject,
            GameStateMachine gameState,
            InputActionAsset inputAsset,
            PlayerProjectile projectilePrefab)
        {
            Rigidbody2D body = playerObject.GetComponent<Rigidbody2D>();
            if (body)
            {
                body.mass = Ee5SliceProfile.PlayerMass;
                body.gravityScale = Ee5SliceProfile.PlayerGravityScale;
                body.linearDamping = Ee5SliceProfile.PlayerLinearDamping;
                body.angularDamping = Ee5SliceProfile.PlayerAngularDamping;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                EditorUtility.SetDirty(body);
            }

            PlayerFlightStateMachine stateMachine = playerObject.GetComponent<PlayerFlightStateMachine>();
            if (stateMachine)
            {
                SerializedObject serializedState = new SerializedObject(stateMachine);
                serializedState.FindProperty("initialState").enumValueIndex = (int)PlayerFlightState.FreeFlight;
                serializedState.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerFlightMotor motor = playerObject.GetComponent<PlayerFlightMotor>();
            if (motor)
            {
                SerializedObject serializedMotor = new SerializedObject(motor);
                serializedMotor.FindProperty("enforceEe5Profile").boolValue = true;
                serializedMotor.FindProperty("thrustForce").floatValue = Ee5SliceProfile.ThrustForce;
                serializedMotor.FindProperty("rotationTorque").floatValue = Ee5SliceProfile.RotationTorque;
                serializedMotor.FindProperty("rotationAddsThrust").boolValue = true;
                serializedMotor.FindProperty("rotationBoostMultiplier").floatValue = Ee5SliceProfile.RotationBoostMultiplier;
                serializedMotor.FindProperty("stabilizationSpeed").floatValue = Ee5SliceProfile.StabilizationSpeed;
                serializedMotor.FindProperty("angularDamping").floatValue = Ee5SliceProfile.FlightAngularDamping;
                serializedMotor.FindProperty("stabilizationAngle").floatValue = 0f;
                serializedMotor.ApplyModifiedPropertiesWithoutUndo();
            }

            HealthComponent health = playerObject.GetComponent<HealthComponent>();
            if (health)
            {
                SerializedObject serializedHealth = new SerializedObject(health);
                serializedHealth.FindProperty("maxHealth").floatValue = 5f;
                serializedHealth.FindProperty("invulnerabilityDuration").floatValue = 1f;
                serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerFlightInput flightInput = playerObject.GetComponent<PlayerFlightInput>();
            if (flightInput)
            {
                flightInput.ConfigureInputAsset(inputAsset);
                SerializedObject serializedInput = new SerializedObject(flightInput);
                serializedInput.FindProperty("gameState").objectReferenceValue = gameState;
                serializedInput.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerRespawnController recovery = playerObject.GetComponent<PlayerRespawnController>();
            if (recovery)
            {
                SerializedObject serializedRecovery = new SerializedObject(recovery);
                serializedRecovery.FindProperty("reloadSceneOnDeath").boolValue = true;
                serializedRecovery.FindProperty("reloadDelay").floatValue = 0f;
                serializedRecovery.FindProperty("respawnAutomatically").boolValue = false;
                serializedRecovery.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerWeaponInput weaponInput = playerObject.GetComponent<PlayerWeaponInput>();
            if (weaponInput)
            {
                weaponInput.ConfigureInputAsset(inputAsset);
                SerializedObject serializedWeaponInput = new SerializedObject(weaponInput);
                serializedWeaponInput.FindProperty("gameState").objectReferenceValue = gameState;
                serializedWeaponInput.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerWeapon weapon = playerObject.GetComponent<PlayerWeapon>();
            if (weapon)
            {
                SerializedObject serializedWeapon = new SerializedObject(weapon);
                serializedWeapon.FindProperty("gameState").objectReferenceValue = gameState;
                serializedWeapon.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
                serializedWeapon.FindProperty("enforceEe5Profile").boolValue = true;
                serializedWeapon.FindProperty("keepFirePointRightOfOrigin").boolValue = true;
                serializedWeapon.FindProperty("firePointLocalOffset").vector2Value = new Vector2(0.55f, 0f);
                serializedWeapon.FindProperty("fireCooldown").floatValue = Ee5SliceProfile.PlayerFireCooldown;
                serializedWeapon.FindProperty("recoilForce").floatValue = Ee5SliceProfile.PlayerRecoilForce;
                serializedWeapon.FindProperty("drawAimLine").boolValue = true;
                serializedWeapon.FindProperty("aimLineMaxDistance").floatValue = 120f;
                serializedWeapon.FindProperty("aimLineWidth").floatValue = 0.035f;
                serializedWeapon.FindProperty("aimLineColor").colorValue = new Color(1f, 1f, 1f, 0.32f);
                serializedWeapon.FindProperty("aimLineEnemyColor").colorValue = new Color(1f, 0.08f, 0.04f, 0.58f);
                serializedWeapon.FindProperty("aimLineSortingOrder").intValue = 5000;
                serializedWeapon.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerFlightPresentation presentation = playerObject.GetComponent<PlayerFlightPresentation>();
            if (presentation)
            {
                SerializedObject serializedPresentation = new SerializedObject(presentation);
                serializedPresentation.FindProperty("boostedExhaustLengthMultiplier").floatValue = 1.25f;
                serializedPresentation.FindProperty("boostedExhaustWidthMultiplier").floatValue = 1.15f;
                serializedPresentation.FindProperty("boostedParticleEmissionMultiplier").floatValue = 1.4f;
                serializedPresentation.FindProperty("exhaustSideOffset").floatValue = 0.28f;
                serializedPresentation.FindProperty("exhaustLength").floatValue = 0.55f;
                serializedPresentation.FindProperty("turnExhaustAmount").floatValue = 1f;
                serializedPresentation.FindProperty("squashScale").vector2Value = new Vector2(1.25f, 0.75f);
                serializedPresentation.FindProperty("squashDuration").floatValue = 0.12f;
                serializedPresentation.FindProperty("squashReturnSpeed").floatValue = 14f;
                serializedPresentation.FindProperty("animationFramesPerSecond").floatValue = 8f;
                serializedPresentation.FindProperty("thrustFramesPerSecond").floatValue = 8f;
                serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            }

            Transform firePoint = playerObject.transform.Find("Fire Point");
            PlayerWeaponPresentation weaponPresentation = playerObject.GetComponent<PlayerWeaponPresentation>();
            if (weaponPresentation && firePoint)
            {
                SerializedObject serializedWeaponPresentation = new SerializedObject(weaponPresentation);
                serializedWeaponPresentation.FindProperty("firePoint").objectReferenceValue = firePoint;
                serializedWeaponPresentation.FindProperty("flashDuration").floatValue = 0.08f;
                serializedWeaponPresentation.FindProperty("flashLength").floatValue = 0.34f;
                serializedWeaponPresentation.FindProperty("flashWidth").floatValue = 0.11f;
                serializedWeaponPresentation.FindProperty("sideFlashLength").floatValue = 0.22f;
                serializedWeaponPresentation.FindProperty("sideFlashWidth").floatValue = 0.12f;
                serializedWeaponPresentation.FindProperty("sortingOrder").intValue = 34;
                serializedWeaponPresentation.FindProperty("cameraShakeStrength").floatValue = 0.025f;
                serializedWeaponPresentation.FindProperty("cameraShakeDuration").floatValue = 0.05f;
                serializedWeaponPresentation.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static bool RepairEnemyPrefabProfile(string prefabPath, bool ranged)
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (!prefabContents)
            {
                Debug.LogError($"Could not open enemy prefab at {prefabPath}.");
                return false;
            }

            bool changed = false;
            EnemyController controller = prefabContents.GetComponent<EnemyController>();
            if (controller)
            {
                SerializedObject serializedController = new SerializedObject(controller);
                changed |= SetFloat(
                    serializedController,
                    "wakeSignalDistanceMultiplier",
                    Ee5SliceProfile.EnemyWakeSignalDistanceMultiplier);
                changed |= SetFloat(serializedController, "wakeSignalChargeDuration", 1.15f);
                changed |= SetFloat(serializedController, "wakeSignalChargeDecay", 1.8f);
                changed |= SetFloat(serializedController, "wakeFinalWarningDuration", 0.35f);
                changed |= SetBool(serializedController, "forwardIsLocalNegativeX", ranged);
                serializedController.ApplyModifiedPropertiesWithoutUndo();
            }

            EnemySpritePresentation presentation = prefabContents.GetComponent<EnemySpritePresentation>();
            if (presentation)
            {
                SerializedObject serializedPresentation = new SerializedObject(presentation);
                changed |= SetBool(serializedPresentation, "faceDormantTowardTarget", ranged);
                changed |= SetBool(serializedPresentation, "forwardIsLocalNegativeX", true);
                changed |= SetBool(serializedPresentation, "restoreFacingAfterWake", true);
                serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabContents);
            return changed;
        }

        static void ValidateEnemyPrefabProfile(string prefabPath, bool ranged, List<string> issues)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                issues.Add($"missing {prefabPath}");
                return;
            }

            EnemyController controller = prefab.GetComponent<EnemyController>();
            if (!controller)
            {
                issues.Add($"{prefabPath} has no EnemyController");
            }
            else
            {
                SerializedObject serializedController = new SerializedObject(controller);
                float multiplier = serializedController.FindProperty("wakeSignalDistanceMultiplier").floatValue;
                if (!Mathf.Approximately(multiplier, Ee5SliceProfile.EnemyWakeSignalDistanceMultiplier))
                    issues.Add($"{prefabPath} wakeSignalDistanceMultiplier={multiplier}");
                bool controllerForwardNegativeX = serializedController.FindProperty("forwardIsLocalNegativeX").boolValue;
                if (controllerForwardNegativeX != ranged)
                    issues.Add($"{prefabPath} forwardIsLocalNegativeX={controllerForwardNegativeX}");
            }

            EnemySpritePresentation presentation = prefab.GetComponent<EnemySpritePresentation>();
            if (!presentation)
            {
                issues.Add($"{prefabPath} has no EnemySpritePresentation");
            }
            else
            {
                SerializedObject serializedPresentation = new SerializedObject(presentation);
                bool facesTarget = serializedPresentation.FindProperty("faceDormantTowardTarget").boolValue;
                bool forwardNegativeX = serializedPresentation.FindProperty("forwardIsLocalNegativeX").boolValue;
                bool restoresFacing = serializedPresentation.FindProperty("restoreFacingAfterWake").boolValue;
                if (facesTarget != ranged)
                    issues.Add($"{prefabPath} faceDormantTowardTarget={facesTarget}");
                if (!forwardNegativeX || !restoresFacing)
                    issues.Add($"{prefabPath} dormant facing basis is incomplete");
            }
        }

        static bool SetFloat(SerializedObject serialized, string propertyName, float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || Mathf.Approximately(property.floatValue, value))
                return false;

            property.floatValue = value;
            return true;
        }

        static bool SetBool(SerializedObject serialized, string propertyName, bool value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.boolValue == value)
                return false;

            property.boolValue = value;
            return true;
        }

        static List<string> GetGeneratedSceneContractIssues()
        {
            List<string> issues = new List<string>();
            GameObject player = GameObject.Find("Player Craft");
            if (!player)
                issues.Add("Player Craft");
            else
            {
                Sprite expectedSprite = LoadFirstSprite(ResolveAssetPath(
                    PlayerCraftSpriteAssetPath,
                    LegacyPlayerCraftSpriteAssetPath));
                issues.AddRange(GetPlayerCraftSpriteIssues(
                    player,
                    expectedSprite,
                    "Player Craft"));
            }
            if (!UnityEngine.Object.FindFirstObjectByType<GameStateMachine>())
                issues.Add("Game State");
            if (!UnityEngine.Object.FindFirstObjectByType<EncounterController>())
                issues.Add("EncounterController");
            if (!UnityEngine.Object.FindFirstObjectByType<SliceObjectiveDirector>())
                issues.Add("SliceObjectiveDirector");
            EnergyKey key = UnityEngine.Object.FindFirstObjectByType<EnergyKey>();
            if (!key)
                issues.Add("EnergyKey");
            else if (!key.EnemyTarget || !key.EnemyTarget.GetComponent<EnemyWeapon>())
                issues.Add("EnergyKey carrier (ranged gunner)");
            if (!UnityEngine.Object.FindFirstObjectByType<EnergyGate>())
                issues.Add("EnergyGate");
            if (!UnityEngine.Object.FindFirstObjectByType<LevelExit>())
                issues.Add("LevelExit");
            if (!UnityEngine.Object.FindFirstObjectByType<GameplayHud>())
                issues.Add("GameplayHud");

            EnemyController[] enemies = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            if (enemies == null || enemies.Length < 2)
                issues.Add($"Enemy roster ({enemies?.Length ?? 0}/2)");
            return issues;
        }

        static List<string> GetPlayerCraftSpriteIssues(
            GameObject player,
            Sprite expectedSprite,
            string label)
        {
            List<string> issues = new List<string>();
            if (!player)
            {
                issues.Add($"{label}: missing Player Craft");
                return issues;
            }

            if (!expectedSprite)
            {
                issues.Add($"{label}: verified player sprite is missing");
                return issues;
            }

            Transform visual = player.transform.Find("Craft Visual");
            SpriteRenderer renderer = visual ? visual.GetComponent<SpriteRenderer>() : null;
            if (!renderer)
                issues.Add($"{label}: Craft Visual SpriteRenderer is missing");
            else if (renderer.sprite != expectedSprite)
            {
                string actualPath = renderer.sprite
                    ? AssetDatabase.GetAssetPath(renderer.sprite)
                    : "<missing>";
                issues.Add($"{label}: renderer uses {actualPath}");
            }

            PlayerFlightPresentation presentation = player.GetComponent<PlayerFlightPresentation>();
            if (!presentation)
            {
                issues.Add($"{label}: PlayerFlightPresentation is missing");
                return issues;
            }

            SerializedObject serializedPresentation = new SerializedObject(presentation);
            CheckSpriteArray(
                serializedPresentation.FindProperty("flightFrames"),
                expectedSprite,
                $"{label}: flightFrames",
                issues);
            CheckSpriteArray(
                serializedPresentation.FindProperty("thrustFrames"),
                expectedSprite,
                $"{label}: thrustFrames",
                issues);
            return issues;
        }

        static void CheckSpriteArray(
            SerializedProperty array,
            Sprite expectedSprite,
            string label,
            List<string> issues)
        {
            if (array == null || !array.isArray || array.arraySize == 0)
            {
                issues.Add($"{label} is empty");
                return;
            }

            for (int i = 0; i < array.arraySize; i++)
            {
                Sprite sprite = array.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite == expectedSprite)
                    continue;

                string actualPath = sprite
                    ? AssetDatabase.GetAssetPath(sprite)
                    : "<missing>";
                issues.Add($"{label}[{i}] uses {actualPath}");
            }
        }

        static bool RepairPlayerCraftRoot(GameObject root, Sprite[] craftSprites)
        {
            if (!root)
                return false;

            Transform visual = root.transform.Find("Craft Visual");
            SpriteRenderer renderer = visual ? visual.GetComponent<SpriteRenderer>() : null;
            PlayerFlightPresentation presentation = root.GetComponent<PlayerFlightPresentation>();
            if (!visual || !renderer || !presentation)
            {
                Debug.LogWarning(
                    $"Skipped player craft sprite repair for {root.name}; Craft Visual, SpriteRenderer, or PlayerFlightPresentation is missing.",
                    root);
                return false;
            }

            renderer.sprite = craftSprites[0];
            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("visual").objectReferenceValue = visual;
            serializedPresentation.FindProperty("visualRenderer").objectReferenceValue = renderer;
            SetSpriteArray(serializedPresentation, "flightFrames", craftSprites);
            SetSpriteArray(serializedPresentation, "thrustFrames", craftSprites);
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(presentation);
            return true;
        }

        static int RenameImportedAsset(string legacyPath, string semanticPath)
        {
            bool hasSemanticAsset = AssetDatabase.LoadMainAssetAtPath(semanticPath);
            bool hasLegacyAsset = AssetDatabase.LoadMainAssetAtPath(legacyPath);

            if (hasSemanticAsset && hasLegacyAsset)
            {
                Debug.LogWarning(
                    $"Skipped sprite rename because both paths exist; resolve the duplicate deliberately: {legacyPath} / {semanticPath}");
                return 0;
            }

            if (hasSemanticAsset)
                return 0;

            if (!hasLegacyAsset)
            {
                Debug.LogWarning($"Skipped sprite rename; source asset was not found: {legacyPath}");
                return 0;
            }

            string newName = Path.GetFileNameWithoutExtension(semanticPath);
            string error = AssetDatabase.RenameAsset(legacyPath, newName);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"Could not rename {legacyPath}: {error}");
                return 0;
            }

            return 1;
        }

        static Transform[] CreateBackdrop()
        {
            GameObject backdropRoot = new GameObject("Starfield Backdrop");
            backdropRoot.transform.position = new Vector3(0f, 0f, 4f);

            return new[]
            {
                CreateBackdropLayer(
                    backdropRoot.transform,
                    "Far Stars",
                    Vector3.one * 4f,
                    new Color(0.32f, 0.4f, 0.7f, 0.35f),
                    -120),
                CreateBackdropLayer(
                    backdropRoot.transform,
                    "Mid Stars",
                    Vector3.one * 4.7f,
                    new Color(0.45f, 0.55f, 0.9f, 0.42f),
                    -110),
                CreateBackdropLayer(
                    backdropRoot.transform,
                    "Near Stars",
                    Vector3.one * 5.4f,
                    new Color(0.65f, 0.75f, 1f, 0.28f),
                    -100)
            };
        }

        static Transform CreateBackdropLayer(
            Transform parent,
            string objectName,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            GameObject layer = new GameObject(objectName);
            layer.transform.SetParent(parent, false);
            layer.transform.localScale = scale;

            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadFirstSprite(StarfieldSpritePath, LegacyStarfieldSpritePath);
            renderer.sortingOrder = sortingOrder;
            renderer.color = color;
            return layer.transform;
        }

        static GameStateMachine CreateGameStateMachine(InputActionAsset inputAsset)
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
            return stateMachine;
        }

        static PlayerCharacter CreatePlayer(
            InputActionAsset inputAsset,
            PlayerProjectile projectilePrefab,
            GameStateMachine gameState)
        {
            GameObject player = new GameObject("Player Craft");
            player.tag = "Player";
            player.transform.position = Vector3.zero;

            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.mass = Ee5SliceProfile.PlayerMass;
            body.gravityScale = Ee5SliceProfile.PlayerGravityScale;
            body.linearDamping = Ee5SliceProfile.PlayerLinearDamping;
            body.angularDamping = Ee5SliceProfile.PlayerAngularDamping;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D playerCollider = player.AddComponent<CircleCollider2D>();
            playerCollider.radius = 0.112f;
            playerCollider.offset = new Vector2(-0.004f, 0f);
            PlayerCharacter character = player.AddComponent<PlayerCharacter>();
            PlayerFlightMotor motor = player.GetComponent<PlayerFlightMotor>();
            SerializedObject serializedMotor = new SerializedObject(motor);
            serializedMotor.FindProperty("enforceEe5Profile").boolValue = true;
            serializedMotor.FindProperty("thrustForce").floatValue = Ee5SliceProfile.ThrustForce;
            serializedMotor.FindProperty("rotationTorque").floatValue = Ee5SliceProfile.RotationTorque;
            serializedMotor.FindProperty("rotationAddsThrust").boolValue = true;
            serializedMotor.FindProperty("rotationBoostMultiplier").floatValue = Ee5SliceProfile.RotationBoostMultiplier;
            serializedMotor.FindProperty("stabilizationSpeed").floatValue = Ee5SliceProfile.StabilizationSpeed;
            serializedMotor.FindProperty("angularDamping").floatValue = Ee5SliceProfile.FlightAngularDamping;
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
            SerializedObject serializedInput = new SerializedObject(input);
            serializedInput.FindProperty("gameState").objectReferenceValue = gameState;
            serializedInput.ApplyModifiedPropertiesWithoutUndo();
            PlayerRespawnController recovery = player.AddComponent<PlayerRespawnController>();
            SerializedObject serializedRecovery = new SerializedObject(recovery);
            // EE5 reloads the authored room on death, resetting the encounter
            // and objective state instead of leaving a defeated room running.
            serializedRecovery.FindProperty("reloadSceneOnDeath").boolValue = true;
            serializedRecovery.FindProperty("reloadDelay").floatValue = 0f;
            serializedRecovery.FindProperty("respawnAutomatically").boolValue = false;
            serializedRecovery.ApplyModifiedPropertiesWithoutUndo();

            PlayerWeaponInput weaponInput = player.AddComponent<PlayerWeaponInput>();
            if (inputAsset)
                weaponInput.ConfigureInputAsset(inputAsset);
            SerializedObject serializedWeaponInput = new SerializedObject(weaponInput);
            serializedWeaponInput.FindProperty("gameState").objectReferenceValue = gameState;
            serializedWeaponInput.ApplyModifiedPropertiesWithoutUndo();

            PlayerWeapon weapon = player.AddComponent<PlayerWeapon>();
            SerializedObject serializedWeapon = new SerializedObject(weapon);
            serializedWeapon.FindProperty("gameState").objectReferenceValue = gameState;
            serializedWeapon.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
            serializedWeapon.FindProperty("enforceEe5Profile").boolValue = true;
            serializedWeapon.FindProperty("keepFirePointRightOfOrigin").boolValue = true;
            serializedWeapon.FindProperty("firePointLocalOffset").vector2Value = new Vector2(0.55f, 0f);
            // Match EE5's authored sniper prefab rather than the faster
            // prototype tuning: each shot should be a meaningful flight beat.
            serializedWeapon.FindProperty("fireCooldown").floatValue = Ee5SliceProfile.PlayerFireCooldown;
            serializedWeapon.FindProperty("recoilForce").floatValue = Ee5SliceProfile.PlayerRecoilForce;
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
            serializedPresentation.FindProperty("boostedExhaustLengthMultiplier").floatValue = 1.25f;
            serializedPresentation.FindProperty("boostedExhaustWidthMultiplier").floatValue = 1.15f;
            serializedPresentation.FindProperty("boostedParticleEmissionMultiplier").floatValue = 1.4f;
            serializedPresentation.FindProperty("boostedExhaustStartColor").colorValue =
                new Color(0.75f, 1f, 1f, 1f);
            serializedPresentation.FindProperty("boostedExhaustEndColor").colorValue =
                new Color(0.12f, 0.4f, 1f, 0f);
            serializedPresentation.FindProperty("exhaustSideOffset").floatValue = 0.28f;
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

            PlayerWeaponPresentation weaponPresentation = player.AddComponent<PlayerWeaponPresentation>();
            SerializedObject serializedWeaponPresentation = new SerializedObject(weaponPresentation);
            serializedWeaponPresentation.FindProperty("firePoint").objectReferenceValue = firePoint.transform;
            serializedWeaponPresentation.FindProperty("flashDuration").floatValue = 0.08f;
            serializedWeaponPresentation.FindProperty("flashLength").floatValue = 0.34f;
            serializedWeaponPresentation.FindProperty("flashWidth").floatValue = 0.11f;
            serializedWeaponPresentation.FindProperty("sideFlashLength").floatValue = 0.22f;
            serializedWeaponPresentation.FindProperty("sideFlashWidth").floatValue = 0.12f;
            serializedWeaponPresentation.FindProperty("flashColor").colorValue =
                new Color(1f, 0.95f, 0.42f, 1f);
            serializedWeaponPresentation.FindProperty("flashEdgeColor").colorValue =
                new Color(1f, 0.16f, 0.04f, 0f);
            serializedWeaponPresentation.FindProperty("sortingOrder").intValue = 34;
            serializedWeaponPresentation.FindProperty("cameraShakeStrength").floatValue = 0.025f;
            serializedWeaponPresentation.FindProperty("cameraShakeDuration").floatValue = 0.05f;
            serializedWeaponPresentation.ApplyModifiedPropertiesWithoutUndo();

            CreateCraftVisual(player.transform);
            Transform craftVisual = player.transform.Find("Craft Visual");
            serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("visual").objectReferenceValue = craftVisual;
            serializedPresentation.FindProperty("visualRenderer").objectReferenceValue =
                craftVisual ? craftVisual.GetComponent<SpriteRenderer>() : null;
            SetSpriteArray(
                serializedPresentation,
                "flightFrames",
                LoadSprites(
                    PlayerCraftSpriteAssetPath,
                    LegacyPlayerCraftSpriteAssetPath));
            SetSpriteArray(
                serializedPresentation,
                "thrustFrames",
                LoadSprites(
                    PlayerCraftSpriteAssetPath,
                    LegacyPlayerCraftSpriteAssetPath));
            // The player uses the verified orange craft sprite. A single-frame
            // presentation is preferable to borrowing a boss animation strip;
            // thrust remains readable through the authored exhaust response.
            serializedPresentation.FindProperty("animationFramesPerSecond").floatValue = 8f;
            serializedPresentation.FindProperty("thrustFramesPerSecond").floatValue = 8f;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            PlayerDamageFeedback damageFeedback = player.AddComponent<PlayerDamageFeedback>();
            SerializedObject serializedDamageFeedback = new SerializedObject(damageFeedback);
            // Match EE5's alternating red/yellow damage readout instead of a
            // single opaque flash that disappears before the player can react.
            serializedDamageFeedback.FindProperty("flashColor").colorValue = new Color(1f, 0.01f, 0.01f, 1f);
            serializedDamageFeedback.FindProperty("alternateFlashColor").colorValue = new Color(1f, 0.92f, 0.01f, 1f);
            serializedDamageFeedback.FindProperty("flashDuration").floatValue = 0.4f;
            serializedDamageFeedback.FindProperty("flashInterval").floatValue = 1f / 30f;
            serializedDamageFeedback.ApplyModifiedPropertiesWithoutUndo();
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
            Sprite[] sprites = LoadSprites(
                PlayerHealthSpriteAssetPath,
                LegacyPlayerHealthSpriteAssetPath);

            PlayerHealthDisplay display = character.gameObject.AddComponent<PlayerHealthDisplay>();
            SerializedObject serialized = new SerializedObject(display);
            serialized.FindProperty("displayRenderer").objectReferenceValue = renderer;
            SerializedProperty spriteProperty = serialized.FindProperty("healthSprites");
            spriteProperty.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                spriteProperty.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            serialized.FindProperty("showOnStartDuration").floatValue = 0.8f;
            serialized.FindProperty("showOnHitDuration").floatValue = 0.6f;
            serialized.FindProperty("fadeSpeed").floatValue = 6f;
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
            serializedProjectile.FindProperty("enforceEe5Profile").boolValue = true;
            // EE5's linked player bullet travels at 30 units per second.
            serializedProjectile.FindProperty("speed").floatValue = Ee5SliceProfile.PlayerProjectileSpeed;
            serializedProjectile.FindProperty("lifetime").floatValue = 2f;
            serializedProjectile.FindProperty("damage").floatValue = 1f;
            serializedProjectile.FindProperty("knockback").floatValue = 0f;
            serializedProjectile.FindProperty("destroyOnUnrecognizedCollision").boolValue = true;
            serializedProjectile.FindProperty("maxTrailPoints").intValue = 6;
            serializedProjectile.FindProperty("pointSpacing").floatValue = 0.03f;
            serializedProjectile.FindProperty("useImpactFade").boolValue = true;
            serializedProjectile.FindProperty("impactFadeTime").floatValue = 0.08f;
            serializedProjectile.FindProperty("trailStartColor").colorValue = Color.white;
            serializedProjectile.FindProperty("trailEndColor").colorValue =
                new Color(1f, 0.1f, 0.04f, 1f);
            serializedProjectile.FindProperty("nearMissDistance").floatValue = 1.35f;
            serializedProjectile.ApplyModifiedPropertiesWithoutUndo();

            ProjectileSpriteTrailPresentation spriteTrail =
                projectile.GetComponent<ProjectileSpriteTrailPresentation>();
            if (!spriteTrail)
                spriteTrail = projectile.AddComponent<ProjectileSpriteTrailPresentation>();
            SerializedObject serializedSpriteTrail = new SerializedObject(spriteTrail);
            serializedSpriteTrail.FindProperty("enemyOnly").boolValue = true;
            serializedSpriteTrail.FindProperty("maxGhosts").intValue = 7;
            serializedSpriteTrail.FindProperty("spawnInterval").floatValue = 0.025f;
            serializedSpriteTrail.FindProperty("ghostLifetime").floatValue = 0.18f;
            serializedSpriteTrail.FindProperty("startAlpha").floatValue = 0.45f;
            serializedSpriteTrail.FindProperty("endScale").floatValue = 0.72f;
            serializedSpriteTrail.FindProperty("finalGhostAlphaBonus").floatValue = 0.2f;
            serializedSpriteTrail.ApplyModifiedPropertiesWithoutUndo();

            SpriteRenderer sprite = projectile.GetComponent<SpriteRenderer>();
            if (!sprite)
                sprite = projectile.AddComponent<SpriteRenderer>();
            sprite.sprite = LoadFirstSprite(
                PlayerProjectileSpriteAssetPath,
                LegacyPlayerProjectileSpriteAssetPath);
            sprite.sortingOrder = 21;

            LineRenderer line = projectile.GetComponent<LineRenderer>();
            if (!line)
                line = projectile.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.startWidth = 0.01f;
            line.endWidth = 0.1f;
            line.numCapVertices = 8;
            line.numCornerVertices = 4;
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
            GameStateMachine gameState,
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
            serializedController.FindProperty("requireLineOfSightToWake").boolValue = true;
            // The line is visible before activation, matching EE5's four-times
            // wake-line envelope; the state machine still wakes only at 6 units.
            serializedController.FindProperty("wakeSignalDistanceMultiplier").floatValue =
                Ee5SliceProfile.EnemyWakeSignalDistanceMultiplier;
            serializedController.FindProperty("wakeSignalChargeDuration").floatValue = 1.15f;
            serializedController.FindProperty("wakeSignalChargeDecay").floatValue = 1.8f;
            serializedController.FindProperty("wakeFinalWarningDuration").floatValue = 0.35f;
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
            serializedController.FindProperty("nearMissDistance").floatValue = 1.65f;
            serializedController.FindProperty("nearMissExitDistance").floatValue = 2.15f;
            serializedController.FindProperty("faceTurnSpeed").floatValue = ranged ? 7.4f : 7.6f;
            serializedController.FindProperty("forwardIsLocalNegativeX").boolValue = ranged;
            serializedController.FindProperty("keepSpriteUpright").boolValue = true;
            serializedController.FindProperty("gameState").objectReferenceValue = gameState;
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
                serializedWeapon.FindProperty("gameState").objectReferenceValue = gameState;
                serializedWeapon.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
                serializedWeapon.FindProperty("firePoint").objectReferenceValue = firePoint.transform;
                serializedWeapon.FindProperty("attackRange").floatValue = 7f;
                serializedWeapon.FindProperty("fireCooldown").floatValue = 0.5f;
                serializedWeapon.FindProperty("projectileSpeed").floatValue = 9.5f;
                serializedWeapon.FindProperty("projectileKnockback").floatValue = 2.5f;
                serializedWeapon.FindProperty("projectileTint").colorValue = new Color(0.05f, 1f, 0.16f, 1f);
                serializedWeapon.FindProperty("drawAimTelegraph").boolValue = true;
                serializedWeapon.FindProperty("telegraphDuration").floatValue = 0.18f;
                serializedWeapon.FindProperty("telegraphMinWidth").floatValue = 0.018f;
                serializedWeapon.FindProperty("telegraphMaxWidth").floatValue = 0.085f;
                serializedWeapon.FindProperty("telegraphColor").colorValue =
                    new Color(0.1f, 1f, 0.3f, 0.5f);
                serializedWeapon.FindProperty("telegraphSortingOrder").intValue = 75;
                serializedWeapon.ApplyModifiedPropertiesWithoutUndo();

                EnemyWeaponPresentation weaponPresentation = enemy.AddComponent<EnemyWeaponPresentation>();
                SerializedObject serializedWeaponPresentation = new SerializedObject(weaponPresentation);
                serializedWeaponPresentation.FindProperty("firePoint").objectReferenceValue = firePoint.transform;
                serializedWeaponPresentation.FindProperty("flashDuration").floatValue = 0.09f;
                serializedWeaponPresentation.FindProperty("flashLength").floatValue = 0.28f;
                serializedWeaponPresentation.FindProperty("flashWidth").floatValue = 0.095f;
                serializedWeaponPresentation.FindProperty("sideFlashLength").floatValue = 0.18f;
                serializedWeaponPresentation.FindProperty("sideFlashWidth").floatValue = 0.1f;
                serializedWeaponPresentation.FindProperty("flashColor").colorValue =
                    new Color(0.45f, 1f, 0.55f, 1f);
                serializedWeaponPresentation.FindProperty("flashEdgeColor").colorValue =
                    new Color(0.05f, 1f, 0.16f, 0f);
                serializedWeaponPresentation.FindProperty("sortingOrder").intValue = 76;
                serializedWeaponPresentation.FindProperty("cameraShakeStrength").floatValue = 0.014f;
                serializedWeaponPresentation.FindProperty("cameraShakeDuration").floatValue = 0.04f;
                serializedWeaponPresentation.ApplyModifiedPropertiesWithoutUndo();
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
            serializedPresentation.FindProperty("dormantFramesPerSecond").floatValue = 8f;
            serializedPresentation.FindProperty("wakeFramesPerSecond").floatValue = 14f;
            serializedPresentation.FindProperty("defeatDisplayDuration").floatValue = 0.3f;
            serializedPresentation.FindProperty("pingPongDormantAnimation").boolValue = ranged;
            serializedPresentation.FindProperty("faceDormantTowardTarget").boolValue = ranged;
            serializedPresentation.FindProperty("forwardIsLocalNegativeX").boolValue = true;
            serializedPresentation.FindProperty("restoreFacingAfterWake").boolValue = true;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            CreateEnemyHealthDisplay(enemy.transform, ranged);

            EnemyWakePresentation wakePresentation = enemy.AddComponent<EnemyWakePresentation>();
            SerializedObject serializedWake = new SerializedObject(wakePresentation);
            serializedWake.FindProperty("blockedColor").colorValue = new Color(0.5f, 0.14f, 1f, 0.18f);
            serializedWake.FindProperty("readyColor").colorValue = new Color(0.08f, 1f, 0.34f, 0.9f);
            serializedWake.FindProperty("flashYellow").colorValue = new Color(1f, 0.9f, 0.05f, 0.95f);
            serializedWake.FindProperty("flashRed").colorValue = new Color(1f, 0.04f, 0.02f, 0.95f);
            serializedWake.FindProperty("minWidth").floatValue = 0.014f;
            serializedWake.FindProperty("maxWidth").floatValue = 0.11f;
            serializedWake.FindProperty("endpointSmoothTime").floatValue = 0.055f;
            serializedWake.FindProperty("glanceRadius").floatValue = 0.56f;
            serializedWake.FindProperty("glanceSpeed").floatValue = 15f;
            serializedWake.FindProperty("enemyEndAlphaMultiplier").floatValue = 0.5f;
            serializedWake.FindProperty("playerEndAlphaMultiplier").floatValue = 0f;
            serializedWake.FindProperty("endWidthMultiplier").floatValue = 0.08f;
            serializedWake.FindProperty("sortingOrder").intValue = 80;
            serializedWake.ApplyModifiedPropertiesWithoutUndo();

            EnemyDeathPresentation deathPresentation = enemy.AddComponent<EnemyDeathPresentation>();
            SerializedObject serializedDeath = new SerializedObject(deathPresentation);
            SetSpriteArray(
                serializedDeath,
                "burstFrames",
                LoadSprites(EnemyBurstSpritePath, LegacyEnemyBurstSpritePath));
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
            Sprite[] sprites = LoadSprites(
                PlayerHealthSpriteAssetPath,
                LegacyPlayerHealthSpriteAssetPath);

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

        static SliceObjectiveDirector CreateEncounterAndExit(
            GameStateMachine gameState,
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
            gate.tag = "Wall";
            gate.transform.position = new Vector3(5f, 0f, 0f);
            BoxCollider2D gateCollider = gate.AddComponent<BoxCollider2D>();
            gateCollider.size = new Vector2(0.35f, 3.8f);
            EnergyGate energyGate = gate.AddComponent<EnergyGate>();
            GameObject keyTarget = new GameObject("Key Target");
            keyTarget.transform.SetParent(gate.transform, false);
            keyTarget.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            SerializedObject serializedGate = new SerializedObject(energyGate);
            serializedGate.FindProperty("liftDistance").floatValue = 12f;
            serializedGate.FindProperty("liftSpeed").floatValue = 6f;
            serializedGate.FindProperty("keyTarget").objectReferenceValue = keyTarget.transform;
            serializedGate.ApplyModifiedPropertiesWithoutUndo();
            EnergyGatePresentation gatePresentation = gate.AddComponent<EnergyGatePresentation>();
            SerializedObject serializedGatePresentation = new SerializedObject(gatePresentation);
            serializedGatePresentation.FindProperty("unlockColor").colorValue =
                new Color(0.2f, 1f, 0.85f, 1f);
            serializedGatePresentation.FindProperty("burstScale").floatValue = 1.35f;
            serializedGatePresentation.FindProperty("cameraShakeStrength").floatValue = 0.08f;
            serializedGatePresentation.FindProperty("cameraShakeDuration").floatValue = 0.24f;
            serializedGatePresentation.FindProperty("unlockPulseDuration").floatValue = 1.8f;
            serializedGatePresentation.FindProperty("unlockPulseWidthMultiplier").floatValue = 1.55f;
            serializedGatePresentation.FindProperty("unlockPulseSpeed").floatValue = 18f;
            serializedGatePresentation.FindProperty("approachColor").colorValue =
                new Color(1f, 0.8f, 0.18f, 1f);
            serializedGatePresentation.FindProperty("approachPulseDuration").floatValue = 0.85f;
            serializedGatePresentation.FindProperty("approachPulseWidthMultiplier").floatValue = 1.25f;
            serializedGatePresentation.FindProperty("approachPulseSpeed").floatValue = 24f;
            serializedGatePresentation.ApplyModifiedPropertiesWithoutUndo();
            CreateSquareOutline(gate.transform, new Vector2(0.35f, 3.8f), new Color(0.2f, 0.55f, 1f));
            CreateGateVisual(gate.transform);

            GameObject key = new GameObject("Energy Key");
            key.transform.position = new Vector3(2.5f, 3.5f, 0f);
            CircleCollider2D keyCollider = key.AddComponent<CircleCollider2D>();
            keyCollider.isTrigger = true;
            EnergyKey energyKey = key.AddComponent<EnergyKey>();
            SerializedObject serializedKey = new SerializedObject(energyKey);
            serializedKey.FindProperty("requiredEncounter").objectReferenceValue = encounter;
            // EE5's key-lock beat is carried by the ranged gunner. The melee
            // hunter remains active pressure while the player chases the key,
            // which is the authored reason this objective does not require the
            // entire encounter to be cleared before it can progress.
            serializedKey.FindProperty("enemyTarget").objectReferenceValue = gunnerEnemy;
            serializedKey.FindProperty("targetGate").objectReferenceValue = energyGate;
            serializedKey.FindProperty("enemyOrbitRadius").floatValue = 1f;
            serializedKey.FindProperty("enemyOrbitSpeed").floatValue = 4f;
            serializedKey.FindProperty("enemyOrbitSharpness").floatValue = 8f;
            serializedKey.FindProperty("gateUnlockRange").floatValue = 2.25f;
            serializedKey.FindProperty("collectDistance").floatValue = 0.85f;
            serializedKey.FindProperty("playerFollowSharpness").floatValue = 14f;
            serializedKey.FindProperty("releasePulseDuration").floatValue = Ee5SliceProfile.KeyReleasePulseDuration;
            serializedKey.FindProperty("releasePulseScale").floatValue = Ee5SliceProfile.KeyReleasePulseScale;
            serializedKey.ApplyModifiedPropertiesWithoutUndo();
            EnergyKeyPresentation keyPresentation = key.AddComponent<EnergyKeyPresentation>();
            SerializedObject serializedKeyPresentation = new SerializedObject(keyPresentation);
            serializedKeyPresentation.FindProperty("availableColor").colorValue =
                new Color(1f, 0.85f, 0.15f, 1f);
            serializedKeyPresentation.FindProperty("tetherColor").colorValue =
                new Color(1f, 0.92f, 0.3f, 0.7f);
            serializedKeyPresentation.FindProperty("tetherWidth").floatValue = 0.035f;
            serializedKeyPresentation.FindProperty("tetherPulseSpeed").floatValue = 7f;
            serializedKeyPresentation.FindProperty("tetherMinAlpha").floatValue = 0.2f;
            serializedKeyPresentation.FindProperty("tetherMaxAlpha").floatValue = 0.72f;
            serializedKeyPresentation.ApplyModifiedPropertiesWithoutUndo();
            SpriteRenderer keySprite = key.AddComponent<SpriteRenderer>();
            keySprite.sprite = LoadFirstSprite(
                EnergyKeySpriteAssetPath,
                LegacyEnergyKeySpriteAssetPath);
            keySprite.sortingOrder = 10;
            key.transform.localScale = Vector3.one * 0.7f;
            CreateSquareOutline(key.transform, Vector2.one * 0.5f, new Color(1f, 0.8f, 0.1f));

            GameObject exit = new GameObject("Level Exit");
            exit.transform.position = new Vector3(6.8f, 0f, 0f);
            CircleCollider2D collider = exit.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 1.45f;
            LevelExit levelExit = exit.AddComponent<LevelExit>();
            SerializedObject serializedExit = new SerializedObject(levelExit);
            serializedExit.FindProperty("requiredGate").objectReferenceValue = energyGate;
            serializedExit.FindProperty("gameState").objectReferenceValue = gameState;
            serializedExit.ApplyModifiedPropertiesWithoutUndo();
            ExtractionPortalPresentation portal = exit.AddComponent<ExtractionPortalPresentation>();
            SerializedObject serializedPortal = new SerializedObject(portal);
            serializedPortal.FindProperty("portalDiameter").floatValue = 3.8f;
            serializedPortal.FindProperty("ringSegments").intValue = 80;
            serializedPortal.ApplyModifiedPropertiesWithoutUndo();
            CreateSquareOutline(exit.transform, Vector2.one * 0.9f, new Color(0.2f, 1f, 0.85f));

            GameObject objectiveObject = new GameObject("Slice Objective Flow");
            SliceObjectiveDirector objectiveDirector = objectiveObject.AddComponent<SliceObjectiveDirector>();
            SerializedObject serializedObjective = new SerializedObject(objectiveDirector);
            serializedObjective.FindProperty("encounter").objectReferenceValue = encounter;
            serializedObjective.FindProperty("energyKey").objectReferenceValue = energyKey;
            serializedObjective.FindProperty("gate").objectReferenceValue = energyGate;
            serializedObjective.FindProperty("exit").objectReferenceValue = levelExit;
            serializedObjective.FindProperty("gameState").objectReferenceValue = gameState;
            serializedObjective.ApplyModifiedPropertiesWithoutUndo();
            return objectiveDirector;
        }

        static void CreateHud(
            SliceObjectiveDirector objectiveDirector,
            GameStateMachine gameState)
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

            GameObject healthObject = new GameObject("Health Label");
            healthObject.transform.SetParent(canvasObject.transform, false);
            RectTransform healthRect = healthObject.AddComponent<RectTransform>();
            healthRect.anchorMin = new Vector2(1f, 1f);
            healthRect.anchorMax = new Vector2(1f, 1f);
            healthRect.pivot = new Vector2(1f, 1f);
            healthRect.anchoredPosition = new Vector2(-24f, -24f);
            healthRect.sizeDelta = new Vector2(260f, 48f);

            Text healthLabel = healthObject.AddComponent<Text>();
            healthLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            healthLabel.fontSize = 22;
            healthLabel.fontStyle = FontStyle.Bold;
            healthLabel.alignment = TextAnchor.UpperRight;
            healthLabel.color = new Color(1f, 0.82f, 0.18f, 1f);
            healthLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            healthLabel.verticalOverflow = VerticalWrapMode.Overflow;
            Outline healthOutline = healthObject.AddComponent<Outline>();
            healthOutline.effectColor = new Color(0.02f, 0.04f, 0.12f, 0.9f);
            healthOutline.effectDistance = new Vector2(2f, -2f);

            GameObject actionCalloutObject = new GameObject("Action Callout");
            actionCalloutObject.transform.SetParent(canvasObject.transform, false);
            RectTransform actionCalloutRect = actionCalloutObject.AddComponent<RectTransform>();
            actionCalloutRect.anchorMin = new Vector2(0.5f, 1f);
            actionCalloutRect.anchorMax = new Vector2(0.5f, 1f);
            actionCalloutRect.pivot = new Vector2(0.5f, 1f);
            actionCalloutRect.anchoredPosition = new Vector2(0f, -178f);
            actionCalloutRect.sizeDelta = new Vector2(700f, 48f);

            Text actionCalloutLabel = actionCalloutObject.AddComponent<Text>();
            actionCalloutLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            actionCalloutLabel.fontSize = 20;
            actionCalloutLabel.fontStyle = FontStyle.Bold;
            actionCalloutLabel.alignment = TextAnchor.UpperCenter;
            actionCalloutLabel.color = Color.white;
            actionCalloutLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            actionCalloutLabel.verticalOverflow = VerticalWrapMode.Overflow;
            Outline actionCalloutOutline = actionCalloutObject.AddComponent<Outline>();
            actionCalloutOutline.effectColor = new Color(0.02f, 0.04f, 0.12f, 0.9f);
            actionCalloutOutline.effectDistance = new Vector2(2f, -2f);
            CanvasGroup actionCalloutGroup = actionCalloutObject.AddComponent<CanvasGroup>();
            actionCalloutGroup.alpha = 0f;
            actionCalloutGroup.interactable = false;
            actionCalloutGroup.blocksRaycasts = false;

            GameObject bannerObject = new GameObject("Objective Banner");
            bannerObject.transform.SetParent(canvasObject.transform, false);
            RectTransform bannerRect = bannerObject.AddComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.anchoredPosition = new Vector2(0f, 120f);
            bannerRect.sizeDelta = new Vector2(920f, 120f);

            Text bannerLabel = bannerObject.AddComponent<Text>();
            bannerLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            bannerLabel.fontSize = 34;
            bannerLabel.fontStyle = FontStyle.Bold;
            bannerLabel.alignment = TextAnchor.MiddleCenter;
            bannerLabel.color = new Color(0.65f, 0.95f, 1f, 1f);
            bannerLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            bannerLabel.verticalOverflow = VerticalWrapMode.Overflow;
            Outline bannerOutline = bannerObject.AddComponent<Outline>();
            bannerOutline.effectColor = new Color(0.02f, 0.04f, 0.12f, 0.9f);
            bannerOutline.effectDistance = new Vector2(2f, -2f);
            CanvasGroup bannerGroup = bannerObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.interactable = false;
            bannerGroup.blocksRaycasts = false;

            GameplayHud hud = canvasObject.AddComponent<GameplayHud>();
            SerializedObject serialized = new SerializedObject(hud);
            serialized.FindProperty("statusLabel").objectReferenceValue = label;
            serialized.FindProperty("healthLabel").objectReferenceValue = healthLabel;
            serialized.FindProperty("actionCalloutLabel").objectReferenceValue = actionCalloutLabel;
            serialized.FindProperty("actionCalloutGroup").objectReferenceValue = actionCalloutGroup;
            serialized.FindProperty("actionCalloutDuration").floatValue = 0.9f;
            serialized.FindProperty("objectiveBannerLabel").objectReferenceValue = bannerLabel;
            serialized.FindProperty("objectiveBannerGroup").objectReferenceValue = bannerGroup;
            serialized.FindProperty("objectiveBannerDuration").floatValue = 1.35f;
            serialized.FindProperty("objectiveDirector").objectReferenceValue = objectiveDirector;
            serialized.FindProperty("gameState").objectReferenceValue = gameState;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            canvasObject.AddComponent<SliceInstructionDisplay>();
        }

        static void CreateInstructionTriggers()
        {
            CreateInstructionTrigger(
                "Flight Controls Instruction",
                new Vector2(0f, 0f),
                new Vector2(4.2f, 3.4f),
                "W / UP / SPACE  THRUST\nA / D  ROTATE    S / DOWN  STABILIZE\nX  FLIP    Z / ENTER / MOUSE  FIRE",
                true);
            CreateInstructionTrigger(
                "Energy Key Instruction",
                new Vector2(2.5f, 3.5f),
                new Vector2(4.8f, 2.8f),
                "DEFEAT THE CARRIER.\nTHE ENERGY KEY WILL BREAK FREE WHEN IT IS DEFEATED.");
            CreateInstructionTrigger(
                "Energy Gate Instruction",
                new Vector2(5f, 0f),
                new Vector2(2.2f, 5f),
                "COLLECT THE ENERGY KEY,\nTHEN FLY INTO THE ENERGY GATE.");
            CreateInstructionTrigger(
                "Extraction Instruction",
                new Vector2(6.8f, 0f),
                new Vector2(3f, 4f),
                "EXTRACTION ONLINE.\nFLY INTO THE PORTAL TO COMPLETE THE SLICE.",
                true);
        }

        static void CreateInstructionTrigger(
            string objectName,
            Vector2 position,
            Vector2 size,
            string message,
            bool onlyTriggerOnce = false)
        {
            GameObject instructionObject = new GameObject(objectName);
            instructionObject.transform.position = position;
            BoxCollider2D trigger = instructionObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = size;

            SliceInstructionTrigger instruction =
                instructionObject.AddComponent<SliceInstructionTrigger>();
            SerializedObject serialized = new SerializedObject(instruction);
            serialized.FindProperty("message").stringValue = message;
            serialized.FindProperty("hideOnExit").boolValue = true;
            serialized.FindProperty("onlyTriggerOnce").boolValue = onlyTriggerOnce;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            EditorBuildSettingsScene flightTest = scenes.FirstOrDefault(scene => scene.path == ScenePath);
            if (flightTest == null)
                flightTest = new EditorBuildSettingsScene(ScenePath, true);
            else
                flightTest.enabled = true;

            // The production-facing slice must be the launch scene. Keep any
            // existing sample scenes after it for editor experiments, but do
            // not make a reviewer hunt through Build Settings to find the game.
            scenes.RemoveAll(scene => scene.path == ScenePath);
            scenes.Insert(0, flightTest);

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
            sprite.sprite = LoadFirstSprite(
                PlayerCraftSpriteAssetPath,
                LegacyPlayerCraftSpriteAssetPath);
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

        static void CreateCamera(PlayerCharacter target, Transform[] parallaxBackdrops)
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
            serializedFollow.FindProperty("enforceEe5Profile").boolValue = true;
            serializedFollow.FindProperty("followSpeed").floatValue = Ee5SliceProfile.CameraFollowSpeed;
            serializedFollow.FindProperty("velocityLead").floatValue = Ee5SliceProfile.CameraVelocityLead;
            serializedFollow.FindProperty("maxLeadDistance").floatValue = Ee5SliceProfile.CameraMaxLeadDistance;
            serializedFollow.FindProperty("facingLead").floatValue = Ee5SliceProfile.CameraFacingLead;
            serializedFollow.FindProperty("leadSmooth").floatValue = Ee5SliceProfile.CameraLeadSmooth;
            serializedFollow.FindProperty("catchupDistance").floatValue = Ee5SliceProfile.CameraCatchupDistance;
            serializedFollow.FindProperty("catchupBoost").floatValue = Ee5SliceProfile.CameraCatchupBoost;
            serializedFollow.FindProperty("hardCatchupDistance").floatValue = Ee5SliceProfile.CameraHardCatchupDistance;
            serializedFollow.FindProperty("closeEnoughSnap").floatValue = Ee5SliceProfile.CameraCloseEnoughSnap;
            // These values mirror the EE5 SimpleCameraFollow profile. Keeping
            // them explicit prevents an inspector experiment from becoming the
            // serialized gold-standard scene tuning by accident.
            serializedFollow.FindProperty("speedZoomStart").floatValue = Ee5SliceProfile.CameraSpeedZoomStart;
            serializedFollow.FindProperty("speedZoomFull").floatValue = Ee5SliceProfile.CameraSpeedZoomFull;
            serializedFollow.FindProperty("maxZoomOut").floatValue = Ee5SliceProfile.CameraMaxZoomOut;
            serializedFollow.FindProperty("zoomSmooth").floatValue = Ee5SliceProfile.CameraZoomSmooth;
            serializedFollow.FindProperty("flipZoomOut").floatValue = Ee5SliceProfile.CameraFlipZoomOut;
            serializedFollow.FindProperty("flipZoomDuration").floatValue = Ee5SliceProfile.CameraFlipZoomDuration;
            SerializedProperty parallaxLayers = serializedFollow.FindProperty("parallaxLayers");
            parallaxLayers.arraySize = parallaxBackdrops?.Length ?? 0;
            float[] parallaxStrengths =
            {
                Ee5SliceProfile.CameraFarParallaxStrength,
                Ee5SliceProfile.CameraMidParallaxStrength,
                Ee5SliceProfile.CameraNearParallaxStrength
            };
            for (int i = 0; i < parallaxLayers.arraySize; i++)
            {
                SerializedProperty layer = parallaxLayers.GetArrayElementAtIndex(i);
                layer.FindPropertyRelative("transform").objectReferenceValue = parallaxBackdrops[i];
                layer.FindPropertyRelative("strength").floatValue = parallaxStrengths[Mathf.Min(i, parallaxStrengths.Length - 1)];
            }
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

        static void CreateBrittleWall(string name, Vector2 position, Vector2 size)
        {
            GameObject wall = new GameObject(name);
            wall.tag = "Wall";
            wall.transform.position = position;

            BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
            collider.size = size;
            BrittleWall brittleWall = wall.AddComponent<BrittleWall>();
            SerializedObject serialized = new SerializedObject(brittleWall);
            serialized.FindProperty("dentSpeed").floatValue = 0.15f;
            serialized.FindProperty("breakSpeed").floatValue = 14f;
            serialized.FindProperty("minimumChipDirectness").floatValue = 0.12f;
            serialized.FindProperty("minimumDirectness").floatValue = 0.68f;
            serialized.FindProperty("requireThrustToChip").boolValue = false;
            serialized.FindProperty("requireThrustToBreak").boolValue = true;
            serialized.FindProperty("chipsBeforeBreak").intValue = 8;
            serialized.FindProperty("retainedVelocity").floatValue = 0.94f;
            serialized.FindProperty("followThroughNudge").floatValue = 0.34f;
            serialized.FindProperty("impactCooldown").floatValue = 0.32f;
            serialized.FindProperty("cameraShakeStrength").floatValue = 0.14f;
            serialized.FindProperty("cameraShakeDuration").floatValue = 0.18f;
            serialized.FindProperty("breakScore").intValue = 200;
            serialized.FindProperty("breakColor").colorValue = new Color(0.95f, 0.12f, 1f, 1f);
            serialized.FindProperty("scrapeAnimationDuration").floatValue = 0.18f;
            serialized.FindProperty("scrapeSlideDistance").floatValue = 0.075f;
            serialized.FindProperty("scrapeShakeAngle").floatValue = 4.5f;
            serialized.FindProperty("scrapePulseScale").floatValue = 0.045f;
            serialized.FindProperty("scrapeFlashColor").colorValue = new Color(1f, 0.62f, 1f, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            CreateWallVisual(wall.transform, size, new Color(0.95f, 0.12f, 1f, 0.92f));
        }

        static void CreateArenaBoundaries()
        {
            // Keep the playable rectangle in one place so movement tuning and
            // future scene variants cannot drift apart from the authored room.
            CreateWall("Left Wall", new Vector2(-8f, 0f), new Vector2(0.5f, 14f));
            CreateWall("Right Wall", new Vector2(8f, 0f), new Vector2(0.5f, 14f));
            CreateWall("Floor", new Vector2(0f, -6f), new Vector2(16f, 0.5f));
            CreateWall("Ceiling", new Vector2(0f, 6f), new Vector2(16f, 0.5f));

            // The box colliders above remain the gameplay boundary. This
            // imported EE5 SpriteShape is presentation-only, so terrain art
            // can become richer without changing movement, brittle impacts,
            // or aim-line collision rules.
            CreateMoonTerrainBasin();

            // EE5's realScene reads as a room rather than a blank box: the
            // shelves create readable flight lanes, break line of sight, and
            // give the wake telegraph and gunner pressure somewhere to matter.
            // They stay off the authored key, gate, and exit landmarks so the
            // objective route remains direct and testable.
            CreateBrittleWall(
                "Upper Crater Shelf",
                new Vector2(0.8f, 4.15f),
                new Vector2(4.2f, 0.35f));
            CreateBrittleWall(
                "Lower Crater Shelf",
                new Vector2(-0.6f, -4.15f),
                new Vector2(4.8f, 0.35f));
            CreateWall(
                "Extraction Spine",
                new Vector2(6.2f, 2.35f),
                new Vector2(0.35f, 2.5f));
        }

        static void CreateMoonTerrainBasin()
        {
            SpriteShape profile = AssetDatabase.LoadAssetAtPath<SpriteShape>(
                MoonTerrainFillProfileAssetPath);
            if (!profile)
            {
                Debug.LogWarning(
                    $"EE5 moon terrain profile not found at {MoonTerrainFillProfileAssetPath}; keeping wall-sprite fallback.");
                return;
            }

            CreateMoonTerrainPiece(
                "Playable Low Basin - SpriteShape",
                profile,
                Vector2.zero,
                new[]
                {
                    new Vector2(-8f, -5.72f),
                    new Vector2(-6.4f, -5.48f),
                    new Vector2(-4.4f, -5.62f),
                    new Vector2(-2.2f, -5.4f),
                    new Vector2(0f, -5.56f),
                    new Vector2(2.2f, -5.38f),
                    new Vector2(4.4f, -5.58f),
                    new Vector2(6.4f, -5.45f),
                    new Vector2(8f, -5.7f),
                    new Vector2(8f, -6.5f),
                    new Vector2(-8f, -6.5f)
                });

            CreateMoonTerrainPiece(
                "Upper Crater Shelf - SpriteShape",
                profile,
                new Vector2(0.8f, 4.15f),
                new[]
                {
                    new Vector2(-2.1f, -0.18f),
                    new Vector2(-1.25f, -0.24f),
                    new Vector2(0f, -0.16f),
                    new Vector2(1.3f, -0.23f),
                    new Vector2(2.1f, -0.18f),
                    new Vector2(2.1f, 0.18f),
                    new Vector2(0.7f, 0.24f),
                    new Vector2(-0.7f, 0.17f),
                    new Vector2(-2.1f, 0.2f)
                });

            CreateMoonTerrainPiece(
                "Lower Crater Shelf - SpriteShape",
                profile,
                new Vector2(-0.6f, -4.15f),
                new[]
                {
                    new Vector2(-2.4f, -0.18f),
                    new Vector2(-1.2f, -0.22f),
                    new Vector2(0f, -0.16f),
                    new Vector2(1.5f, -0.23f),
                    new Vector2(2.4f, -0.16f),
                    new Vector2(2.4f, 0.18f),
                    new Vector2(0.9f, 0.22f),
                    new Vector2(-0.7f, 0.16f),
                    new Vector2(-2.4f, 0.2f)
                });
        }

        static void CreateMoonTerrainPiece(
            string objectName,
            SpriteShape profile,
            Vector2 position,
            Vector2[] points)
        {
            GameObject terrain = new GameObject(objectName);
            terrain.tag = "Wall";
            terrain.transform.position = position;

            SpriteShapeController controller = terrain.AddComponent<SpriteShapeController>();
            controller.spriteShape = profile;
            controller.fillPixelsPerUnit = 24f;
            controller.splineDetail = 2;
            controller.worldSpaceUVs = true;
            controller.colliderDetail = 0;
            controller.autoUpdateCollider = false;
            controller.spriteShapeRenderer.sortingOrder = -20;
            controller.spriteShapeRenderer.color = new Color(0.8f, 0.86f, 1f, 0.92f);

            controller.spline.Clear();
            controller.spline.isOpenEnded = false;
            for (int i = 0; i < points.Length; i++)
            {
                controller.spline.InsertPointAt(i, points[i]);
                controller.spline.SetHeight(i, 0.35f);
                controller.spline.SetTangentMode(i, ShapeTangentMode.Continuous);
            }

            controller.RefreshSpriteShape();
        }

        static void CreateEnvironmentalPressure()
        {
            CreateContactHazard(
                "Red Heat Hazard",
                new Vector2(0f, -2.4f),
                1.15f,
                new Color(1f, 0.08f, 0.01f, 0.92f));
            CreateHealthPickup(new Vector2(-5.8f, 4.4f));
            CreateFireRatePickup(new Vector2(-5.8f, -4.4f));
        }

        static void CreateContactHazard(
            string objectName,
            Vector2 position,
            float radius,
            Color color)
        {
            GameObject hazard = new GameObject(objectName);
            hazard.transform.position = position;

            CircleCollider2D collider = hazard.AddComponent<CircleCollider2D>();
            collider.radius = radius;
            collider.isTrigger = true;

            ContactHazard contactHazard = hazard.AddComponent<ContactHazard>();
            SerializedObject serialized = new SerializedObject(contactHazard);
            serialized.FindProperty("damage").floatValue = 1f;
            serialized.FindProperty("damageCooldown").floatValue = 0.45f;
            serialized.FindProperty("knockback").floatValue = 7f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            CreateCircleOutline(hazard.transform, radius, color, 40, 0.12f);
            CreateCircleOutline(
                hazard.transform,
                radius * 0.62f,
                new Color(1f, 0.55f, 0.04f, 0.75f),
                32,
                0.075f);

            HazardPresentation presentation = hazard.AddComponent<HazardPresentation>();
            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("bodyColor").colorValue =
                new Color(1f, 0.02f, 0.01f, 0.92f);
            serializedPresentation.FindProperty("pulseColor").colorValue =
                new Color(1f, 0.3f, 0.01f, 0.95f);
            serializedPresentation.FindProperty("hotFlashColor").colorValue =
                new Color(1f, 0.8f, 0.1f, 1f);
            serializedPresentation.FindProperty("pulseFrequency").floatValue = 4.2f;
            serializedPresentation.FindProperty("pulseStrength").floatValue = 0.72f;
            serializedPresentation.FindProperty("scalePulse").floatValue = 0.035f;
            serializedPresentation.FindProperty("emberRate").floatValue = 18f;
            serializedPresentation.FindProperty("emberRadius").floatValue = 1.15f;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CreateHealthPickup(Vector2 position)
        {
            GameObject pickup = new GameObject("Health Cache");
            pickup.transform.position = position;

            CircleCollider2D collider = pickup.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.32f;

            HealthPickup healthPickup = pickup.AddComponent<HealthPickup>();
            SerializedObject serialized = new SerializedObject(healthPickup);
            serialized.FindProperty("healAmount").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SpriteRenderer renderer = pickup.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadFirstSprite(
                PlayerHealthSpriteAssetPath,
                LegacyPlayerHealthSpriteAssetPath);
            renderer.color = new Color(0.2f, 1f, 0.45f, 1f);
            renderer.sortingOrder = 12;
            CreateCircleOutline(pickup.transform, 0.42f, new Color(0.2f, 1f, 0.45f, 0.65f), 28, 0.05f);

            PickupPresentation presentation = pickup.AddComponent<PickupPresentation>();
            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("accentColor").colorValue =
                new Color(0.2f, 1f, 0.45f, 1f);
            serializedPresentation.FindProperty("bobHeight").floatValue = 0.1f;
            serializedPresentation.FindProperty("bobSpeed").floatValue = 2.8f;
            serializedPresentation.FindProperty("rotateSpeed").floatValue = 36f;
            serializedPresentation.FindProperty("pulseAmount").floatValue = 0.06f;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CreateFireRatePickup(Vector2 position)
        {
            GameObject pickup = new GameObject("Fire Rate Cache");
            pickup.transform.position = position;

            CircleCollider2D collider = pickup.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.32f;

            FireRatePickup fireRatePickup = pickup.AddComponent<FireRatePickup>();
            SerializedObject serialized = new SerializedObject(fireRatePickup);
            serialized.FindProperty("duration").floatValue = 5f;
            serialized.FindProperty("multiplier").floatValue = 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SpriteRenderer renderer = pickup.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadFirstSprite(
                PlayerProjectileSpriteAssetPath,
                LegacyPlayerProjectileSpriteAssetPath);
            renderer.color = new Color(0.2f, 0.9f, 1f, 1f);
            renderer.sortingOrder = 12;
            CreateCircleOutline(pickup.transform, 0.42f, new Color(0.2f, 0.9f, 1f, 0.65f), 28, 0.05f);

            PickupPresentation presentation = pickup.AddComponent<PickupPresentation>();
            SerializedObject serializedPresentation = new SerializedObject(presentation);
            serializedPresentation.FindProperty("accentColor").colorValue =
                new Color(0.2f, 0.9f, 1f, 1f);
            serializedPresentation.FindProperty("bobHeight").floatValue = 0.1f;
            serializedPresentation.FindProperty("bobSpeed").floatValue = 2.8f;
            serializedPresentation.FindProperty("rotateSpeed").floatValue = 36f;
            serializedPresentation.FindProperty("pulseAmount").floatValue = 0.06f;
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CreateWallVisual(
            Transform parent,
            Vector2 size,
            Color? tintOverride = null)
        {
            Sprite wallSprite = LoadFirstSprite(
                BoundaryWallSpriteAssetPath,
                LegacyBoundaryWallSpriteAssetPath);
            if (!wallSprite)
            {
                CreateSquareOutline(parent, size, new Color(0.3f, 0.35f, 0.6f));
                return;
            }

            // The authored EE5 wall strip has visible artwork offset inside a
            // large transparent sprite. Keep the art and collider aligned
            // while allowing the builder to size the boundary.
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
            renderer.color = tintOverride ?? new Color(0.52f, 0.63f, 0.9f, 0.9f);
            renderer.sortingOrder = -10;
        }

        static void CreateGateVisual(Transform parent)
        {
            Sprite gateSprite = LoadFirstSprite(
                EnergyGateSpriteAssetPath,
                LegacyEnergyGateSpriteAssetPath);
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

        static void CreateCircleOutline(
            Transform parent,
            float radius,
            Color color,
            int segments,
            float width)
        {
            LineRenderer line = parent.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segments;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = 14;
            line.numCornerVertices = 2;
            line.material = new Material(Shader.Find("Sprites/Default"));

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }
        }

        static Sprite LoadFirstSprite(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .FirstOrDefault();
        }

        static Sprite LoadFirstSprite(string semanticPath, string legacyPath)
        {
            return LoadFirstSprite(ResolveAssetPath(semanticPath, legacyPath));
        }

        static Sprite[] LoadSprites(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .ToArray();
        }

        static Sprite[] LoadSprites(string semanticPath, string legacyPath)
        {
            return LoadSprites(ResolveAssetPath(semanticPath, legacyPath));
        }

        static string ResolveAssetPath(string semanticPath, string legacyPath)
        {
            return AssetDatabase.LoadMainAssetAtPath(semanticPath)
                ? semanticPath
                : legacyPath;
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
