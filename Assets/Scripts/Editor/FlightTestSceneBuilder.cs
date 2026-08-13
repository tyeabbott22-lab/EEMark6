using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
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
using ExtraterrestrialExhaust.Presentation;

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
        const string SceneBackupPath = "Library/FlightTest.unity.prebuild.bak";
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
        const string MeleeSpritePath = "Assets/Art/Reference/Enemies/sprAlienPurpleSword.png";
        const string MeleeIdleSpritePath = "Assets/Art/Reference/Enemies/sprAlienPurpleSleep.png";
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
        const string NebulaSpritePath = "Assets/Art/Reference/Environment/nebula_backdrop.png";
        const string EnemyBurstSpritePath = "Assets/Art/Reference/Effects/enemy_defeat_burst.png";
        const string LegacyEnemyBurstSpritePath = "Assets/Art/Reference/Effects/sprExplode.png";
        const string EnemyBurstAudioPath = "Assets/Audio/Reference/sfxExplode.wav";
        static bool lastBuildSucceeded;
        // All generated outlines share one material. Rebuilding the scene can
        // create dozens of decorative rings; caching the material keeps an
        // editor session from accumulating an invisible material per ring,
        // while the scene still serializes one stable reference for its lines.
        static Material generatedLineMaterial;
        static bool loggedMissingLineMaterial;

        [MenuItem("Extraterrestrial Exhaust/Build Flight Test Scene")]
        public static void Build()
        {
            BuildInternal(false);
        }

        [MenuItem("Extraterrestrial Exhaust/Build Flight Test Scene (Preserve Prefabs)")]
        public static void BuildPreservingPrefabs()
        {
            BuildInternal(true);
        }

        static void BuildInternal(bool preservePrefabs)
        {
            lastBuildSucceeded = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("FlightTest build cancelled before replacing the active scene.");
                return;
            }

            if (!BackupExistingFlightTestScene())
                return;

            EnsureTag("StopperZone");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
                PlayerProjectile projectilePrefab = preservePrefabs
                    ? AssetDatabase.LoadAssetAtPath<PlayerProjectile>(ProjectilePrefabPath)
                    : CreateProjectilePrefab();
                if (!projectilePrefab)
                {
                    throw new System.InvalidOperationException(
                        preservePrefabs
                            ? $"Could not preserve prefabs because {ProjectilePrefabPath} is missing."
                            : $"Could not create the player projectile at {ProjectilePrefabPath}.");
                }

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
            PlayerCharacter player = preservePrefabs
                ? CreatePrefabBackedPlayer(inputAsset, projectilePrefab, gameState)
                : CreatePlayer(inputAsset, projectilePrefab, gameState, false);
            CreateCamera(player, backdrops);
            EnemyController meleeEnemy = preservePrefabs
                ? CreatePrefabBackedEnemy(
                    gameState,
                    projectilePrefab,
                    "Purple Melee Hunter",
                    Ee5SliceProfile.VerticalSliceMeleeSpawn,
                    EnemyMeleePrefabPath)
                : CreateEnemy(
                    gameState,
                    projectilePrefab,
                    "Purple Melee Hunter",
                    Ee5SliceProfile.VerticalSliceMeleeSpawn,
                    false,
                    EnemyMeleePrefabPath,
                    MeleeSpritePath,
                    MeleeIdleSpritePath,
                    MeleeDefeatSpritePath,
                    false);
            EnemyController gunnerEnemy = preservePrefabs
                ? CreatePrefabBackedEnemy(
                    gameState,
                    projectilePrefab,
                    "White Gunner",
                    Ee5SliceProfile.VerticalSliceGunnerSpawn,
                    EnemyGunnerPrefabPath)
                : CreateEnemy(
                    gameState,
                    projectilePrefab,
                    "White Gunner",
                    Ee5SliceProfile.VerticalSliceGunnerSpawn,
                    true,
                    EnemyGunnerPrefabPath,
                    EnemySpritePath,
                    EnemyIdleSpritePath,
                    EnemyDefeatSpritePath,
                    false);
            SliceObjectiveDirector objectiveDirector = CreateEncounterAndExit(
                gameState,
                meleeEnemy,
                gunnerEnemy);
            // The generated scene contains one authored objective chain. Use
            // those exact scene objects when serializing HUD references rather
            // than making the HUD recover them from a scene-wide lookup.
            EncounterController encounter = UnityEngine.Object.FindFirstObjectByType<EncounterController>();
            EnergyKey energyKey = UnityEngine.Object.FindFirstObjectByType<EnergyKey>();
            LevelExit levelExit = UnityEngine.Object.FindFirstObjectByType<LevelExit>();
            CreateHud(objectiveDirector, gameState, encounter, energyKey, levelExit);
            CreateInstructionTriggers();

            CreateArenaBoundaries();
            CreateFlightStopperZone();
            CreateEnvironmentalPressure();

            // The gold-standard slice is intentionally focused on the EE5 loop:
            // encounter -> key -> gate -> extraction. Pickup and hazard scripts
            // remain optional pressure and recovery beats rather than becoming
            // hidden objective requirements.

            EditorSceneManager.SaveScene(scene, ScenePath);
            lastBuildSucceeded = true;
            ConfigureBuildSettings();
                Selection.activeGameObject = GameObject.Find("Player Craft");
                Debug.Log(
                    $"Built {ScenePath}{(preservePrefabs ? " from existing prefabs without rewriting them" : "")}. "
                    + "Controls: W/Space thrust, A/D or Q/E rotate, S/C stabilize, X flip.");
                ValidateActiveFlightTestSceneContract();
            }
            catch (System.Exception exception)
            {
                // A Unity-version API or imported asset can fail midway
                // through composition. Restore the backed-up scene instead of
                // leaving the editor on an unsaved empty scene and make the
                // real cause visible in the Console.
                Debug.LogException(exception);
                if (File.Exists(ScenePath))
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        [MenuItem("Extraterrestrial Exhaust/Rebuild Flight Test Scene (Full Repair + Validate)")]
        public static void RebuildAndValidate()
        {
            // Keep the common recovery path one deliberate operation. The
            // individual repair menus remain useful for diagnosis, but a
            // reviewer or a fresh checkout should not have to remember which
            // stale prefab profile was repaired before rebuilding the slice.
            RepairEnemyPrefabProfiles();
            RepairPlayerCraftPhysicsProfile();
            RepairPlayerCraftSpriteWiring();
            Build();
            if (lastBuildSucceeded)
                ValidateActiveFlightTestSceneContract();
            else
                Debug.LogWarning(
                    "FlightTest rebuild did not complete; validation was skipped so a restored legacy scene cannot be reported as a valid slice.");
        }

        static bool BackupExistingFlightTestScene()
        {
            if (!File.Exists(ScenePath))
                return true;

            try
            {
                string directory = Path.GetDirectoryName(SceneBackupPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.Copy(ScenePath, SceneBackupPath, true);
                Debug.Log($"Backed up the previous FlightTest scene to {SceneBackupPath}.");
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogError(
                    $"FlightTest build cancelled because the previous scene could not be backed up: {exception.Message}");
                return false;
            }
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

        [MenuItem("Extraterrestrial Exhaust/Repair Enemy Intro Sprite Wiring (Preserve Gameplay Tuning)")]
        public static void RepairEnemyIntroSpriteWiring()
        {
            bool meleeRepaired = RepairEnemyIntroSpriteWiring(EnemyMeleePrefabPath, false);
            bool gunnerRepaired = RepairEnemyIntroSpriteWiring(EnemyGunnerPrefabPath, true);
            bool sceneRepaired = false;
            bool sceneSaved = false;

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.path == ScenePath)
            {
                sceneRepaired |= RepairEnemyIntroSpritePresentation(
                    GameObject.Find("Purple Melee Hunter"),
                    false);
                sceneRepaired |= RepairEnemyIntroSpritePresentation(
                    GameObject.Find("White Gunner"),
                    true);
                if (sceneRepaired)
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    sceneSaved = EditorSceneManager.SaveScene(activeScene, ScenePath);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Repaired only the EE5 enemy intro sprite arrays and facing contract. "
                + $"Purple melee prefab: {meleeRepaired}; white gunner prefab: {gunnerRepaired}; "
                + $"active FlightTest: {sceneRepaired}. "
                + "Gameplay physics, movement, weapon cadence, and other inspector tuning were preserved. "
                + (sceneRepaired
                    ? (sceneSaved
                        ? "The repaired active scene was saved."
                        : "The active scene could not be saved; save FlightTest manually.")
                    : "No active-scene intro changes were needed.")
                + " Rebuild FlightTest with Preserve Prefabs afterward if the scene needs regeneration.");
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

        static bool RepairEnemyIntroSpriteWiring(string prefabPath, bool ranged)
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (!prefabContents)
            {
                Debug.LogError($"Could not open enemy prefab at {prefabPath}.");
                return false;
            }

            bool changed = RepairEnemyIntroSpritePresentation(prefabContents, ranged);
            if (changed)
                PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabContents);
            return changed;
        }

        static bool RepairEnemyIntroSpritePresentation(GameObject enemyObject, bool ranged)
        {
            if (!enemyObject)
                return false;

            EnemySpritePresentation presentation =
                enemyObject.GetComponent<EnemySpritePresentation>();
            if (!presentation)
            {
                Debug.LogWarning(
                    $"Could not repair intro sprite wiring on {enemyObject.name}: "
                    + "EnemySpritePresentation is missing.");
                return false;
            }

            bool changed = false;
            SpriteRenderer spriteRenderer = enemyObject.GetComponent<SpriteRenderer>();
            Sprite activeSprite = LoadFirstSprite(ranged ? EnemySpritePath : MeleeSpritePath);
            if (spriteRenderer && spriteRenderer.sprite != activeSprite)
            {
                spriteRenderer.sprite = activeSprite;
                EditorUtility.SetDirty(spriteRenderer);
                changed = true;
            }

            SerializedObject serializedPresentation = new SerializedObject(presentation);
            changed |= SetBool(
                serializedPresentation,
                "faceDormantTowardTarget",
                ranged || Ee5SliceProfile.EnemyMeleeFacesDormantTarget);
            changed |= SetBool(
                serializedPresentation,
                "invertDormantSpriteX",
                !ranged && Ee5SliceProfile.EnemyMeleeInvertsSpriteDuringIntro);
            changed |= SetBool(serializedPresentation, "forwardIsLocalNegativeX", ranged);
            changed |= SetBool(serializedPresentation, "restoreFacingAfterWake", true);
            changed |= SetBool(serializedPresentation, "pingPongDormantAnimation", true);
            changed |= SetBool(serializedPresentation, "randomizeDormantStartFrame", true);
            changed |= SetFloat(
                serializedPresentation,
                "dormantFacingHysteresis",
                Ee5SliceProfile.EnemyDormantFacingHysteresis);
            changed |= SetSpriteArrayIfDifferent(
                serializedPresentation,
                "dormantSprites",
                LoadSprites(ranged ? EnemyIdleSpritePath : MeleeIdleSpritePath));
            changed |= SetSpriteArrayIfDifferent(
                serializedPresentation,
                "alertSprites",
                LoadSprites(ranged ? EnemyDefeatSpritePath : MeleeDefeatSpritePath));
            changed |= SetSpriteArrayIfDifferent(
                serializedPresentation,
                "activeSprites",
                LoadSprites(ranged ? EnemySpritePath : MeleeSpritePath));
            changed |= SetSpriteArrayIfDifferent(
                serializedPresentation,
                "defeatedSprites",
                LoadSprites(ranged ? EnemyDefeatSpritePath : MeleeDefeatSpritePath));
            serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            if (changed)
                EditorUtility.SetDirty(presentation);
            return changed;
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
            bool saved = EditorSceneManager.SaveScene(activeScene, ScenePath);

            Debug.Log(
                "Repaired the active FlightTest player to the EE5 profile: "
                + $"55 thrust, {Ee5SliceProfile.PlayerFlightLinearDamping:0.##} linear damping, one-second player shots, "
                + "12 recoil, orange craft sprite, and room-reset death flow. "
                + (saved
                    ? "The repaired scene was saved."
                    : "The scene could not be saved; save FlightTest manually."));
        }

        [MenuItem("Extraterrestrial Exhaust/Repair Player Craft Physics Profile")]
        public static void RepairPlayerCraftPhysicsProfile()
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (!prefabContents)
            {
                Debug.LogError($"Could not open player prefab at {PlayerPrefabPath}.");
                return;
            }

            bool prefabRepaired = ApplyPlayerCraftPhysicsProfile(prefabContents);
            if (prefabRepaired)
                PrefabUtility.SaveAsPrefabAsset(prefabContents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(prefabContents);

            bool sceneRepaired = false;
            bool sceneSaved = false;
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.path == ScenePath)
            {
                GameObject scenePlayer = GameObject.Find("Player Craft");
                if (scenePlayer)
                {
                    sceneRepaired = ApplyPlayerCraftPhysicsProfile(scenePlayer);
                    if (sceneRepaired)
                    {
                        EditorUtility.SetDirty(scenePlayer);
                        EditorSceneManager.MarkSceneDirty(activeScene);
                        sceneSaved = EditorSceneManager.SaveScene(activeScene, ScenePath);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Applied the EE5 player Rigidbody2D and flight-motor profile. "
                + $"Prefab changed: {prefabRepaired}; active FlightTest changed: {sceneRepaired}. "
                + "The intended Unity 6 feel is 0.08 linear damping with 3.25 angular damping. "
                + (sceneRepaired
                    ? (sceneSaved
                        ? "The repaired active scene was saved."
                        : "The active scene could not be saved; save FlightTest manually.")
                    : "No active-scene physics changes were needed."));
        }

        [MenuItem("Extraterrestrial Exhaust/Validate Player Craft Physics Profile")]
        public static void ValidatePlayerCraftPhysicsProfile()
        {
            List<string> issues = new List<string>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            AddPlayerCraftPhysicsIssues(prefab, "Prefab", issues);

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.path == ScenePath)
                AddPlayerCraftPhysicsIssues(GameObject.Find("Player Craft"), "Active FlightTest", issues);

            if (issues.Count == 0)
            {
                Debug.Log(
                    "Player craft physics matches the EE5 profile: "
                    + $"mass {Ee5SliceProfile.PlayerMass}, gravity {Ee5SliceProfile.PlayerGravityScale}, "
                    + $"linear damping {Ee5SliceProfile.PlayerFlightLinearDamping}, "
                    + $"angular damping {Ee5SliceProfile.PlayerFlightAngularDamping}.");
                return;
            }

            Debug.LogWarning(
                "Player craft physics profile is stale: " + string.Join("; ", issues)
                + ". Run Extraterrestrial Exhaust > Repair Player Craft Physics Profile.");
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

        [MenuItem("Extraterrestrial Exhaust/Repair Active FlightTest Objective Contract")]
        public static void RepairActiveFlightTestObjectiveContract()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.path != ScenePath)
            {
                Debug.LogError(
                    $"Open {ScenePath} before repairing the active FlightTest objective contract.");
                return;
            }

            int removedLegacyPlaceholders = RemoveLegacyObjectivePlaceholders();

            EncounterController encounter = UnityEngine.Object.FindFirstObjectByType<EncounterController>();
            EnergyKey energyKey = UnityEngine.Object.FindFirstObjectByType<EnergyKey>();
            EnergyGate gate = UnityEngine.Object.FindFirstObjectByType<EnergyGate>();
            LevelExit exit = UnityEngine.Object.FindFirstObjectByType<LevelExit>();
            SliceObjectiveDirector objective =
                UnityEngine.Object.FindFirstObjectByType<SliceObjectiveDirector>();
            GameStateMachine gameState = UnityEngine.Object.FindFirstObjectByType<GameStateMachine>();
            EnemyController[] enemies =
                UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            Vector2 keyPosition = energyKey ? energyKey.transform.position : Vector2.zero;
            EnemyController carrier = enemies
                .Where(enemy => enemy && enemy.GetComponent<EnemyWeapon>())
                .OrderBy(enemy => Vector2.Distance(
                    keyPosition,
                    enemy.transform.position))
                .FirstOrDefault();
            EnemyController melee = enemies
                .Where(enemy => enemy && enemy.IsMelee)
                .OrderBy(enemy => Vector2.Distance(
                    keyPosition,
                    enemy.transform.position))
                .FirstOrDefault();

            List<string> missing = new List<string>();
            if (!encounter) missing.Add("EncounterController");
            if (!energyKey) missing.Add("EnergyKey");
            if (!gate) missing.Add("EnergyGate");
            if (!exit) missing.Add("LevelExit");
            if (!objective) missing.Add("SliceObjectiveDirector");
            if (!gameState) missing.Add("GameStateMachine");
            if (!carrier) missing.Add("ranged enemy carrier");
            if (!melee) missing.Add("melee encounter enemy");
            if (missing.Count > 0)
            {
                Debug.LogError(
                    "Could not repair the FlightTest objective contract; missing: "
                    + string.Join(", ", missing));
                return;
            }

            Transform keyTarget = gate.transform.Find("Key Target");
            if (!keyTarget)
            {
                keyTarget = new GameObject("Key Target").transform;
                keyTarget.SetParent(gate.transform, false);
                EditorUtility.SetDirty(keyTarget.gameObject);
            }
            if (Vector2.Distance(
                    keyTarget.localPosition,
                    Ee5SliceProfile.VerticalSliceGateKeyTarget) > 0.001f)
            {
                Undo.RecordObject(keyTarget, "Repair FlightTest gate key target");
                keyTarget.localPosition = Ee5SliceProfile.VerticalSliceGateKeyTarget;
                EditorUtility.SetDirty(keyTarget);
            }

            RepairEncounterRoster(encounter, melee, carrier);
            RepairSceneReference(energyKey, "requiredEncounter", encounter);
            RepairSceneReference(energyKey, "enemyTarget", carrier);
            RepairSceneReference(energyKey, "targetGate", gate);
            RepairSceneReference(gate, "keyTarget", keyTarget);
            RepairSceneReference(exit, "encounter", encounter);
            RepairSceneReference(exit, "requiredGate", gate);
            RepairSceneReference(exit, "gameState", gameState);
            RepairSceneReference(objective, "encounter", encounter);
            RepairSceneReference(objective, "energyKey", energyKey);
            RepairSceneReference(objective, "gate", gate);
            RepairSceneReference(objective, "exit", exit);
            RepairSceneReference(objective, "gameState", gameState);

            // The HUD mirrors the same chain for status text and banners. Keep
            // those links serialized too; runtime lookup remains a recovery
            // path for hand-authored scenes, not the generated scene contract.
            GameplayHud hud = UnityEngine.Object.FindFirstObjectByType<GameplayHud>();
            if (hud)
            {
                RepairSceneReference(hud, "encounter", encounter);
                RepairSceneReference(hud, "energyKey", energyKey);
                RepairSceneReference(hud, "exit", exit);
                RepairSceneReference(hud, "gameState", gameState);
                RepairSceneReference(hud, "objectiveDirector", objective);
            }

            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = objective.gameObject;
            List<string> remainingIssues = GetGeneratedSceneContractIssues();
            // This menu is explicitly the recovery path for a scene that was
            // built before serialized references were repaired. Persist the
            // repair here so a successful console message cannot leave the
            // on-disk FlightTest one save behind the playable editor scene.
            bool saved = EditorSceneManager.SaveScene(activeScene, ScenePath);
            if (remainingIssues.Count == 0)
            {
                Debug.Log(
                    "Repaired and validated the active FlightTest objective contract. "
                    + (saved
                        ? "Serialized encounter, key, gate, exit, and game-state links were saved."
                        : "The scene was repaired but could not be saved; save FlightTest manually.")
                    + (removedLegacyPlaceholders > 0
                        ? $" Removed {removedLegacyPlaceholders} legacy objective placeholder(s)."
                        : string.Empty));
            }
            else
            {
                Debug.LogWarning(
                    "Objective references were repaired, but the scene still has contract issues: "
                    + string.Join("; ", remainingIssues)
                    + (saved ? ". The repaired scene was saved." : ". The scene could not be saved."));
            }
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
            if (!body || body.bodyType != RigidbodyType2D.Dynamic)
                mismatches.Add($"bodyType={(body ? body.bodyType.ToString() : "<missing>")} (expected Dynamic)");
            if (!body || !body.simulated)
                mismatches.Add("Rigidbody2D.simulated=false (expected true)");
            if (body && body.constraints != RigidbodyConstraints2D.None)
                mismatches.Add($"constraints={body.constraints} (expected None)");
            if (!body || !Mathf.Approximately(body.gravityScale, Ee5SliceProfile.PlayerGravityScale))
                mismatches.Add($"gravityScale={(body ? body.gravityScale : -1f)} (expected {Ee5SliceProfile.PlayerGravityScale})");
            if (!body || !Mathf.Approximately(body.linearDamping, Ee5SliceProfile.PlayerFlightLinearDamping))
                mismatches.Add($"linearDamping={(body ? body.linearDamping : -1f)} (expected {Ee5SliceProfile.PlayerFlightLinearDamping})");
            if (!body || !Mathf.Approximately(body.angularDamping, Ee5SliceProfile.PlayerFlightAngularDamping))
                mismatches.Add($"angularDamping={(body ? body.angularDamping : -1f)} (expected {Ee5SliceProfile.PlayerFlightAngularDamping})");

            PlayerFlightMotor motor = playerObject.GetComponent<PlayerFlightMotor>();
            if (!motor)
            {
                mismatches.Add("PlayerFlightMotor is missing");
            }
            else
            {
                SerializedObject serializedMotor = new SerializedObject(motor);
                SerializedProperty stopperTag = serializedMotor.FindProperty("stopperTag");
                if (stopperTag == null || stopperTag.stringValue != "StopperZone")
                {
                    mismatches.Add(
                        $"stopperTag={(stopperTag != null ? stopperTag.stringValue : "<missing>")} (expected StopperZone)");
                }
            }

            PlayerCollisionDamage collisionDamage = playerObject.GetComponent<PlayerCollisionDamage>();
            if (collisionDamage && collisionDamage.enabled != Ee5SliceProfile.PlayerCollisionDamageEnabled)
            {
                mismatches.Add(
                    $"PlayerCollisionDamage.enabled={collisionDamage.enabled} "
                    + $"(expected {Ee5SliceProfile.PlayerCollisionDamageEnabled})");
            }

            HealthComponent healthProfile = playerObject.GetComponent<HealthComponent>();
            if (!healthProfile)
            {
                mismatches.Add("HealthComponent is missing");
            }
            else
            {
                SerializedObject serializedHealth = new SerializedObject(healthProfile);
                float maxHealth = serializedHealth.FindProperty("maxHealth").floatValue;
                float invulnerability = serializedHealth.FindProperty("invulnerabilityDuration").floatValue;
                if (!Mathf.Approximately(maxHealth, Ee5SliceProfile.PlayerMaxHealth))
                    mismatches.Add($"maxHealth={maxHealth} (expected {Ee5SliceProfile.PlayerMaxHealth})");
                if (!Mathf.Approximately(invulnerability, Ee5SliceProfile.PlayerInvulnerabilityDuration))
                {
                    mismatches.Add(
                        $"invulnerabilityDuration={invulnerability} "
                        + $"(expected {Ee5SliceProfile.PlayerInvulnerabilityDuration})");
                }
            }

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

            PlayerFlightPresentation presentation =
                playerObject.GetComponent<PlayerFlightPresentation>();
            if (!presentation)
            {
                mismatches.Add("PlayerFlightPresentation is missing");
            }
            else
            {
                SerializedObject serializedPresentation =
                    new SerializedObject(presentation);
                SerializedProperty enforceProfile =
                    serializedPresentation.FindProperty("enforceEe5Profile");
                if (enforceProfile == null || !enforceProfile.boolValue)
                    mismatches.Add("PlayerFlightPresentation.enforceEe5Profile=false");
                CheckSerializedFloat(
                    serializedPresentation,
                    "boostedExhaustYScale",
                    Ee5SliceProfile.PlayerBoostedExhaustYScale,
                    "Player boosted exhaust Y scale",
                    mismatches);
            }

            PlayerRespawnController recovery = playerObject.GetComponent<PlayerRespawnController>();
            if (!recovery)
            {
                mismatches.Add("PlayerRespawnController is missing");
            }
            else
            {
                SerializedObject serializedRecovery = new SerializedObject(recovery);
                SerializedProperty reloadScene = serializedRecovery.FindProperty("reloadSceneOnDeath");
                SerializedProperty reloadDelay = serializedRecovery.FindProperty("reloadDelay");
                SerializedProperty respawnAutomatically =
                    serializedRecovery.FindProperty("respawnAutomatically");
                if (reloadScene == null || !reloadScene.boolValue)
                    mismatches.Add("reloadSceneOnDeath=false (expected true)");
                if (reloadDelay == null || !Mathf.Approximately(reloadDelay.floatValue, 0f))
                {
                    mismatches.Add(
                        $"reloadDelay={(reloadDelay != null ? reloadDelay.floatValue : -1f)} (expected 0)");
                }
                if (respawnAutomatically != null && respawnAutomatically.boolValue)
                    mismatches.Add("respawnAutomatically=true (expected false for EE5 room reset)");
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
                body.linearDamping = Ee5SliceProfile.PlayerFlightLinearDamping;
                body.angularDamping = Ee5SliceProfile.PlayerFlightAngularDamping;
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
                ApplyEe5PlayerMotorProfile(motor);

            PlayerCollisionDamage collisionDamage = playerObject.GetComponent<PlayerCollisionDamage>();
            if (collisionDamage)
                collisionDamage.enabled = Ee5SliceProfile.PlayerCollisionDamageEnabled;

            HealthComponent health = playerObject.GetComponent<HealthComponent>();
            if (health)
            {
                SerializedObject serializedHealth = new SerializedObject(health);
                serializedHealth.FindProperty("maxHealth").floatValue = Ee5SliceProfile.PlayerMaxHealth;
                serializedHealth.FindProperty("invulnerabilityDuration").floatValue =
                    Ee5SliceProfile.PlayerInvulnerabilityDuration;
                serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerFlightInput flightInput = playerObject.GetComponent<PlayerFlightInput>();
            if (flightInput)
            {
                flightInput.ConfigureInputAsset(inputAsset);
                SerializedObject serializedInput = new SerializedObject(flightInput);
                serializedInput.FindProperty("gameState").objectReferenceValue = gameState;
                serializedInput.FindProperty("includeEe5KeyboardFallback").boolValue = true;
                serializedInput.FindProperty("turnDeadzone").floatValue = Ee5SliceProfile.PlayerTurnDeadzone;
                serializedInput.FindProperty("thrustDeadzone").floatValue = Ee5SliceProfile.PlayerThrustDeadzone;
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
                serializedPresentation.FindProperty("enforceEe5Profile").boolValue = true;
                serializedPresentation.FindProperty("boostedExhaustLengthMultiplier").floatValue =
                    Ee5SliceProfile.PlayerBoostedExhaustLengthMultiplier;
                serializedPresentation.FindProperty("boostedExhaustWidthMultiplier").floatValue =
                    Ee5SliceProfile.PlayerBoostedExhaustWidthMultiplier;
                serializedPresentation.FindProperty("boostedExhaustYScale").floatValue =
                    Ee5SliceProfile.PlayerBoostedExhaustYScale;
                serializedPresentation.FindProperty("boostedParticleEmissionMultiplier").floatValue =
                    Ee5SliceProfile.PlayerBoostedParticleEmissionMultiplier;
                serializedPresentation.FindProperty("boostedExhaustStartColor").colorValue =
                    Ee5SliceProfile.PlayerBoostedExhaustCoreColor;
                serializedPresentation.FindProperty("boostedExhaustMidColor").colorValue =
                    Ee5SliceProfile.PlayerBoostedExhaustMidColor;
                serializedPresentation.FindProperty("boostedExhaustEndColor").colorValue =
                    Ee5SliceProfile.PlayerBoostedExhaustTipColor;
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
            float expectedRootScaleX = ranged
                ? Ee5SliceProfile.EnemyGunnerRootScaleX
                : Ee5SliceProfile.EnemyMeleeRootScaleX;
            Vector3 rootScale = prefabContents.transform.localScale;
            if (!Mathf.Approximately(rootScale.x, expectedRootScaleX))
            {
                rootScale.x = expectedRootScaleX;
                prefabContents.transform.localScale = rootScale;
                changed = true;
            }
            EnemyController controller = prefabContents.GetComponent<EnemyController>();
            HealthComponent health = prefabContents.GetComponent<HealthComponent>();
            if (health)
            {
                SerializedObject serializedHealth = new SerializedObject(health);
                changed |= SetFloat(
                    serializedHealth,
                    "invulnerabilityDuration",
                    Ee5SliceProfile.EnemyInvulnerabilityDuration);
                serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            }

            if (controller)
            {
                SerializedObject serializedController = new SerializedObject(controller);
                changed |= SetFloat(
                    serializedController,
                    "wakeSignalDistanceMultiplier",
                    Ee5SliceProfile.EnemyWakeSignalDistanceMultiplier);
                changed |= SetFloat(
                    serializedController,
                    "chaseSpeed",
                    ranged ? Ee5SliceProfile.EnemyGunnerChaseSpeed : Ee5SliceProfile.EnemyMeleeChaseSpeed);
                changed |= SetFloat(
                    serializedController,
                    "faceTurnSpeed",
                    Ee5SliceProfile.EnemyFaceTurnSpeed);
                changed |= SetFloat(
                    serializedController,
                    "attackRange",
                    ranged ? 7f : Ee5SliceProfile.EnemyMeleeAttackRange);
                changed |= SetFloat(
                    serializedController,
                    "attackExitRange",
                    ranged ? 7f : Ee5SliceProfile.EnemyMeleeAttackExitRange);
                changed |= SetFloat(
                    serializedController,
                    "contactDamageRange",
                    ranged ? 0f : Ee5SliceProfile.EnemyMeleeContactRange);
                changed |= SetFloat(
                    serializedController,
                    "attackFacingRefreshDegrees",
                    ranged ? 0f : Ee5SliceProfile.EnemyMeleeAttackFacingRefreshDegrees);
                changed |= SetFloat(serializedController, "targetBuffer", 0.04f);
                changed |= SetEnum(
                    serializedController,
                    "movementMode",
                    ranged ? EnemyMovementMode.Wander : EnemyMovementMode.Chase);
                changed |= SetFloat(
                    serializedController,
                    "wanderRadius",
                    Ee5SliceProfile.EnemyGunnerWanderRadius);
                changed |= SetFloat(
                    serializedController,
                    "wanderDurationMin",
                    Ee5SliceProfile.EnemyGunnerWanderDurationMin);
                changed |= SetFloat(
                    serializedController,
                    "wanderDurationMax",
                    Ee5SliceProfile.EnemyGunnerWanderDurationMax);
                changed |= SetFloat(
                    serializedController,
                    "wakeDuration",
                    Ee5SliceProfile.EnemyWakeBuildupDuration);
                changed |= SetFloat(
                    serializedController,
                    "wakeIdleDurationMin",
                    Ee5SliceProfile.EnemyWakeIdleDurationMin);
                changed |= SetFloat(
                    serializedController,
                    "wakeIdleDurationMax",
                    Ee5SliceProfile.EnemyWakeIdleDurationMax);
                changed |= SetFloat(
                    serializedController,
                    "wakeScreamDuration",
                    Ee5SliceProfile.EnemyWakeScreamDuration);
                changed |= SetFloat(
                    serializedController,
                    "wakeSignalChargeDuration",
                    Ee5SliceProfile.EnemyWakeSignalChargeDuration);
                changed |= SetFloat(
                    serializedController,
                    "wakeSignalChargeDecay",
                    Ee5SliceProfile.EnemyWakeSignalChargeDecay);
                changed |= SetFloat(
                    serializedController,
                    "wakeSignalChargeSpeedAtEdge",
                    Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtEdge);
                changed |= SetFloat(
                    serializedController,
                    "wakeSignalChargeSpeedAtClose",
                    Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtClose);
                changed |= SetFloat(
                    serializedController,
                    "wakeFinalWarningDuration",
                    Ee5SliceProfile.EnemyWakeFinalWarningDuration);
                // The gold EE5 gunner patrols its authored home radius. Keep
                // the legacy orbit experiment available in the component, but
                // never leave the prefab itself in the orbiting state.
                changed |= SetBool(
                    serializedController,
                    "orbitWhileAttacking",
                    false);
                changed |= SetBool(serializedController, "forwardIsLocalNegativeX", ranged);
                serializedController.ApplyModifiedPropertiesWithoutUndo();
            }

            Rigidbody2D body = prefabContents.GetComponent<Rigidbody2D>();
            if (body)
            {
                bool bodyChanged = false;
                if (body.bodyType != RigidbodyType2D.Kinematic)
                {
                    body.bodyType = RigidbodyType2D.Kinematic;
                    bodyChanged = true;
                }
                if (!Mathf.Approximately(body.linearDamping, 0f))
                {
                    body.linearDamping = 0f;
                    bodyChanged = true;
                }
                if (!Mathf.Approximately(body.angularDamping, 0.05f))
                {
                    body.angularDamping = 0.05f;
                    bodyChanged = true;
                }
                if (body.interpolation != RigidbodyInterpolation2D.Interpolate)
                {
                    body.interpolation = RigidbodyInterpolation2D.Interpolate;
                    bodyChanged = true;
                }
                if (bodyChanged)
                {
                    EditorUtility.SetDirty(body);
                    changed = true;
                }
            }

            Collider2D bodyCollider = prefabContents.GetComponent<Collider2D>();
            if (bodyCollider)
            {
                bool expectedTrigger = !ranged && Ee5SliceProfile.EnemyMeleeUsesTriggerBody;
                if (bodyCollider.isTrigger != expectedTrigger)
                {
                    bodyCollider.isTrigger = expectedTrigger;
                    EditorUtility.SetDirty(bodyCollider);
                    changed = true;
                }
            }

            EnemySpritePresentation presentation = prefabContents.GetComponent<EnemySpritePresentation>();
            if (presentation)
            {
                SpriteRenderer spriteRenderer = prefabContents.GetComponent<SpriteRenderer>();
                if (spriteRenderer)
                {
                    Sprite activeSprite = LoadFirstSprite(
                        ranged ? EnemySpritePath : MeleeSpritePath);
                    if (spriteRenderer.sprite != activeSprite)
                    {
                        spriteRenderer.sprite = activeSprite;
                        EditorUtility.SetDirty(spriteRenderer);
                        changed = true;
                    }
                }

                SerializedObject serializedPresentation = new SerializedObject(presentation);
                changed |= SetBool(
                    serializedPresentation,
                    "faceDormantTowardTarget",
                    ranged || Ee5SliceProfile.EnemyMeleeFacesDormantTarget);
                changed |= SetBool(
                    serializedPresentation,
                    "invertDormantSpriteX",
                    !ranged && Ee5SliceProfile.EnemyMeleeInvertsSpriteDuringIntro);
                changed |= SetBool(serializedPresentation, "forwardIsLocalNegativeX", ranged);
                changed |= SetFloat(
                    serializedPresentation,
                    "dormantFacingHysteresis",
                    Ee5SliceProfile.EnemyDormantFacingHysteresis);
                changed |= SetBool(serializedPresentation, "restoreFacingAfterWake", true);
                changed |= SetBool(serializedPresentation, "pingPongDormantAnimation", true);
                changed |= SetBool(serializedPresentation, "randomizeDormantStartFrame", true);
                changed |= SetFloat(serializedPresentation, "animationFramesPerSecond", 10f);
                changed |= SetFloat(serializedPresentation, "dormantFramesPerSecond", 8f);
                changed |= SetFloat(serializedPresentation, "wakeFramesPerSecond", 14f);
                Sprite[] dormantSprites = LoadSprites(
                    ranged ? EnemyIdleSpritePath : MeleeIdleSpritePath);
                Sprite[] activeSprites = LoadSprites(
                    ranged ? EnemySpritePath : MeleeSpritePath);
                Sprite[] wakeSprites = LoadSprites(
                    ranged ? EnemyDefeatSpritePath : MeleeDefeatSpritePath);
                changed |= SetSpriteArrayIfDifferent(
                    serializedPresentation, "dormantSprites", dormantSprites);
                changed |= SetSpriteArrayIfDifferent(
                    serializedPresentation, "alertSprites", wakeSprites);
                changed |= SetSpriteArrayIfDifferent(
                    serializedPresentation, "activeSprites", activeSprites);
                changed |= SetSpriteArrayIfDifferent(
                    serializedPresentation, "defeatedSprites", wakeSprites);
                serializedPresentation.ApplyModifiedPropertiesWithoutUndo();
            }

            EnemyContactDamage contactDamage = prefabContents.GetComponent<EnemyContactDamage>();
            if (!contactDamage)
            {
                contactDamage = prefabContents.AddComponent<EnemyContactDamage>();
                changed = true;
            }

            if (contactDamage)
            {
                SerializedObject serializedContact = new SerializedObject(contactDamage);
                changed |= SetFloat(
                    serializedContact,
                    "damage",
                    Ee5SliceProfile.EnemyContactDamage);
                changed |= SetFloat(
                    serializedContact,
                    "cooldown",
                    Ee5SliceProfile.EnemyContactCooldown);
                changed |= SetFloat(
                    serializedContact,
                    "knockback",
                    Ee5SliceProfile.EnemyContactKnockback);
                serializedContact.ApplyModifiedPropertiesWithoutUndo();
            }

            if (ranged)
            {
                EnemyWeapon weapon = prefabContents.GetComponent<EnemyWeapon>();
                if (weapon)
                {
                    SerializedObject serializedWeapon = new SerializedObject(weapon);
                    changed |= SetBool(serializedWeapon, "enforceEe5Profile", true);
                    changed |= SetFloat(
                        serializedWeapon,
                        "fireCooldown",
                        Ee5SliceProfile.EnemyGunnerFireCooldown);
                    changed |= SetFloat(
                        serializedWeapon,
                        "projectileSpeed",
                        Ee5SliceProfile.EnemyGunnerProjectileSpeed);
                    changed |= SetFloat(
                        serializedWeapon,
                        "projectileLifetime",
                        Ee5SliceProfile.EnemyGunnerProjectileLifetime);
                    changed |= SetFloat(
                        serializedWeapon,
                        "projectileKnockback",
                        Ee5SliceProfile.EnemyGunnerProjectileKnockback);
                    changed |= SetBool(
                        serializedWeapon,
                        "mirrorFirePointYWithUprightFlip",
                        Ee5SliceProfile.EnemyGunnerMirrorFirePointYWithUprightFlip);
                    SerializedProperty firePointProperty =
                        serializedWeapon.FindProperty("firePoint");
                    if (firePointProperty != null
                        && firePointProperty.objectReferenceValue is Transform firePoint)
                    {
                        if (firePoint.localPosition != Ee5SliceProfile.EnemyGunnerFirePointLocalPosition)
                        {
                            firePoint.localPosition =
                                Ee5SliceProfile.EnemyGunnerFirePointLocalPosition;
                            EditorUtility.SetDirty(firePoint);
                            changed = true;
                        }
                    }
                    changed |= SetBool(
                        serializedWeapon,
                        "requireTargetWithinAttackRange",
                        Ee5SliceProfile.EnemyGunnerRequiresAttackRange);
                    changed |= SetBool(
                        serializedWeapon,
                        "requireLineOfSightToFire",
                        Ee5SliceProfile.EnemyGunnerRequiresLineOfSightToFire);
                    changed |= SetBool(
                        serializedWeapon,
                        "drawAimTelegraph",
                        Ee5SliceProfile.EnemyGunnerDrawAimTelegraph);
                    serializedWeapon.ApplyModifiedPropertiesWithoutUndo();
                }
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
            Rigidbody2D body = prefab.GetComponent<Rigidbody2D>();
            if (!body)
            {
                issues.Add($"{prefabPath} has no Rigidbody2D");
            }
            else
            {
                if (body.bodyType != RigidbodyType2D.Kinematic)
                    issues.Add($"{prefabPath} Rigidbody2D is dynamic (expected kinematic)");
                if (!Mathf.Approximately(body.linearDamping, 0f))
                    issues.Add($"{prefabPath} linearDamping={body.linearDamping} (expected 0)");
                if (body.interpolation != RigidbodyInterpolation2D.Interpolate)
                    issues.Add($"{prefabPath} Rigidbody2D interpolation is not Interpolate");
            }
            Collider2D bodyCollider = prefab.GetComponent<Collider2D>();
            if (!bodyCollider)
            {
                issues.Add($"{prefabPath} has no root Collider2D");
            }
            else
            {
                bool expectedTrigger = !ranged && Ee5SliceProfile.EnemyMeleeUsesTriggerBody;
                if (bodyCollider.isTrigger != expectedTrigger)
                    issues.Add($"{prefabPath} root collider trigger={bodyCollider.isTrigger}");
            }
            HealthComponent health = prefab.GetComponent<HealthComponent>();
            if (!health)
            {
                issues.Add($"{prefabPath} has no HealthComponent");
            }
            else
            {
                SerializedObject serializedHealth = new SerializedObject(health);
                float invulnerability = serializedHealth.FindProperty("invulnerabilityDuration").floatValue;
                if (!Mathf.Approximately(invulnerability, Ee5SliceProfile.EnemyInvulnerabilityDuration))
                {
                    issues.Add(
                        $"{prefabPath} invulnerabilityDuration={invulnerability} "
                        + $"(expected {Ee5SliceProfile.EnemyInvulnerabilityDuration})");
                }
            }

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
                float expectedRootScaleX = ranged
                    ? Ee5SliceProfile.EnemyGunnerRootScaleX
                    : Ee5SliceProfile.EnemyMeleeRootScaleX;
                if (!Mathf.Approximately(prefab.transform.localScale.x, expectedRootScaleX))
                {
                    issues.Add(
                        $"{prefabPath} root scale.x={prefab.transform.localScale.x}; "
                        + $"expected {expectedRootScaleX}");
                }
                CheckSerializedFloat(
                    serializedController,
                    "wakeDuration",
                    Ee5SliceProfile.EnemyWakeBuildupDuration,
                    $"{prefabPath} wakeDuration",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeIdleDurationMin",
                    Ee5SliceProfile.EnemyWakeIdleDurationMin,
                    $"{prefabPath} wakeIdleDurationMin",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeIdleDurationMax",
                    Ee5SliceProfile.EnemyWakeIdleDurationMax,
                    $"{prefabPath} wakeIdleDurationMax",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeScreamDuration",
                    Ee5SliceProfile.EnemyWakeScreamDuration,
                    $"{prefabPath} wakeScreamDuration",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeSignalChargeDuration",
                    Ee5SliceProfile.EnemyWakeSignalChargeDuration,
                    $"{prefabPath} wakeSignalChargeDuration",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeSignalChargeDecay",
                    Ee5SliceProfile.EnemyWakeSignalChargeDecay,
                    $"{prefabPath} wakeSignalChargeDecay",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeSignalChargeSpeedAtEdge",
                    Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtEdge,
                    $"{prefabPath} wakeSignalChargeSpeedAtEdge",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeSignalChargeSpeedAtClose",
                    Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtClose,
                    $"{prefabPath} wakeSignalChargeSpeedAtClose",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wakeFinalWarningDuration",
                    Ee5SliceProfile.EnemyWakeFinalWarningDuration,
                    $"{prefabPath} wakeFinalWarningDuration",
                    issues);
                float chaseSpeed = serializedController.FindProperty("chaseSpeed").floatValue;
                float expectedChaseSpeed = ranged
                    ? Ee5SliceProfile.EnemyGunnerChaseSpeed
                    : Ee5SliceProfile.EnemyMeleeChaseSpeed;
                if (!Mathf.Approximately(chaseSpeed, expectedChaseSpeed))
                    issues.Add($"{prefabPath} chaseSpeed={chaseSpeed}");
                SerializedProperty movementMode = serializedController.FindProperty("movementMode");
                int expectedMovementMode = ranged
                    ? (int)EnemyMovementMode.Wander
                    : (int)EnemyMovementMode.Chase;
                if (movementMode == null || movementMode.enumValueIndex != expectedMovementMode)
                    issues.Add($"{prefabPath} movementMode is not EE5-compatible");
                CheckSerializedFloat(
                    serializedController,
                    "wanderRadius",
                    Ee5SliceProfile.EnemyGunnerWanderRadius,
                    $"{prefabPath} wanderRadius",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wanderDurationMin",
                    Ee5SliceProfile.EnemyGunnerWanderDurationMin,
                    $"{prefabPath} wanderDurationMin",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "wanderDurationMax",
                    Ee5SliceProfile.EnemyGunnerWanderDurationMax,
                    $"{prefabPath} wanderDurationMax",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "contactDamageRange",
                    ranged ? 0f : Ee5SliceProfile.EnemyMeleeContactRange,
                    $"{prefabPath} contactDamageRange",
                    issues);
                CheckSerializedFloat(
                    serializedController,
                    "attackFacingRefreshDegrees",
                    ranged ? 0f : Ee5SliceProfile.EnemyMeleeAttackFacingRefreshDegrees,
                    $"{prefabPath} attackFacingRefreshDegrees",
                    issues);
                CheckSerializedBool(
                    serializedController,
                    "orbitWhileAttacking",
                    false,
                    $"{prefabPath} orbitWhileAttacking",
                    issues);
            }

            if (ranged)
            {
                EnemyWeapon weapon = prefab.GetComponent<EnemyWeapon>();
                if (!weapon)
                {
                    issues.Add($"{prefabPath} has no EnemyWeapon");
                }
                else
                {
                    SerializedObject serializedWeapon = new SerializedObject(weapon);
                    CheckSerializedBool(
                        serializedWeapon,
                        "enforceEe5Profile",
                        true,
                        $"{prefabPath} enforceEe5Profile",
                        issues);
                    float cooldown = serializedWeapon.FindProperty("fireCooldown").floatValue;
                    float projectileSpeed = serializedWeapon.FindProperty("projectileSpeed").floatValue;
                    float projectileLifetime = serializedWeapon.FindProperty("projectileLifetime").floatValue;
                    float projectileKnockback = serializedWeapon.FindProperty("projectileKnockback").floatValue;
                    if (!Mathf.Approximately(cooldown, Ee5SliceProfile.EnemyGunnerFireCooldown))
                        issues.Add($"{prefabPath} fireCooldown={cooldown}");
                    if (!Mathf.Approximately(projectileSpeed, Ee5SliceProfile.EnemyGunnerProjectileSpeed))
                        issues.Add($"{prefabPath} projectileSpeed={projectileSpeed}");
                    if (!Mathf.Approximately(projectileLifetime, Ee5SliceProfile.EnemyGunnerProjectileLifetime))
                        issues.Add($"{prefabPath} projectileLifetime={projectileLifetime}");
                    if (!Mathf.Approximately(projectileKnockback, Ee5SliceProfile.EnemyGunnerProjectileKnockback))
                        issues.Add($"{prefabPath} projectileKnockback={projectileKnockback}");
                    SerializedProperty mirrorFirePoint =
                        serializedWeapon.FindProperty("mirrorFirePointYWithUprightFlip");
                    if (mirrorFirePoint == null
                        || mirrorFirePoint.boolValue != Ee5SliceProfile.EnemyGunnerMirrorFirePointYWithUprightFlip)
                    {
                        issues.Add($"{prefabPath} mirrorFirePointYWithUprightFlip is not EE5-compatible");
                    }
                    SerializedProperty firePoint = serializedWeapon.FindProperty("firePoint");
                    Transform firePointTransform = firePoint != null
                        ? firePoint.objectReferenceValue as Transform
                        : null;
                    if (!firePointTransform
                        || Vector3.Distance(
                            firePointTransform.localPosition,
                            Ee5SliceProfile.EnemyGunnerFirePointLocalPosition) > 0.001f)
                    {
                        issues.Add($"{prefabPath} fire point pose is not EE5-compatible");
                    }
                    SerializedProperty requiresRange =
                        serializedWeapon.FindProperty("requireTargetWithinAttackRange");
                    SerializedProperty requiresLineOfSight =
                        serializedWeapon.FindProperty("requireLineOfSightToFire");
                    SerializedProperty drawTelegraph =
                        serializedWeapon.FindProperty("drawAimTelegraph");
                    if (requiresRange == null
                        || requiresRange.boolValue != Ee5SliceProfile.EnemyGunnerRequiresAttackRange)
                    {
                        issues.Add($"{prefabPath} requireTargetWithinAttackRange is not EE5-compatible");
                    }
                    if (requiresLineOfSight == null
                        || requiresLineOfSight.boolValue != Ee5SliceProfile.EnemyGunnerRequiresLineOfSightToFire)
                    {
                        issues.Add($"{prefabPath} requireLineOfSightToFire is not EE5-compatible");
                    }
                    if (drawTelegraph == null
                        || drawTelegraph.boolValue != Ee5SliceProfile.EnemyGunnerDrawAimTelegraph)
                    {
                        issues.Add($"{prefabPath} drawAimTelegraph is not EE5-compatible");
                    }
                }
            }

            EnemyContactDamage contactDamage = prefab.GetComponent<EnemyContactDamage>();
            if (!contactDamage)
            {
                issues.Add($"{prefabPath} has no EnemyContactDamage");
            }
            else
            {
                SerializedObject serializedContact = new SerializedObject(contactDamage);
                float damage = serializedContact.FindProperty("damage").floatValue;
                float cooldown = serializedContact.FindProperty("cooldown").floatValue;
                float knockback = serializedContact.FindProperty("knockback").floatValue;
                if (!Mathf.Approximately(damage, Ee5SliceProfile.EnemyContactDamage))
                    issues.Add($"{prefabPath} contact damage={damage}");
                if (!Mathf.Approximately(cooldown, Ee5SliceProfile.EnemyContactCooldown))
                    issues.Add($"{prefabPath} contact cooldown={cooldown}");
                if (!Mathf.Approximately(knockback, Ee5SliceProfile.EnemyContactKnockback))
                    issues.Add($"{prefabPath} contact knockback={knockback}");
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
                bool invertsIntro = serializedPresentation.FindProperty("invertDormantSpriteX").boolValue;
                bool forwardNegativeX = serializedPresentation.FindProperty("forwardIsLocalNegativeX").boolValue;
                bool restoresFacing = serializedPresentation.FindProperty("restoreFacingAfterWake").boolValue;
                bool expectedDormantFacing = ranged || Ee5SliceProfile.EnemyMeleeFacesDormantTarget;
                bool expectedIntroMirror = !ranged && Ee5SliceProfile.EnemyMeleeInvertsSpriteDuringIntro;
                if (facesTarget != expectedDormantFacing)
                    issues.Add($"{prefabPath} faceDormantTowardTarget={facesTarget}");
                if (invertsIntro != expectedIntroMirror)
                    issues.Add($"{prefabPath} invertDormantSpriteX={invertsIntro}");
                if (forwardNegativeX != ranged || !restoresFacing)
                    issues.Add($"{prefabPath} dormant facing basis is incomplete");
                SerializedProperty pingPongDormant =
                    serializedPresentation.FindProperty("pingPongDormantAnimation");
                SerializedProperty randomizeDormant =
                    serializedPresentation.FindProperty("randomizeDormantStartFrame");
                if (pingPongDormant == null || !pingPongDormant.boolValue)
                    issues.Add($"{prefabPath} pingPongDormantAnimation=false");
                if (randomizeDormant == null || !randomizeDormant.boolValue)
                    issues.Add($"{prefabPath} randomizeDormantStartFrame=false");
                Sprite[] expectedDormantSprites = LoadSprites(
                    ranged ? EnemyIdleSpritePath : MeleeIdleSpritePath);
                Sprite[] expectedActiveSprites = LoadSprites(
                    ranged ? EnemySpritePath : MeleeSpritePath);
                Sprite[] expectedWakeSprites = LoadSprites(
                    ranged ? EnemyDefeatSpritePath : MeleeDefeatSpritePath);
                CheckSpriteArray(
                    serializedPresentation,
                    "dormantSprites",
                    expectedDormantSprites,
                    $"{prefabPath} dormantSprites",
                    issues);
                CheckSpriteArray(
                    serializedPresentation,
                    "alertSprites",
                    expectedWakeSprites,
                    $"{prefabPath} alertSprites",
                    issues);
                CheckSpriteArray(
                    serializedPresentation,
                    "activeSprites",
                    expectedActiveSprites,
                    $"{prefabPath} activeSprites",
                    issues);
                CheckSpriteArray(
                    serializedPresentation,
                    "defeatedSprites",
                    expectedWakeSprites,
                    $"{prefabPath} defeatedSprites",
                    issues);
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

        static void CheckSerializedFloat(
            SerializedObject serialized,
            string propertyName,
            float expected,
            string label,
            List<string> issues)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                issues.Add($"{label} property missing");
                return;
            }

            if (!Mathf.Approximately(property.floatValue, expected))
                issues.Add($"{label}={property.floatValue:0.###} (expected {expected:0.###})");
        }

        static void CheckSerializedFloat(
            SerializedProperty parent,
            string propertyName,
            float expected,
            string label,
            List<string> issues)
        {
            SerializedProperty property = parent?.FindPropertyRelative(propertyName);
            if (property == null)
            {
                issues.Add($"{label} property missing");
                return;
            }

            if (!Mathf.Approximately(property.floatValue, expected))
                issues.Add($"{label}={property.floatValue:0.###} (expected {expected:0.###})");
        }

        static void CheckSerializedBool(
            SerializedObject serialized,
            string propertyName,
            bool expected,
            string label,
            List<string> issues)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                issues.Add($"{label} property missing");
                return;
            }

            if (property.boolValue != expected)
                issues.Add($"{label}={property.boolValue} (expected {expected})");
        }

        static void CheckObjectReference(
            SerializedObject serialized,
            string propertyName,
            string label,
            List<string> issues)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                issues.Add($"{label} property missing");
                return;
            }

            if (!property.objectReferenceValue)
                issues.Add($"{label} reference missing");
        }

        static void CheckSceneObjectPosition(
            GameObject sceneObject,
            Vector2 expected,
            string label,
            List<string> issues)
        {
            if (!sceneObject)
            {
                issues.Add($"{label} missing");
                return;
            }

            Vector2 actual = sceneObject.transform.position;
            if (Vector2.Distance(actual, expected) > 0.01f)
            {
                issues.Add(
                    $"{label} position=({actual.x:0.###},{actual.y:0.###}) "
                    + $"(expected {expected.x:0.###},{expected.y:0.###})");
            }
        }

        static bool SetBool(SerializedObject serialized, string propertyName, bool value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.boolValue == value)
                return false;

            property.boolValue = value;
            return true;
        }

        /// <summary>
        /// Keeps the serialized builder contract aligned with PlayerFlightMotor's
        /// runtime EE5 profile. In particular, the optional neutral upright assist
        /// must not remain hidden in a generated scene when the gold profile disables it.
        /// </summary>
        static bool ApplyEe5PlayerMotorProfile(PlayerFlightMotor motor)
        {
            if (!motor)
                return false;

            SerializedObject serializedMotor = new SerializedObject(motor);
            bool changed = false;
            changed |= SetBool(serializedMotor, "enforceEe5Profile", true);
            changed |= SetFloat(serializedMotor, "thrustForce", Ee5SliceProfile.ThrustForce);
            changed |= SetFloat(serializedMotor, "rotationTorque", Ee5SliceProfile.RotationTorque);
            changed |= SetBool(serializedMotor, "rotationAddsThrust", true);
            changed |= SetFloat(
                serializedMotor,
                "rotationBoostMultiplier",
                Ee5SliceProfile.RotationBoostMultiplier);
            changed |= SetFloat(serializedMotor, "stabilizationSpeed", Ee5SliceProfile.StabilizationSpeed);
            changed |= SetFloat(serializedMotor, "angularDamping", Ee5SliceProfile.FlightAngularDamping);
            changed |= SetFloat(serializedMotor, "stabilizationAngle", 0f);
            changed |= SetBool(
                serializedMotor,
                "uprightAssistEnabled",
                Ee5SliceProfile.UprightAssistEnabled);
            changed |= SetFloat(
                serializedMotor,
                "uprightAssistWindow",
                Ee5SliceProfile.UprightAssistWindow);
            changed |= SetFloat(
                serializedMotor,
                "uprightAssistSpeed",
                Ee5SliceProfile.UprightAssistSpeed);
            changed |= SetFloat(
                serializedMotor,
                "uprightAssistAngularBrake",
                Ee5SliceProfile.UprightAssistAngularBrake);
            changed |= SetFloat(
                serializedMotor,
                "uprightAssistMaxAngularSpeed",
                Ee5SliceProfile.UprightAssistMaxAngularSpeed);
            changed |= SetFloat(
                serializedMotor,
                "uprightAssistReleaseDelay",
                Ee5SliceProfile.UprightAssistReleaseDelay);
            changed |= SetBool(
                serializedMotor,
                "removeVelocityIntoColliders",
                Ee5SliceProfile.PlayerRemoveVelocityIntoColliders);

            SerializedProperty stopperTag = serializedMotor.FindProperty("stopperTag");
            if (stopperTag != null && stopperTag.stringValue != "StopperZone")
            {
                stopperTag.stringValue = "StopperZone";
                changed = true;
            }

            serializedMotor.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        static bool SetSpriteArrayIfDifferent(
            SerializedObject serialized,
            string propertyName,
            Sprite[] sprites)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                return false;

            sprites ??= System.Array.Empty<Sprite>();
            bool changed = property.arraySize != sprites.Length;
            if (!changed)
            {
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (property.GetArrayElementAtIndex(i).objectReferenceValue == sprites[i])
                        continue;

                    changed = true;
                    break;
                }
            }

            if (!changed)
                return false;

            property.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            return true;
        }

        static void CheckSpriteArray(
            SerializedObject serialized,
            string propertyName,
            Sprite[] expected,
            string label,
            List<string> issues)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            expected ??= System.Array.Empty<Sprite>();
            if (property == null || property.arraySize != expected.Length)
            {
                issues.Add(
                    $"{label} has {(property != null ? property.arraySize : 0)} frames "
                    + $"(expected {expected.Length})");
                return;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (property.GetArrayElementAtIndex(i).objectReferenceValue == expected[i])
                    continue;

                issues.Add($"{label} frame {i} is wired to the wrong sprite");
                return;
            }
        }

        static bool SetEnum(SerializedObject serialized, string propertyName, System.Enum value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                return false;

            int enumValue = System.Convert.ToInt32(value);
            if (property.enumValueIndex == enumValue)
                return false;

            property.enumValueIndex = enumValue;
            return true;
        }

        static int RemoveLegacyObjectivePlaceholders()
        {
            int removed = 0;
            EnergyKey energyKey = UnityEngine.Object.FindFirstObjectByType<EnergyKey>();
            Transform keyVisual = energyKey
                ? energyKey.transform.Find("Key Visual")
                : null;
            if (keyVisual
                && keyVisual.GetComponent<SpriteRenderer>()
                && RemoveGeneratedSquareOutline(keyVisual, 0.25f))
            {
                removed++;
            }

            LevelExit levelExit = UnityEngine.Object.FindFirstObjectByType<LevelExit>();
            if (levelExit
                && levelExit.GetComponent<ExtractionPortalPresentation>()
                && RemoveGeneratedSquareOutline(levelExit.transform, 0.45f))
            {
                removed++;
            }

            if (removed > 0)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            return removed;
        }

        static bool HasLegacyObjectivePlaceholders()
        {
            EnergyKey energyKey = UnityEngine.Object.FindFirstObjectByType<EnergyKey>();
            Transform keyVisual = energyKey
                ? energyKey.transform.Find("Key Visual")
                : null;
            if (keyVisual
                && keyVisual.GetComponent<SpriteRenderer>()
                && IsGeneratedSquareOutline(keyVisual.GetComponent<LineRenderer>(), 0.25f))
            {
                return true;
            }

            LevelExit levelExit = UnityEngine.Object.FindFirstObjectByType<LevelExit>();
            return levelExit
                && levelExit.GetComponent<ExtractionPortalPresentation>()
                && IsGeneratedSquareOutline(levelExit.GetComponent<LineRenderer>(), 0.45f);
        }

        static bool RemoveGeneratedSquareOutline(Transform parent, float halfExtent)
        {
            LineRenderer outline = parent.GetComponent<LineRenderer>();
            if (!IsGeneratedSquareOutline(outline, halfExtent))
                return false;

            Undo.DestroyObjectImmediate(outline);
            return true;
        }

        static bool IsGeneratedSquareOutline(LineRenderer outline, float halfExtent)
        {
            if (!outline || outline.positionCount != 4)
                return false;

            const float tolerance = 0.02f;
            for (int i = 0; i < outline.positionCount; i++)
            {
                Vector3 position = outline.GetPosition(i);
                if (Mathf.Abs(Mathf.Abs(position.x) - halfExtent) > tolerance
                    || Mathf.Abs(Mathf.Abs(position.y) - halfExtent) > tolerance)
                {
                    return false;
                }
            }

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
                CheckSceneObjectPosition(
                    player,
                    Ee5SliceProfile.VerticalSlicePlayerSpawn,
                    "Player Craft",
                    issues);
            }
            if (!UnityEngine.Object.FindFirstObjectByType<GameStateMachine>())
                issues.Add("Game State");
            else
            {
                GameStateMachine gameState = UnityEngine.Object.FindFirstObjectByType<GameStateMachine>();
                SerializedObject serializedGameState = new SerializedObject(gameState);
                SerializedProperty initialState = serializedGameState.FindProperty("initialState");
                if (initialState == null || initialState.enumValueIndex != (int)GameState.Playing)
                    issues.Add("Game State initial state is not Playing");
                CheckSerializedFloat(
                    serializedGameState,
                    "enemyDefeatTimeScale",
                    Ee5SliceProfile.EnemyDefeatTimeScale,
                    "Game State defeat time scale",
                    issues);
                CheckSerializedFloat(
                    serializedGameState,
                    "enemyDefeatSlowdownDuration",
                    Ee5SliceProfile.EnemyDefeatSlowdownDuration,
                    "Game State defeat slowdown duration",
                    issues);
            }

            Camera sceneCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (!sceneCamera)
            {
                issues.Add("Main Camera");
            }
            else
            {
                if (!sceneCamera.orthographic
                    || !Mathf.Approximately(
                        sceneCamera.orthographicSize,
                        Ee5SliceProfile.CameraOrthographicSize))
                {
                    issues.Add(
                        $"Main Camera orthographic size={sceneCamera.orthographicSize} "
                        + $"(expected {Ee5SliceProfile.CameraOrthographicSize})");
                }

                PlayerCameraFollow cameraFollow = sceneCamera.GetComponent<PlayerCameraFollow>();
                if (!cameraFollow)
                {
                    issues.Add("Main Camera PlayerCameraFollow");
                }
                else
                {
                    if (!sceneCamera.GetComponent<AudioListener>())
                        issues.Add("Main Camera AudioListener");

                    SerializedObject serializedFollow = new SerializedObject(cameraFollow);
                    CheckObjectReference(
                        serializedFollow,
                        "target",
                        "Main Camera player target",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "speedZoomStart",
                        Ee5SliceProfile.CameraSpeedZoomStart,
                        "Camera speed zoom start",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "flipZoomOut",
                        Ee5SliceProfile.CameraFlipZoomOut,
                        "Camera flip zoom out",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "flipZoomDuration",
                        Ee5SliceProfile.CameraFlipZoomDuration,
                        "Camera flip zoom duration",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "wallSlamMinSpeed",
                        Ee5SliceProfile.CameraWallSlamMinSpeed,
                        "Camera wall slam minimum speed",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "wallSlamMaxSpeed",
                        Ee5SliceProfile.CameraWallSlamMaxSpeed,
                        "Camera wall slam maximum speed",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "wallSlamShakeStrength",
                        Ee5SliceProfile.CameraWallSlamShakeStrength,
                        "Camera wall slam shake strength",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "wallSlamShakeDuration",
                        Ee5SliceProfile.CameraWallSlamShakeDuration,
                        "Camera wall slam shake duration",
                        issues);
                    CheckSerializedFloat(
                        serializedFollow,
                        "wallSlamCooldown",
                        Ee5SliceProfile.CameraWallSlamCooldown,
                        "Camera wall slam cooldown",
                        issues);

                    SerializedProperty parallaxLayers = serializedFollow.FindProperty("parallaxLayers");
                    if (parallaxLayers == null || parallaxLayers.arraySize != 2)
                    {
                        issues.Add(
                            $"Camera parallax layer count={(parallaxLayers != null ? parallaxLayers.arraySize : 0)} "
                            + "(expected 2)");
                    }
                    else
                    {
                        CheckSerializedFloat(
                            parallaxLayers.GetArrayElementAtIndex(0),
                            "strength",
                            Ee5SliceProfile.CameraFarParallaxStrength,
                            "Camera far parallax strength",
                            issues);
                        CheckSerializedFloat(
                            parallaxLayers.GetArrayElementAtIndex(1),
                            "strength",
                            Ee5SliceProfile.CameraMidParallaxStrength,
                            "Camera starfield parallax strength",
                            issues);
                    }
                }
            }

            GameObject nebula = GameObject.Find("Nebula Backdrop - far parallax order -120");
            Sprite expectedNebula = LoadFirstSprite(NebulaSpritePath, LegacyStarfieldSpritePath);
            SpriteRenderer nebulaRenderer = nebula ? nebula.GetComponent<SpriteRenderer>() : null;
            if (!nebulaRenderer || nebulaRenderer.sprite != expectedNebula)
                issues.Add("Nebula backdrop sprite");

            StarfieldGridGenerator starField =
                UnityEngine.Object.FindFirstObjectByType<StarfieldGridGenerator>();
            if (!starField)
            {
                issues.Add("Star Field Generator");
            }
            else
            {
                SerializedObject serializedStarField = new SerializedObject(starField);
                Sprite expectedStarTile = LoadFirstSprite(
                    StarfieldSpritePath,
                    LegacyStarfieldSpritePath);
                SerializedProperty starTile = serializedStarField.FindProperty("starTileSprite");
                if (starTile == null || starTile.objectReferenceValue != expectedStarTile)
                    issues.Add("Star Field Generator tile sprite");
            }

            PlayerProjectile projectilePrefab =
                AssetDatabase.LoadAssetAtPath<PlayerProjectile>(ProjectilePrefabPath);
            if (!projectilePrefab)
            {
                issues.Add("PlayerProjectile prefab");
            }
            else
            {
                SerializedObject serializedProjectile = new SerializedObject(projectilePrefab);
                CheckSerializedFloat(
                    serializedProjectile,
                    "speed",
                    Ee5SliceProfile.PlayerProjectileSpeed,
                    "PlayerProjectile speed",
                    issues);
                CheckSerializedFloat(
                    serializedProjectile,
                    "lifetime",
                    Ee5SliceProfile.PlayerProjectileLifetime,
                    "PlayerProjectile lifetime",
                    issues);
                CheckSerializedFloat(
                    serializedProjectile,
                    "damage",
                    Ee5SliceProfile.PlayerProjectileDamage,
                    "PlayerProjectile damage",
                    issues);
                CheckSerializedFloat(
                    serializedProjectile,
                    "knockback",
                    Ee5SliceProfile.PlayerProjectileKnockback,
                    "PlayerProjectile knockback",
                    issues);
                CheckSerializedFloat(
                    serializedProjectile,
                    "nearMissDistance",
                    Ee5SliceProfile.ProjectileNearMissDistance,
                    "PlayerProjectile near miss distance",
                    issues);
                CheckSerializedBool(
                    serializedProjectile,
                    "destroyOnUnrecognizedCollision",
                    Ee5SliceProfile.PlayerProjectileDestroysOnUnknownCollision,
                    "PlayerProjectile unknown-collision destruction",
                    issues);
                Collider2D projectileCollider = projectilePrefab.GetComponent<Collider2D>();
                if (!projectileCollider || !projectileCollider.isTrigger)
                    issues.Add("PlayerProjectile collider must be a trigger");
            }

            EncounterController encounter = UnityEngine.Object.FindFirstObjectByType<EncounterController>();
            if (!encounter)
            {
                issues.Add("EncounterController");
            }
            else
            {
                SerializedObject serializedEncounter = new SerializedObject(encounter);
                SerializedProperty encounterEnemies = serializedEncounter.FindProperty("encounterEnemies");
                if (encounterEnemies == null || encounterEnemies.arraySize < 2)
                {
                    issues.Add("EncounterController enemy roster");
                }
                else
                {
                    for (int i = 0; i < encounterEnemies.arraySize; i++)
                    {
                        if (!encounterEnemies.GetArrayElementAtIndex(i).objectReferenceValue)
                            issues.Add($"EncounterController enemy reference {i}");
                    }
                }
            }

            CheckSceneObjectPosition(
                GameObject.Find("Purple Melee Hunter"),
                Ee5SliceProfile.VerticalSliceMeleeSpawn,
                "Purple Melee Hunter",
                issues);
            AddGeneratedEnemyContractIssues(
                GameObject.Find("Purple Melee Hunter"),
                false,
                "Purple Melee Hunter",
                issues);
            CheckSceneObjectPosition(
                GameObject.Find("White Gunner"),
                Ee5SliceProfile.VerticalSliceGunnerSpawn,
                "White Gunner",
                issues);
            AddGeneratedEnemyContractIssues(
                GameObject.Find("White Gunner"),
                true,
                "White Gunner",
                issues);
            GameObject energyGateObject = GameObject.Find("Energy Gate");
            CheckSceneObjectPosition(
                energyGateObject,
                Ee5SliceProfile.VerticalSliceGatePosition,
                "Energy Gate",
                issues);
            Transform gateArtwork = energyGateObject
                ? energyGateObject.transform.Find("Gate Artwork")
                : null;
            if (!gateArtwork)
            {
                issues.Add("Energy Gate artwork");
            }
            else
            {
                if (Vector3.Distance(
                        gateArtwork.localPosition,
                        Ee5SliceProfile.EnergyGateArtworkLocalPosition) > 0.02f)
                    issues.Add("Energy Gate artwork is not centered against its collider");
                if (Vector3.Distance(
                        gateArtwork.localScale,
                        Vector3.one * Ee5SliceProfile.EnergyGateArtworkScale) > 0.02f)
                    issues.Add("Energy Gate artwork scale does not match the authored collider");
            }
            CheckSceneObjectPosition(
                GameObject.Find("Level Exit"),
                Ee5SliceProfile.VerticalSliceExitPosition,
                "Level Exit",
                issues);

            SliceObjectiveDirector objectiveDirector =
                UnityEngine.Object.FindFirstObjectByType<SliceObjectiveDirector>();
            if (!objectiveDirector)
            {
                issues.Add("SliceObjectiveDirector");
            }
            else
            {
                SerializedObject serializedObjective = new SerializedObject(objectiveDirector);
                CheckObjectReference(serializedObjective, "encounter", "Objective encounter", issues);
                CheckObjectReference(serializedObjective, "energyKey", "Objective energy key", issues);
                CheckObjectReference(serializedObjective, "gate", "Objective gate", issues);
                CheckObjectReference(serializedObjective, "exit", "Objective exit", issues);
                CheckObjectReference(serializedObjective, "gameState", "Objective game state", issues);
            }
            EnergyKey key = UnityEngine.Object.FindFirstObjectByType<EnergyKey>();
            if (!key)
                issues.Add("EnergyKey");
            else
            {
                if (!key.EnemyTarget || !key.EnemyTarget.GetComponent<EnemyWeapon>())
                    issues.Add("EnergyKey carrier (ranged gunner)");

                Rigidbody2D keyBody = key.GetComponent<Rigidbody2D>();
                if (!keyBody)
                {
                    issues.Add("EnergyKey Rigidbody2D");
                }
                else
                {
                    if (keyBody.bodyType != RigidbodyType2D.Kinematic)
                        issues.Add("EnergyKey body type is not Kinematic");
                    if (keyBody.interpolation != RigidbodyInterpolation2D.Interpolate)
                        issues.Add("EnergyKey interpolation is not Interpolate");
                    if (keyBody.collisionDetectionMode != CollisionDetectionMode2D.Continuous)
                        issues.Add("EnergyKey collision detection is not Continuous");
                }

                SerializedObject serializedKey = new SerializedObject(key);
                CheckSerializedBool(
                    serializedKey,
                    "enforceEe5Profile",
                    true,
                    "EnergyKey enforceEe5Profile",
                    issues);
                CheckObjectReference(serializedKey, "requiredEncounter", "EnergyKey encounter", issues);
                CheckObjectReference(serializedKey, "enemyTarget", "EnergyKey carrier", issues);
                CheckObjectReference(serializedKey, "targetGate", "EnergyKey target gate", issues);
                CheckSerializedFloat(
                    serializedKey,
                    "enemyOrbitRadius",
                    Ee5SliceProfile.EnergyKeyEnemyOrbitRadius,
                    "EnergyKey enemy orbit radius",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "enemyOrbitSpeed",
                    Ee5SliceProfile.EnergyKeyEnemyOrbitSpeed,
                    "EnergyKey enemy orbit speed",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "enemyOrbitSharpness",
                    Ee5SliceProfile.EnergyKeyEnemyOrbitSharpness,
                    "EnergyKey enemy orbit sharpness",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "orbitRadiusX",
                    Ee5SliceProfile.EnergyKeyOrbitRadiusX,
                    "EnergyKey player orbit radius X",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "orbitRadiusY",
                    Ee5SliceProfile.EnergyKeyOrbitRadiusY,
                    "EnergyKey player orbit radius Y",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "orbitSpeed",
                    Ee5SliceProfile.EnergyKeyOrbitSpeed,
                    "EnergyKey player orbit speed",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "orbitSharpness",
                    Ee5SliceProfile.EnergyKeyOrbitSharpness,
                    "EnergyKey player orbit sharpness",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "radiusEase",
                    Ee5SliceProfile.EnergyKeyRadiusEase,
                    "EnergyKey player orbit radius ease",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "centerFollowSharpness",
                    Ee5SliceProfile.EnergyKeyCenterFollowSharpness,
                    "EnergyKey player orbit center follow",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "collectDistance",
                    Ee5SliceProfile.EnergyKeyCollectDistance,
                    "EnergyKey collect distance",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "gateUnlockRange",
                    Ee5SliceProfile.EnergyKeyGateUnlockRange,
                    "EnergyKey gate handoff range",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "playerFollowSharpness",
                    Ee5SliceProfile.EnergyKeyPlayerFollowSharpness,
                    "EnergyKey player follow sharpness",
                    issues);
                CheckSerializedFloat(
                    serializedKey,
                    "gateFlySpeed",
                    Ee5SliceProfile.EnergyKeyGateFlySpeed,
                    "EnergyKey gate flight speed",
                    issues);
            }

            EnergyGate gate = UnityEngine.Object.FindFirstObjectByType<EnergyGate>();
            if (!gate)
                issues.Add("EnergyGate");
            else
            {
                BoxCollider2D gateCollider = gate.GetComponent<BoxCollider2D>();
                if (!gateCollider)
                {
                    issues.Add("EnergyGate collider");
                }
                else if (Vector2.Distance(
                             gateCollider.size,
                             Ee5SliceProfile.VerticalSliceGateColliderSize) > 0.01f)
                {
                    issues.Add(
                        $"EnergyGate collider size=({gateCollider.size.x:0.###},{gateCollider.size.y:0.###}) "
                        + $"(expected {Ee5SliceProfile.VerticalSliceGateColliderSize.x:0.###},{Ee5SliceProfile.VerticalSliceGateColliderSize.y:0.###})");
                }

                Transform keyTarget = gate.transform.Find("Key Target");
                if (!keyTarget)
                {
                    issues.Add("EnergyGate key target transform");
                }
                else if (Vector2.Distance(
                             keyTarget.localPosition,
                             Ee5SliceProfile.VerticalSliceGateKeyTarget) > 0.01f)
                {
                    issues.Add(
                        $"EnergyGate key target=({keyTarget.localPosition.x:0.###},{keyTarget.localPosition.y:0.###}) "
                        + $"(expected {Ee5SliceProfile.VerticalSliceGateKeyTarget.x:0.###},{Ee5SliceProfile.VerticalSliceGateKeyTarget.y:0.###})");
                }

                SerializedObject serializedGate = new SerializedObject(gate);
                CheckSerializedBool(
                    serializedGate,
                    "enforceEe5Profile",
                    true,
                    "EnergyGate enforceEe5Profile",
                    issues);
                CheckObjectReference(serializedGate, "keyTarget", "EnergyGate key target", issues);
                CheckSerializedFloat(
                    serializedGate,
                    "liftDistance",
                    Ee5SliceProfile.EnergyGateLiftDistance,
                    "EnergyGate lift distance",
                    issues);
                CheckSerializedFloat(
                    serializedGate,
                    "liftSpeed",
                    Ee5SliceProfile.EnergyGateLiftSpeed,
                    "EnergyGate lift speed",
                    issues);
            }
            LevelExit levelExit = UnityEngine.Object.FindFirstObjectByType<LevelExit>();
            if (!levelExit)
            {
                issues.Add("LevelExit");
            }
            else
            {
                SerializedObject serializedExit = new SerializedObject(levelExit);
                CheckObjectReference(serializedExit, "encounter", "LevelExit encounter", issues);
                CheckObjectReference(serializedExit, "requiredGate", "LevelExit required gate", issues);
                CheckObjectReference(serializedExit, "gameState", "LevelExit game state", issues);
                Collider2D exitCollider = levelExit.GetComponent<Collider2D>();
                if (!exitCollider || !exitCollider.isTrigger)
                    issues.Add("LevelExit must be a trigger");
            }

            if (HasLegacyObjectivePlaceholders())
                issues.Add("legacy objective placeholder outline(s)");

            GameplayHud hud = UnityEngine.Object.FindFirstObjectByType<GameplayHud>();
            if (!hud)
            {
                issues.Add("GameplayHud");
            }
            else
            {
                // The opening objective banner is deliberately deferred until
                // Start. Keep its two authoritative sources serialized so a
                // hand-edited scene cannot silently fall back to guessed text.
                SerializedObject serializedHud = new SerializedObject(hud);
                CheckObjectReference(
                    serializedHud,
                    "objectiveDirector",
                    "HUD objective director",
                    issues);
                CheckObjectReference(
                    serializedHud,
                    "gameState",
                    "HUD game state",
                    issues);
                CheckObjectReference(
                    serializedHud,
                    "encounter",
                    "HUD encounter",
                    issues);
                CheckObjectReference(
                    serializedHud,
                    "energyKey",
                    "HUD energy key",
                    issues);
                CheckObjectReference(
                    serializedHud,
                    "exit",
                    "HUD exit",
                    issues);
            }
            GameObject stopper = GameObject.Find("Flight Stopper Zone");
            if (!stopper)
                issues.Add("Flight Stopper Zone");
            else
            {
                BoxCollider2D stopperCollider = stopper.GetComponent<BoxCollider2D>();
                if (!stopperCollider)
                    issues.Add("Flight Stopper Zone collider");
                else if (!stopperCollider.isTrigger)
                    issues.Add("Flight Stopper Zone must be a trigger");
                if (stopper.tag != "StopperZone")
                    issues.Add("Flight Stopper Zone tag");
            }

            EnemyController[] enemies = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            if (enemies == null || enemies.Length < 2)
                issues.Add($"Enemy roster ({enemies?.Length ?? 0}/2)");
            return issues;
        }

        static void AddGeneratedEnemyContractIssues(
            GameObject enemyObject,
            bool ranged,
            string label,
            List<string> issues)
        {
            if (!enemyObject)
            {
                issues.Add($"{label} object");
                return;
            }

            EnemyController controller = enemyObject.GetComponent<EnemyController>();
            if (!controller)
            {
                issues.Add($"{label} EnemyController");
            }
            else
            {
                SerializedObject serializedController = new SerializedObject(controller);
                bool forwardNegativeX = serializedController
                    .FindProperty("forwardIsLocalNegativeX")?.boolValue ?? false;
                if (forwardNegativeX != ranged)
                {
                    issues.Add(
                        $"{label} facing basis={(forwardNegativeX ? "negative X" : "positive X")} "
                        + $"(expected {(ranged ? "negative" : "positive")} X)");
                }

                float expectedRootScaleX = ranged
                    ? Ee5SliceProfile.EnemyGunnerRootScaleX
                    : Ee5SliceProfile.EnemyMeleeRootScaleX;
                if (!Mathf.Approximately(enemyObject.transform.localScale.x, expectedRootScaleX))
                {
                    issues.Add(
                        $"{label} root scale.x={enemyObject.transform.localScale.x:0.###} "
                        + $"(expected {expectedRootScaleX:0.###})");
                }

                CheckSerializedFloat(
                    serializedController,
                    "attackFacingRefreshDegrees",
                    ranged ? 0f : Ee5SliceProfile.EnemyMeleeAttackFacingRefreshDegrees,
                    $"{label} attack facing refresh",
                    issues);
            }

            EnemyWeapon weapon = enemyObject.GetComponent<EnemyWeapon>();
            if (ranged != (weapon != null))
            {
                issues.Add(
                    ranged
                        ? $"{label} EnemyWeapon is missing"
                        : $"{label} has EnemyWeapon but is the melee role");
            }
            else if (ranged)
            {
                SerializedObject serializedWeapon = new SerializedObject(weapon);
                CheckSerializedBool(
                    serializedWeapon,
                    "mirrorFirePointYWithUprightFlip",
                    Ee5SliceProfile.EnemyGunnerMirrorFirePointYWithUprightFlip,
                    $"{label} muzzle mirroring",
                    issues);
                Transform firePoint = weapon.FirePoint;
                if (!firePoint
                    || Vector3.Distance(
                        firePoint.localPosition,
                        Ee5SliceProfile.EnemyGunnerFirePointLocalPosition) > 0.001f)
                {
                    issues.Add($"{label} fire point pose");
                }
            }

            EnemyContactDamage contactDamage = enemyObject.GetComponent<EnemyContactDamage>();
            if (!contactDamage)
            {
                issues.Add($"{label} EnemyContactDamage");
            }
            else
            {
                SerializedObject serializedContact = new SerializedObject(contactDamage);
                CheckSerializedFloat(
                    serializedContact,
                    "damage",
                    Ee5SliceProfile.EnemyContactDamage,
                    $"{label} contact damage",
                    issues);
                CheckSerializedFloat(
                    serializedContact,
                    "cooldown",
                    Ee5SliceProfile.EnemyContactCooldown,
                    $"{label} contact cooldown",
                    issues);
                CheckSerializedFloat(
                    serializedContact,
                    "knockback",
                    Ee5SliceProfile.EnemyContactKnockback,
                    $"{label} contact knockback",
                    issues);
            }

            Sprite expectedActiveSprite = LoadFirstSprite(
                ranged ? EnemySpritePath : MeleeSpritePath);
            SpriteRenderer spriteRenderer = enemyObject.GetComponent<SpriteRenderer>();
            if (!spriteRenderer)
            {
                issues.Add($"{label} SpriteRenderer");
            }
            else if (spriteRenderer.sprite != expectedActiveSprite)
            {
                string actualPath = spriteRenderer.sprite
                    ? AssetDatabase.GetAssetPath(spriteRenderer.sprite)
                    : "<missing>";
                issues.Add(
                    $"{label} active sprite uses {actualPath} "
                    + $"(expected {(ranged ? EnemySpritePath : MeleeSpritePath)})");
            }

            EnemySpritePresentation presentation =
                enemyObject.GetComponent<EnemySpritePresentation>();
            if (!presentation)
            {
                issues.Add($"{label} EnemySpritePresentation");
                return;
            }

            SerializedObject serializedPresentation = new SerializedObject(presentation);
            CheckSpriteArray(
                serializedPresentation,
                "dormantSprites",
                LoadSprites(ranged ? EnemyIdleSpritePath : MeleeIdleSpritePath),
                $"{label} dormantSprites",
                issues);
            CheckSpriteArray(
                serializedPresentation,
                "alertSprites",
                LoadSprites(ranged ? EnemyDefeatSpritePath : MeleeDefeatSpritePath),
                $"{label} alertSprites",
                issues);
            CheckSpriteArray(
                serializedPresentation,
                "activeSprites",
                LoadSprites(ranged ? EnemySpritePath : MeleeSpritePath),
                $"{label} activeSprites",
                issues);
            CheckSpriteArray(
                serializedPresentation,
                "defeatedSprites",
                LoadSprites(ranged ? EnemyDefeatSpritePath : MeleeDefeatSpritePath),
                $"{label} defeatedSprites",
                issues);

            CheckSerializedBool(
                serializedPresentation,
                "faceDormantTowardTarget",
                ranged || Ee5SliceProfile.EnemyMeleeFacesDormantTarget,
                $"{label} dormant facing",
                issues);
            CheckSerializedBool(
                serializedPresentation,
                "invertDormantSpriteX",
                !ranged && Ee5SliceProfile.EnemyMeleeInvertsSpriteDuringIntro,
                $"{label} intro sprite mirror",
                issues);
            CheckSerializedBool(
                serializedPresentation,
                "forwardIsLocalNegativeX",
                ranged,
                $"{label} sprite facing basis",
                issues);
            CheckSerializedBool(
                serializedPresentation,
                "restoreFacingAfterWake",
                true,
                $"{label} wake facing restore",
                issues);
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
            if (!visual)
            {
                visual = new GameObject("Craft Visual").transform;
                visual.SetParent(root.transform, false);
                EditorUtility.SetDirty(visual.gameObject);
            }

            // Older FlightTest scenes predate the composed player visual and
            // contain only the decorative outline LineRenderer. Repair the
            // object in place so a user can recover that scene through the
            // menu without editing Unity YAML or rebuilding the whole room.
            SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
            if (!renderer)
            {
                renderer = visual.gameObject.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = 10;
            }

            PlayerFlightPresentation presentation = root.GetComponent<PlayerFlightPresentation>();
            if (!presentation)
                presentation = root.AddComponent<PlayerFlightPresentation>();

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

        static bool ApplyPlayerCraftPhysicsProfile(GameObject root)
        {
            if (!root)
                return false;

            bool changed = false;
            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            if (body)
            {
                if (body.bodyType != RigidbodyType2D.Dynamic)
                {
                    body.bodyType = RigidbodyType2D.Dynamic;
                    changed = true;
                }
                if (!Mathf.Approximately(body.mass, Ee5SliceProfile.PlayerMass))
                {
                    body.mass = Ee5SliceProfile.PlayerMass;
                    changed = true;
                }
                if (!Mathf.Approximately(body.gravityScale, Ee5SliceProfile.PlayerGravityScale))
                {
                    body.gravityScale = Ee5SliceProfile.PlayerGravityScale;
                    changed = true;
                }
                if (!Mathf.Approximately(body.linearDamping, Ee5SliceProfile.PlayerFlightLinearDamping))
                {
                    body.linearDamping = Ee5SliceProfile.PlayerFlightLinearDamping;
                    changed = true;
                }
                if (!Mathf.Approximately(body.angularDamping, Ee5SliceProfile.PlayerFlightAngularDamping))
                {
                    body.angularDamping = Ee5SliceProfile.PlayerFlightAngularDamping;
                    changed = true;
                }
                if (body.interpolation != RigidbodyInterpolation2D.Interpolate)
                {
                    body.interpolation = RigidbodyInterpolation2D.Interpolate;
                    changed = true;
                }
                if (body.collisionDetectionMode != CollisionDetectionMode2D.Continuous)
                {
                    body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                    changed = true;
                }
                if (!body.simulated)
                {
                    body.simulated = true;
                    changed = true;
                }

                if (changed)
                    EditorUtility.SetDirty(body);
            }

            PlayerFlightMotor motor = root.GetComponent<PlayerFlightMotor>();
            if (motor)
                changed |= ApplyEe5PlayerMotorProfile(motor);

            return changed;
        }

        static void AddPlayerCraftPhysicsIssues(
            GameObject root,
            string label,
            List<string> issues)
        {
            if (!root)
            {
                issues.Add($"{label}: missing Player Craft");
                return;
            }

            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            if (!body)
            {
                issues.Add($"{label}: Rigidbody2D missing");
            }
            else
            {
                if (body.bodyType != RigidbodyType2D.Dynamic)
                    issues.Add($"{label}: bodyType={body.bodyType} (expected Dynamic)");
                if (!body.simulated)
                    issues.Add($"{label}: Rigidbody2D.simulated=false (expected true)");
                if (body.constraints != RigidbodyConstraints2D.None)
                    issues.Add($"{label}: constraints={body.constraints} (expected None)");
                if (!Mathf.Approximately(body.mass, Ee5SliceProfile.PlayerMass))
                    issues.Add($"{label}: mass={body.mass} (expected {Ee5SliceProfile.PlayerMass})");
                if (!Mathf.Approximately(body.gravityScale, Ee5SliceProfile.PlayerGravityScale))
                    issues.Add($"{label}: gravityScale={body.gravityScale} (expected {Ee5SliceProfile.PlayerGravityScale})");
                if (!Mathf.Approximately(body.linearDamping, Ee5SliceProfile.PlayerFlightLinearDamping))
                    issues.Add($"{label}: linearDamping={body.linearDamping} (expected {Ee5SliceProfile.PlayerFlightLinearDamping})");
                if (!Mathf.Approximately(body.angularDamping, Ee5SliceProfile.PlayerFlightAngularDamping))
                    issues.Add($"{label}: angularDamping={body.angularDamping} (expected {Ee5SliceProfile.PlayerFlightAngularDamping})");
                if (body.interpolation != RigidbodyInterpolation2D.Interpolate)
                    issues.Add($"{label}: interpolation={body.interpolation} (expected Interpolate)");
                if (body.collisionDetectionMode != CollisionDetectionMode2D.Continuous)
                    issues.Add($"{label}: collisionDetection={body.collisionDetectionMode} (expected Continuous)");
            }

            PlayerFlightMotor motor = root.GetComponent<PlayerFlightMotor>();
            if (!motor)
            {
                issues.Add($"{label}: PlayerFlightMotor missing");
                return;
            }

            SerializedObject serializedMotor = new SerializedObject(motor);
            CheckSerializedBool(serializedMotor, "enforceEe5Profile", true, $"{label}: enforceEe5Profile", issues);
            CheckSerializedFloat(serializedMotor, "thrustForce", Ee5SliceProfile.ThrustForce, $"{label}: thrustForce", issues);
            CheckSerializedFloat(serializedMotor, "rotationTorque", Ee5SliceProfile.RotationTorque, $"{label}: rotationTorque", issues);
            CheckSerializedFloat(serializedMotor, "stabilizationSpeed", Ee5SliceProfile.StabilizationSpeed, $"{label}: stabilizationSpeed", issues);
            CheckSerializedFloat(serializedMotor, "angularDamping", Ee5SliceProfile.FlightAngularDamping, $"{label}: flight angular damping", issues);
            CheckSerializedBool(
                serializedMotor,
                "uprightAssistEnabled",
                Ee5SliceProfile.UprightAssistEnabled,
                $"{label}: uprightAssistEnabled",
                issues);
            CheckSerializedFloat(
                serializedMotor,
                "uprightAssistWindow",
                Ee5SliceProfile.UprightAssistWindow,
                $"{label}: upright assist window",
                issues);
            CheckSerializedFloat(
                serializedMotor,
                "uprightAssistSpeed",
                Ee5SliceProfile.UprightAssistSpeed,
                $"{label}: upright assist speed",
                issues);
            CheckSerializedFloat(
                serializedMotor,
                "uprightAssistAngularBrake",
                Ee5SliceProfile.UprightAssistAngularBrake,
                $"{label}: upright assist angular brake",
                issues);
            CheckSerializedFloat(
                serializedMotor,
                "uprightAssistMaxAngularSpeed",
                Ee5SliceProfile.UprightAssistMaxAngularSpeed,
                $"{label}: upright assist max angular speed",
                issues);
            CheckSerializedBool(
                serializedMotor,
                "removeVelocityIntoColliders",
                Ee5SliceProfile.PlayerRemoveVelocityIntoColliders,
                $"{label}: removeVelocityIntoColliders",
                issues);
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

            Transform nebula = CreateBackdropLayer(
                backdropRoot.transform,
                "Nebula Backdrop - far parallax order -120",
                Vector3.one * 4.7f,
                new Color(0.32972205f, 0f, 0.4627451f, 0.9f),
                -120,
                NebulaSpritePath);

            GameObject starFieldObject = new GameObject("Star Field Generator");
            starFieldObject.transform.SetParent(backdropRoot.transform, false);
            starFieldObject.transform.localPosition = new Vector3(0.01f, -0.01f, 0f);
            StarfieldGridGenerator starField = starFieldObject.AddComponent<StarfieldGridGenerator>();
            SerializedObject serializedStarField = new SerializedObject(starField);
            serializedStarField.FindProperty("starTileSprite").objectReferenceValue =
                LoadFirstSprite(StarfieldSpritePath, LegacyStarfieldSpritePath);
            serializedStarField.FindProperty("columns").intValue = 10;
            serializedStarField.FindProperty("rows").intValue = 12;
            serializedStarField.FindProperty("seamOverlap").floatValue = 0.02f;
            serializedStarField.FindProperty("seed").intValue = 32090;
            serializedStarField.FindProperty("staggerRows").boolValue = true;
            serializedStarField.FindProperty("rowStaggerAmount").floatValue = 0.5f;
            serializedStarField.FindProperty("randomFlips").boolValue = true;
            serializedStarField.FindProperty("randomQuarterRotations").boolValue = true;
            serializedStarField.FindProperty("brightnessJitter").floatValue = 0.06f;
            serializedStarField.FindProperty("addExtraStars").boolValue = true;
            serializedStarField.FindProperty("extraStarCount").intValue = 350;
            serializedStarField.FindProperty("extraStarSizeRange").vector2Value = new Vector2(0.015f, 0.055f);
            serializedStarField.FindProperty("yellowStarChance").floatValue = 0.18f;
            serializedStarField.FindProperty("blueStarChance").floatValue = 0.04f;
            serializedStarField.FindProperty("generatedRootName").stringValue = "_Generated Starfield";
            serializedStarField.FindProperty("sortingLayerName").stringValue = "Default";
            serializedStarField.FindProperty("sortingOrder").intValue = -110;
            serializedStarField.FindProperty("generateOnStart").boolValue = true;
            serializedStarField.ApplyModifiedPropertiesWithoutUndo();

            return new[] { nebula, starFieldObject.transform };
        }

        static Transform CreateBackdropLayer(
            Transform parent,
            string objectName,
            Vector3 scale,
            Color color,
            int sortingOrder,
            string spritePath)
        {
            GameObject layer = new GameObject(objectName);
            layer.transform.SetParent(parent, false);
            layer.transform.localScale = scale;

            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadFirstSprite(spritePath, LegacyStarfieldSpritePath);
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
            serialized.FindProperty("enemyDefeatTimeScale").floatValue =
                Ee5SliceProfile.EnemyDefeatTimeScale;
            serialized.FindProperty("enemyDefeatSlowdownDuration").floatValue =
                Ee5SliceProfile.EnemyDefeatSlowdownDuration;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return stateMachine;
        }

        /// <summary>
        /// Instantiates the authored player prefab for the safe rebuild path.
        /// Scene wiring is applied to the instance only, so inspector tuning
        /// such as Rigidbody damping survives into the playable checkpoint.
        /// </summary>
        static PlayerCharacter CreatePrefabBackedPlayer(
            InputActionAsset inputAsset,
            PlayerProjectile projectilePrefab,
            GameStateMachine gameState)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (!prefab)
                throw new System.InvalidOperationException(
                    $"Could not preserve prefabs because {PlayerPrefabPath} is missing.");

            GameObject player = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (!player)
                throw new System.InvalidOperationException(
                    $"Could not instantiate the authored player prefab at {PlayerPrefabPath}.");

            player.name = "Player Craft";
            player.tag = "Player";
            player.transform.position = Ee5SliceProfile.VerticalSlicePlayerSpawn;

            PlayerCharacter character = RequireComponent<PlayerCharacter>(player, PlayerPrefabPath);
            PlayerFlightMotor motor = RequireComponent<PlayerFlightMotor>(player, PlayerPrefabPath);
            ApplyEe5PlayerMotorProfile(motor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(motor);
            PlayerFlightInput input = RequireComponent<PlayerFlightInput>(player, PlayerPrefabPath);
            input.ConfigureInputAsset(inputAsset);
            PrefabUtility.RecordPrefabInstancePropertyModifications(input);
            SetSerializedObjectReference(input, "gameState", gameState);
            SetSerializedFloat(input, "turnDeadzone", Ee5SliceProfile.PlayerTurnDeadzone);
            SetSerializedFloat(input, "thrustDeadzone", Ee5SliceProfile.PlayerThrustDeadzone);

            PlayerWeaponInput weaponInput = RequireComponent<PlayerWeaponInput>(player, PlayerPrefabPath);
            weaponInput.ConfigureInputAsset(inputAsset);
            PrefabUtility.RecordPrefabInstancePropertyModifications(weaponInput);
            SetSerializedObjectReference(weaponInput, "gameState", gameState);

            PlayerWeapon weapon = RequireComponent<PlayerWeapon>(player, PlayerPrefabPath);
            SetSerializedObjectReference(weapon, "gameState", gameState);
            SetSerializedObjectReference(weapon, "projectilePrefab", projectilePrefab);

            // Do not normalize the Rigidbody damping here. It is deliberately
            // authored on PlayerCraft.prefab and the runtime motor now honors
            // that value instead of replacing it during Awake.
            return character;
        }

        /// <summary>
        /// Instantiates an authored enemy prefab without running the repair
        /// path. The controller still owns runtime role recovery, while the
        /// scene supplies only references that cannot live inside a prefab.
        /// </summary>
        static EnemyController CreatePrefabBackedEnemy(
            GameStateMachine gameState,
            PlayerProjectile projectilePrefab,
            string objectName,
            Vector2 position,
            string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
                throw new System.InvalidOperationException(
                    $"Could not preserve prefabs because {prefabPath} is missing.");

            GameObject enemy = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (!enemy)
                throw new System.InvalidOperationException(
                    $"Could not instantiate the authored enemy prefab at {prefabPath}.");

            enemy.name = objectName;
            enemy.transform.position = position;

            EnemyController controller = RequireComponent<EnemyController>(enemy, prefabPath);
            SetSerializedObjectReference(controller, "gameState", gameState);

            EnemyWeapon weapon = enemy.GetComponent<EnemyWeapon>();
            if (weapon)
            {
                SetSerializedObjectReference(weapon, "gameState", gameState);
                SetSerializedObjectReference(weapon, "projectilePrefab", projectilePrefab);

                // Keep the generated scene instance visually faithful even
                // before Play invokes EnemyWeapon's runtime self-heal. This
                // is an instance override; the preserved prefab asset remains
                // untouched.
                SerializedObject serializedWeapon = new SerializedObject(weapon);
                SetBool(
                    serializedWeapon,
                    "mirrorFirePointYWithUprightFlip",
                    Ee5SliceProfile.EnemyGunnerMirrorFirePointYWithUprightFlip);
                serializedWeapon.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(weapon);

                Transform firePoint = weapon.FirePoint;
                if (firePoint)
                {
                    firePoint.localPosition = Ee5SliceProfile.EnemyGunnerFirePointLocalPosition;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(firePoint);
                }
            }

            // Preserve Prefabs means preserve their gameplay tuning, not
            // preserve a stale partial sprite array. Apply the exact EE5 intro
            // contract as a scene-instance override so the rebuild self-heals
            // the visual without rewriting the user's authored prefab asset.
            if (RepairEnemyIntroSpritePresentation(enemy, weapon != null))
            {
                EnemySpritePresentation presentation =
                    enemy.GetComponent<EnemySpritePresentation>();
                if (presentation)
                    PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);

                SpriteRenderer spriteRenderer = enemy.GetComponent<SpriteRenderer>();
                if (spriteRenderer)
                    PrefabUtility.RecordPrefabInstancePropertyModifications(spriteRenderer);
                EditorUtility.SetDirty(enemy);
            }

            return controller;
        }

        static T RequireComponent<T>(GameObject root, string assetPath)
            where T : Component
        {
            T component = root.GetComponent<T>();
            if (component)
                return component;

            throw new System.InvalidOperationException(
                $"{assetPath} is missing required {typeof(T).Name}.");
        }

        static void SetSerializedObjectReference(
            Component component,
            string propertyName,
            UnityEngine.Object value)
        {
            if (!component)
                return;

            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                return;

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        static void RepairSceneReference(
            Component component,
            string propertyName,
            UnityEngine.Object value)
        {
            if (!component)
                return;

            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                return;

            Undo.RecordObject(component, "Repair FlightTest objective contract");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }

        static void RepairEncounterRoster(
            EncounterController encounter,
            EnemyController melee,
            EnemyController carrier)
        {
            SerializedObject serialized = new SerializedObject(encounter);
            SerializedProperty roster = serialized.FindProperty("encounterEnemies");
            if (roster == null)
                return;

            Undo.RecordObject(encounter, "Repair FlightTest encounter roster");
            roster.arraySize = 2;
            roster.GetArrayElementAtIndex(0).objectReferenceValue = melee;
            roster.GetArrayElementAtIndex(1).objectReferenceValue = carrier;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(encounter);
        }

        static void SetSerializedFloat(Component component, string propertyName, float value)
        {
            if (!component)
                return;

            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                return;

            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        static PlayerCharacter CreatePlayer(
            InputActionAsset inputAsset,
            PlayerProjectile projectilePrefab,
            GameStateMachine gameState,
            bool preservePrefabs)
        {
            GameObject player = new GameObject("Player Craft");
            player.tag = "Player";
            player.transform.position = Ee5SliceProfile.VerticalSlicePlayerSpawn;

            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.mass = Ee5SliceProfile.PlayerMass;
            body.gravityScale = Ee5SliceProfile.PlayerGravityScale;
            body.linearDamping = Ee5SliceProfile.PlayerFlightLinearDamping;
            body.angularDamping = Ee5SliceProfile.PlayerFlightAngularDamping;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D playerCollider = player.AddComponent<CircleCollider2D>();
            playerCollider.radius = 0.112f;
            playerCollider.offset = new Vector2(-0.004f, 0f);
            PlayerCharacter character = player.AddComponent<PlayerCharacter>();
            PlayerFlightMotor motor = player.GetComponent<PlayerFlightMotor>();
            ApplyEe5PlayerMotorProfile(motor);
            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            SerializedObject serializedPlayerHealth = new SerializedObject(playerHealth);
            serializedPlayerHealth.FindProperty("maxHealth").floatValue = Ee5SliceProfile.PlayerMaxHealth;
            serializedPlayerHealth.FindProperty("invulnerabilityDuration").floatValue =
                Ee5SliceProfile.PlayerInvulnerabilityDuration;
            serializedPlayerHealth.ApplyModifiedPropertiesWithoutUndo();
            PlayerFlightInput input = player.GetComponent<PlayerFlightInput>();
            if (inputAsset)
                input.ConfigureInputAsset(inputAsset);
            SerializedObject serializedInput = new SerializedObject(input);
            serializedInput.FindProperty("gameState").objectReferenceValue = gameState;
            serializedInput.FindProperty("includeEe5KeyboardFallback").boolValue = true;
            serializedInput.FindProperty("turnDeadzone").floatValue = Ee5SliceProfile.PlayerTurnDeadzone;
            serializedInput.FindProperty("thrustDeadzone").floatValue = Ee5SliceProfile.PlayerThrustDeadzone;
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
            serializedPresentation.FindProperty("enforceEe5Profile").boolValue = true;
            serializedPresentation.FindProperty("boostedExhaustLengthMultiplier").floatValue =
                Ee5SliceProfile.PlayerBoostedExhaustLengthMultiplier;
            serializedPresentation.FindProperty("boostedExhaustWidthMultiplier").floatValue =
                Ee5SliceProfile.PlayerBoostedExhaustWidthMultiplier;
            serializedPresentation.FindProperty("boostedExhaustYScale").floatValue =
                Ee5SliceProfile.PlayerBoostedExhaustYScale;
            serializedPresentation.FindProperty("boostedParticleEmissionMultiplier").floatValue =
                Ee5SliceProfile.PlayerBoostedParticleEmissionMultiplier;
            serializedPresentation.FindProperty("boostedExhaustStartColor").colorValue =
                Ee5SliceProfile.PlayerBoostedExhaustCoreColor;
            serializedPresentation.FindProperty("boostedExhaustMidColor").colorValue =
                Ee5SliceProfile.PlayerBoostedExhaustMidColor;
            serializedPresentation.FindProperty("boostedExhaustEndColor").colorValue =
                Ee5SliceProfile.PlayerBoostedExhaustTipColor;
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
            PlayerCollisionDamage collisionDamage = player.AddComponent<PlayerCollisionDamage>();
            collisionDamage.enabled = Ee5SliceProfile.PlayerCollisionDamageEnabled;
            if (!preservePrefabs)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    player,
                    PlayerPrefabPath,
                    InteractionMode.AutomatedAction);
            }
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
            serializedProjectile.FindProperty("lifetime").floatValue = Ee5SliceProfile.PlayerProjectileLifetime;
            serializedProjectile.FindProperty("damage").floatValue = Ee5SliceProfile.PlayerProjectileDamage;
            serializedProjectile.FindProperty("knockback").floatValue = Ee5SliceProfile.PlayerProjectileKnockback;
            serializedProjectile.FindProperty("destroyOnUnrecognizedCollision").boolValue =
                Ee5SliceProfile.PlayerProjectileDestroysOnUnknownCollision;
            serializedProjectile.FindProperty("maxTrailPoints").intValue = 6;
            serializedProjectile.FindProperty("pointSpacing").floatValue = 0.03f;
            serializedProjectile.FindProperty("useImpactFade").boolValue = true;
            serializedProjectile.FindProperty("impactFadeTime").floatValue = 0.08f;
            serializedProjectile.FindProperty("trailStartColor").colorValue = Color.white;
            serializedProjectile.FindProperty("trailEndColor").colorValue =
                new Color(1f, 0.1f, 0.04f, 1f);
            serializedProjectile.FindProperty("nearMissDistance").floatValue = Ee5SliceProfile.ProjectileNearMissDistance;
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
            AssignLineMaterial(line);

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
            string defeatedSpritePath,
            bool preservePrefabs)
        {
            GameObject enemy = new GameObject(objectName);
            enemy.transform.position = position;
            // Match the EE5 prefab basis: the purple melee art is authored
            // facing local negative X, so the root mirror supplies its saved
            // default world-facing direction without corrupting sprite names.
            enemy.transform.localScale = Vector3.one * 1f;
            Vector3 authoredRootScale = enemy.transform.localScale;
            authoredRootScale.x = ranged
                ? Ee5SliceProfile.EnemyGunnerRootScaleX
                : Ee5SliceProfile.EnemyMeleeRootScaleX;
            enemy.transform.localScale = authoredRootScale;

            Rigidbody2D body = enemy.AddComponent<Rigidbody2D>();
            // EE5 drives enemies with MovePosition on kinematic, interpolated
            // bodies. Dynamic bodies fight that scripted motion at contact and
            // produce the exact melee jitter the slice report caught.
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D collider = enemy.AddComponent<CircleCollider2D>();
            // The melee hitbox is slightly larger than its visual body so that
            // contact damage becomes reliable before the chase state brakes.
            collider.radius = ranged ? 0.55f : 0.72f;
            collider.isTrigger = !ranged && Ee5SliceProfile.EnemyMeleeUsesTriggerBody;
            HealthComponent health = enemy.AddComponent<HealthComponent>();
            SerializedObject serializedHealth = new SerializedObject(health);
            serializedHealth.FindProperty("maxHealth").floatValue = ranged ? 5f : 3f;
            serializedHealth.FindProperty("invulnerabilityDuration").floatValue =
                Ee5SliceProfile.EnemyInvulnerabilityDuration;
            serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            EnemyController controller = enemy.AddComponent<EnemyController>();
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("detectionRange").floatValue = 12f;
            serializedController.FindProperty("wakeDistance").floatValue = 6f;
            serializedController.FindProperty("movementMode").enumValueIndex = (int)(
                ranged ? EnemyMovementMode.Wander : EnemyMovementMode.Chase);
            serializedController.FindProperty("wanderRadius").floatValue =
                Ee5SliceProfile.EnemyGunnerWanderRadius;
            serializedController.FindProperty("wanderDurationMin").floatValue =
                Ee5SliceProfile.EnemyGunnerWanderDurationMin;
            serializedController.FindProperty("wanderDurationMax").floatValue =
                Ee5SliceProfile.EnemyGunnerWanderDurationMax;
            serializedController.FindProperty("wakeDuration").floatValue =
                Ee5SliceProfile.EnemyWakeBuildupDuration;
            serializedController.FindProperty("requireLineOfSightToWake").boolValue = true;
            // The line arms across EE5's four-times wake envelope. The six-unit
            // value remains the base trigger; a full clear-sight charge commits
            // the idle/scream intro before combat begins.
            serializedController.FindProperty("wakeSignalDistanceMultiplier").floatValue =
                Ee5SliceProfile.EnemyWakeSignalDistanceMultiplier;
            serializedController.FindProperty("wakeIdleDurationMin").floatValue =
                Ee5SliceProfile.EnemyWakeIdleDurationMin;
            serializedController.FindProperty("wakeIdleDurationMax").floatValue =
                Ee5SliceProfile.EnemyWakeIdleDurationMax;
            serializedController.FindProperty("wakeScreamDuration").floatValue =
                Ee5SliceProfile.EnemyWakeScreamDuration;
            serializedController.FindProperty("wakeSignalChargeDuration").floatValue =
                Ee5SliceProfile.EnemyWakeSignalChargeDuration;
            serializedController.FindProperty("wakeSignalChargeDecay").floatValue =
                Ee5SliceProfile.EnemyWakeSignalChargeDecay;
            serializedController.FindProperty("wakeSignalChargeSpeedAtEdge").floatValue =
                Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtEdge;
            serializedController.FindProperty("wakeSignalChargeSpeedAtClose").floatValue =
                Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtClose;
            serializedController.FindProperty("wakeFinalWarningDuration").floatValue =
                Ee5SliceProfile.EnemyWakeFinalWarningDuration;
            serializedController.FindProperty("attackRange").floatValue = ranged
                ? 7f
                : Ee5SliceProfile.EnemyMeleeAttackRange;
            serializedController.FindProperty("attackExitRange").floatValue = ranged
                ? 7f
                : Ee5SliceProfile.EnemyMeleeAttackExitRange;
            serializedController.FindProperty("contactDamageRange").floatValue = ranged
                ? 0f
                : Ee5SliceProfile.EnemyMeleeContactRange;
            serializedController.FindProperty("attackFacingRefreshDegrees").floatValue = ranged
                ? 0f
                : Ee5SliceProfile.EnemyMeleeAttackFacingRefreshDegrees;
            serializedController.FindProperty("targetBuffer").floatValue = 0.04f;
            // Match the authored EE5 roles: the white gunner moves at 2 and
            // the purple close hunter at 3 units per second.
            serializedController.FindProperty("chaseSpeed").floatValue = ranged
                ? Ee5SliceProfile.EnemyGunnerChaseSpeed
                : Ee5SliceProfile.EnemyMeleeChaseSpeed;
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
            // The gold EE5 gunner wanders around its spawn center rather than
            // orbiting the player. Keep the old orbit field available for
            // experiments, but never enable it in the vertical-slice profile.
            serializedController.FindProperty("orbitWhileAttacking").boolValue = false;
            serializedController.FindProperty("orbitRadius").floatValue = 1.5f;
            serializedController.FindProperty("orbitMoveSpeed").floatValue = 2f;
            serializedController.FindProperty("orbitAngularSpeed").floatValue = ranged ? 135f : 100f;
            serializedController.FindProperty("orbitDirection").floatValue = 1f;
            serializedController.FindProperty("nearMissDistance").floatValue = 1.65f;
            serializedController.FindProperty("nearMissExitDistance").floatValue = 2.15f;
            serializedController.FindProperty("faceTurnSpeed").floatValue =
                Ee5SliceProfile.EnemyFaceTurnSpeed;
            serializedController.FindProperty("forwardIsLocalNegativeX").boolValue = ranged;
            serializedController.FindProperty("keepSpriteUpright").boolValue = true;
            serializedController.FindProperty("gameState").objectReferenceValue = gameState;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            // EE5 gives both roster roles the same close-contact punishment.
            // The gunner remains primarily a ranged threat, but colliding with
            // it must never become a free pass through the encounter.
            EnemyContactDamage contactDamage = enemy.AddComponent<EnemyContactDamage>();
            SerializedObject serializedContact = new SerializedObject(contactDamage);
            serializedContact.FindProperty("damage").floatValue = Ee5SliceProfile.EnemyContactDamage;
            serializedContact.FindProperty("cooldown").floatValue = Ee5SliceProfile.EnemyContactCooldown;
            serializedContact.FindProperty("knockback").floatValue = Ee5SliceProfile.EnemyContactKnockback;
            serializedContact.ApplyModifiedPropertiesWithoutUndo();
            enemy.AddComponent<DamageFlashFeedback>();
            if (ranged)
            {
                EnemyWeapon weapon = enemy.AddComponent<EnemyWeapon>();
                GameObject firePoint = new GameObject("Enemy Fire Point");
                firePoint.transform.SetParent(enemy.transform, false);
                firePoint.transform.localPosition = Ee5SliceProfile.EnemyGunnerFirePointLocalPosition;
                SerializedObject serializedWeapon = new SerializedObject(weapon);
                serializedWeapon.FindProperty("enforceEe5Profile").boolValue = true;
                serializedWeapon.FindProperty("gameState").objectReferenceValue = gameState;
                serializedWeapon.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
                serializedWeapon.FindProperty("firePoint").objectReferenceValue = firePoint.transform;
                serializedWeapon.FindProperty("attackRange").floatValue = 7f;
                serializedWeapon.FindProperty("requireTargetWithinAttackRange").boolValue =
                    Ee5SliceProfile.EnemyGunnerRequiresAttackRange;
                serializedWeapon.FindProperty("requireLineOfSightToFire").boolValue =
                    Ee5SliceProfile.EnemyGunnerRequiresLineOfSightToFire;
                serializedWeapon.FindProperty("fireCooldown").floatValue = Ee5SliceProfile.EnemyGunnerFireCooldown;
                serializedWeapon.FindProperty("projectileSpeed").floatValue = Ee5SliceProfile.EnemyGunnerProjectileSpeed;
                serializedWeapon.FindProperty("projectileLifetime").floatValue =
                    Ee5SliceProfile.EnemyGunnerProjectileLifetime;
                serializedWeapon.FindProperty("projectileKnockback").floatValue =
                    Ee5SliceProfile.EnemyGunnerProjectileKnockback;
                serializedWeapon.FindProperty("mirrorFirePointYWithUprightFlip").boolValue =
                    Ee5SliceProfile.EnemyGunnerMirrorFirePointYWithUprightFlip;
                serializedWeapon.FindProperty("projectileTint").colorValue = new Color(0.05f, 1f, 0.16f, 1f);
                serializedWeapon.FindProperty("drawAimTelegraph").boolValue =
                    Ee5SliceProfile.EnemyGunnerDrawAimTelegraph;
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
            AssignLineMaterial(line);

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
            serializedPresentation.FindProperty("pingPongDormantAnimation").boolValue = true;
            serializedPresentation.FindProperty("randomizeDormantStartFrame").boolValue = true;
            serializedPresentation.FindProperty("faceDormantTowardTarget").boolValue =
                ranged || Ee5SliceProfile.EnemyMeleeFacesDormantTarget;
            serializedPresentation.FindProperty("invertDormantSpriteX").boolValue =
                !ranged && Ee5SliceProfile.EnemyMeleeInvertsSpriteDuringIntro;
            serializedPresentation.FindProperty("forwardIsLocalNegativeX").boolValue = ranged;
            serializedPresentation.FindProperty("dormantFacingHysteresis").floatValue =
                Ee5SliceProfile.EnemyDormantFacingHysteresis;
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

            if (!preservePrefabs)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    enemy,
                    prefabPath,
                    InteractionMode.AutomatedAction);
            }
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
            gate.transform.position = Ee5SliceProfile.VerticalSliceGatePosition;
            BoxCollider2D gateCollider = gate.AddComponent<BoxCollider2D>();
            gateCollider.size = Ee5SliceProfile.VerticalSliceGateColliderSize;
            EnergyGate energyGate = gate.AddComponent<EnergyGate>();
            GameObject keyTarget = new GameObject("Key Target");
            keyTarget.transform.SetParent(gate.transform, false);
            keyTarget.transform.localPosition = Ee5SliceProfile.VerticalSliceGateKeyTarget;
            SerializedObject serializedGate = new SerializedObject(energyGate);
            serializedGate.FindProperty("enforceEe5Profile").boolValue = true;
            serializedGate.FindProperty("liftDistance").floatValue =
                Ee5SliceProfile.EnergyGateLiftDistance;
            serializedGate.FindProperty("liftSpeed").floatValue =
                Ee5SliceProfile.EnergyGateLiftSpeed;
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
            CreateSquareOutline(
                gate.transform,
                Ee5SliceProfile.VerticalSliceGateColliderSize,
                new Color(0.2f, 0.55f, 1f));
            CreateGateVisual(gate.transform);

            GameObject key = new GameObject("Energy Key");
            // EE5 carries the key on the ranged gunner from frame one. The
            // old compact-slice spawn was detached from the carrier, which
            // made the objective appear to teleport at scene start.
            key.transform.position = gunnerEnemy.transform.position
                + Ee5SliceProfile.EnergyKeyEnemyOffset;
            CircleCollider2D keyCollider = key.AddComponent<CircleCollider2D>();
            keyCollider.isTrigger = true;
            EnergyKey energyKey = key.AddComponent<EnergyKey>();
            Rigidbody2D keyBody = key.GetComponent<Rigidbody2D>();
            if (keyBody)
            {
                // EnergyKey follows on FixedUpdate through MovePosition. Keep
                // the EE5 kinematic/interpolated transport serialized in the
                // generated scene instead of relying only on runtime Awake.
                keyBody.bodyType = RigidbodyType2D.Kinematic;
                keyBody.gravityScale = 0f;
                keyBody.interpolation = RigidbodyInterpolation2D.Interpolate;
                keyBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }
            SerializedObject serializedKey = new SerializedObject(energyKey);
            serializedKey.FindProperty("enforceEe5Profile").boolValue = true;
            serializedKey.FindProperty("requiredEncounter").objectReferenceValue = encounter;
            // EE5's key-lock beat is carried by the ranged gunner. The melee
            // hunter remains active pressure while the player chases the key,
            // which is the authored reason this objective does not require the
            // entire encounter to be cleared before it can progress.
            serializedKey.FindProperty("enemyTarget").objectReferenceValue = gunnerEnemy;
            serializedKey.FindProperty("targetGate").objectReferenceValue = energyGate;
            serializedKey.FindProperty("enemyOffset").vector3Value =
                Ee5SliceProfile.EnergyKeyEnemyOffset;
            serializedKey.FindProperty("enemyOrbitRadius").floatValue =
                Ee5SliceProfile.EnergyKeyEnemyOrbitRadius;
            serializedKey.FindProperty("enemyOrbitSpeed").floatValue =
                Ee5SliceProfile.EnergyKeyEnemyOrbitSpeed;
            serializedKey.FindProperty("enemyOrbitSharpness").floatValue =
                Ee5SliceProfile.EnergyKeyEnemyOrbitSharpness;
            serializedKey.FindProperty("orbitRadiusX").floatValue =
                Ee5SliceProfile.EnergyKeyOrbitRadiusX;
            serializedKey.FindProperty("orbitRadiusY").floatValue =
                Ee5SliceProfile.EnergyKeyOrbitRadiusY;
            serializedKey.FindProperty("orbitSpeed").floatValue =
                Ee5SliceProfile.EnergyKeyOrbitSpeed;
            serializedKey.FindProperty("orbitSharpness").floatValue =
                Ee5SliceProfile.EnergyKeyOrbitSharpness;
            serializedKey.FindProperty("orbitRotationSpeed").floatValue = 0f;
            serializedKey.FindProperty("radiusEase").floatValue =
                Ee5SliceProfile.EnergyKeyRadiusEase;
            serializedKey.FindProperty("centerFollowSharpness").floatValue =
                Ee5SliceProfile.EnergyKeyCenterFollowSharpness;
            serializedKey.FindProperty("playerOffset").vector3Value =
                Ee5SliceProfile.EnergyKeyPlayerOffset;
            serializedKey.FindProperty("gateUnlockRange").floatValue =
                Ee5SliceProfile.EnergyKeyGateUnlockRange;
            serializedKey.FindProperty("collectDistance").floatValue =
                Ee5SliceProfile.EnergyKeyCollectDistance;
            serializedKey.FindProperty("playerFollowSharpness").floatValue =
                Ee5SliceProfile.EnergyKeyPlayerFollowSharpness;
            serializedKey.FindProperty("gateFlySpeed").floatValue =
                Ee5SliceProfile.EnergyKeyGateFlySpeed;
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
            GameObject keyVisual = new GameObject("Key Visual");
            keyVisual.transform.SetParent(key.transform, false);
            keyVisual.transform.localPosition = Ee5SliceProfile.EnergyKeyVisualOffset;
            keyVisual.transform.localScale = Vector3.one * Ee5SliceProfile.EnergyKeyVisualScale;
            SpriteRenderer keySprite = keyVisual.AddComponent<SpriteRenderer>();
            keySprite.sprite = LoadFirstSprite(
                EnergyKeySpriteAssetPath,
                LegacyEnergyKeySpriteAssetPath);
            keySprite.sortingOrder = 10;
            serializedKey.FindProperty("visual").objectReferenceValue = keyVisual.transform;
            serializedKey.ApplyModifiedPropertiesWithoutUndo();

            GameObject exit = new GameObject("Level Exit");
            exit.transform.position = Ee5SliceProfile.VerticalSliceExitPosition;
            CircleCollider2D collider = exit.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = Ee5SliceProfile.VerticalSliceExitRadius;
            LevelExit levelExit = exit.AddComponent<LevelExit>();
            SerializedObject serializedExit = new SerializedObject(levelExit);
            // Keep the generated objective contract fully serialized. Runtime fallback is
            // useful for hand-authored scenes, but the reproducible FlightTest should not
            // depend on a scene-wide lookup to recover its encounter owner.
            serializedExit.FindProperty("encounter").objectReferenceValue = encounter;
            serializedExit.FindProperty("requiredGate").objectReferenceValue = energyGate;
            serializedExit.FindProperty("gameState").objectReferenceValue = gameState;
            serializedExit.ApplyModifiedPropertiesWithoutUndo();
            ExtractionPortalPresentation portal = exit.AddComponent<ExtractionPortalPresentation>();
            SerializedObject serializedPortal = new SerializedObject(portal);
            serializedPortal.FindProperty("portalDiameter").floatValue = 3.8f;
            serializedPortal.FindProperty("ringSegments").intValue = 80;
            serializedPortal.ApplyModifiedPropertiesWithoutUndo();

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
            GameStateMachine gameState,
            EncounterController encounter,
            EnergyKey energyKey,
            LevelExit levelExit)
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
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            healthLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            actionCalloutLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            bannerLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            // Persist the same objective contract that the director evaluates.
            // GameplayHud still has runtime recovery for hand-authored scenes,
            // but the generated FlightTest should not depend on scene-wide
            // lookups to become reproducible after a fresh checkout.
            serialized.FindProperty("encounter").objectReferenceValue = encounter;
            serialized.FindProperty("energyKey").objectReferenceValue = energyKey;
            serialized.FindProperty("exit").objectReferenceValue = levelExit;
            serialized.FindProperty("gameState").objectReferenceValue = gameState;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            canvasObject.AddComponent<SliceInstructionDisplay>();
        }

        static void CreateInstructionTriggers()
        {
            CreateInstructionTrigger(
                "Flight Controls Instruction",
                Ee5SliceProfile.VerticalSliceFlightInstructionPosition,
                Ee5SliceProfile.VerticalSliceFlightInstructionSize,
                "W / UP / SPACE  THRUST\nA / D  ROTATE    S / DOWN  STABILIZE\nX  FLIP    Z / ENTER / MOUSE  FIRE",
                true);
            CreateInstructionTrigger(
                "Energy Key Instruction",
                Ee5SliceProfile.VerticalSliceKeyInstructionPosition,
                Ee5SliceProfile.VerticalSliceKeyInstructionSize,
                "DEFEAT THE CARRIER.\nTHE ENERGY KEY WILL BREAK FREE WHEN IT IS DEFEATED.");
            CreateInstructionTrigger(
                "Energy Gate Instruction",
                Ee5SliceProfile.VerticalSliceGateInstructionPosition,
                Ee5SliceProfile.VerticalSliceGateInstructionSize,
                "COLLECT THE ENERGY KEY,\nTHEN FLY INTO THE ENERGY GATE.");
            CreateInstructionTrigger(
                "Extraction Instruction",
                Ee5SliceProfile.VerticalSliceExitInstructionPosition,
                Ee5SliceProfile.VerticalSliceExitInstructionSize,
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
            AssignLineMaterial(line);
        }

        static void CreateCamera(PlayerCharacter target, Transform[] parallaxBackdrops)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.orthographic = true;
            camera.orthographicSize = Ee5SliceProfile.CameraOrthographicSize;
            camera.backgroundColor = Ee5SliceProfile.CameraBackgroundColor;
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
            serializedFollow.FindProperty("wallSlamMinSpeed").floatValue =
                Ee5SliceProfile.CameraWallSlamMinSpeed;
            serializedFollow.FindProperty("wallSlamMaxSpeed").floatValue =
                Ee5SliceProfile.CameraWallSlamMaxSpeed;
            serializedFollow.FindProperty("wallSlamShakeStrength").floatValue =
                Ee5SliceProfile.CameraWallSlamShakeStrength;
            serializedFollow.FindProperty("wallSlamShakeDuration").floatValue =
                Ee5SliceProfile.CameraWallSlamShakeDuration;
            serializedFollow.FindProperty("wallSlamCooldown").floatValue =
                Ee5SliceProfile.CameraWallSlamCooldown;
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
            serialized.FindProperty("followThroughAssistDuration").floatValue =
                Ee5SliceProfile.BrittleFollowThroughAssistDuration;
            serialized.FindProperty("angularVelocityRetain").floatValue =
                Ee5SliceProfile.BrittleAngularVelocityRetention;
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
            Vector2 halfExtents = Ee5SliceProfile.VerticalSliceArenaHalfExtents;
            Vector2 boundarySize = new Vector2(
                halfExtents.x * 2f,
                halfExtents.y * 2f);
            float overscan = Ee5SliceProfile.VerticalSliceBoundaryOverscan;
            float thickness = Ee5SliceProfile.VerticalSliceBoundaryThickness;
            CreateWall(
                "Left Wall",
                new Vector2(-halfExtents.x, 0f),
                new Vector2(thickness, boundarySize.y + overscan));
            CreateWall(
                "Right Wall",
                new Vector2(halfExtents.x, 0f),
                new Vector2(thickness, boundarySize.y + overscan));
            CreateWall(
                "Floor",
                new Vector2(0f, -halfExtents.y),
                new Vector2(boundarySize.x, thickness));
            CreateWall(
                "Ceiling",
                new Vector2(0f, halfExtents.y),
                new Vector2(boundarySize.x, thickness));

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
                Ee5SliceProfile.VerticalSliceUpperShelfPosition,
                Ee5SliceProfile.VerticalSliceUpperShelfSize);
            CreateBrittleWall(
                "Lower Crater Shelf",
                Ee5SliceProfile.VerticalSliceLowerShelfPosition,
                Ee5SliceProfile.VerticalSliceLowerShelfSize);
            CreateWall(
                "Extraction Spine",
                Ee5SliceProfile.VerticalSliceExtractionSpinePosition,
                Ee5SliceProfile.VerticalSliceExtractionSpineSize);
        }

        static void CreateFlightStopperZone()
        {
            // EE5's lower-center white stopper is a gameplay volume, not a
            // decorative wall: while inside it, flight input and rotation are
            // suppressed but existing momentum is allowed to coast through.
            GameObject zone = new GameObject("Flight Stopper Zone");
            zone.tag = "StopperZone";
            zone.transform.position = new Vector3(
                0f,
                Ee5SliceProfile.FlightStopperCenterY,
                0f);

            BoxCollider2D collider = zone.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(
                Ee5SliceProfile.FlightStopperWidth,
                Ee5SliceProfile.FlightStopperHeight);

            // Keep the cue understated like EE5's nearly transparent white
            // strip while still making the volume discoverable in the slice.
            CreateSquareOutline(
                zone.transform,
                collider.size,
                new Color(0.75f, 0.88f, 1f, 0.32f));
        }

        static void EnsureTag(string tag)
        {
            if (!InternalEditorUtility.tags.Contains(tag))
            {
                InternalEditorUtility.AddTag(tag);
                Debug.Log($"Added required gameplay tag: {tag}");
            }
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

            Vector2 halfExtents = Ee5SliceProfile.VerticalSliceArenaHalfExtents;
            float floorY = -halfExtents.y;
            float basinBottomY = floorY - Ee5SliceProfile.VerticalSliceBoundaryThickness;
            float halfWidth = halfExtents.x;

            CreateMoonTerrainPiece(
                "Playable Low Basin - SpriteShape",
                profile,
                Vector2.zero,
                new[]
                {
                    new Vector2(-halfWidth, floorY + 0.28f),
                    new Vector2(-halfWidth * 0.8f, floorY + 0.52f),
                    new Vector2(-halfWidth * 0.55f, floorY + 0.38f),
                    new Vector2(-halfWidth * 0.275f, floorY + 0.6f),
                    new Vector2(0f, floorY + 0.44f),
                    new Vector2(halfWidth * 0.275f, floorY + 0.62f),
                    new Vector2(halfWidth * 0.55f, floorY + 0.42f),
                    new Vector2(halfWidth * 0.8f, floorY + 0.55f),
                    new Vector2(halfWidth, floorY + 0.3f),
                    new Vector2(halfWidth, basinBottomY),
                    new Vector2(-halfWidth, basinBottomY)
                });

            CreateMoonTerrainPiece(
                "Upper Crater Shelf - SpriteShape",
                profile,
                Ee5SliceProfile.VerticalSliceUpperShelfPosition,
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
                Ee5SliceProfile.VerticalSliceLowerShelfPosition,
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
                Ee5SliceProfile.VerticalSliceHazardPosition,
                Ee5SliceProfile.VerticalSliceHazardRadius,
                new Color(1f, 0.08f, 0.01f, 0.92f));
            CreateHealthPickup(Ee5SliceProfile.VerticalSliceHealthCachePosition);
            CreateFireRatePickup(Ee5SliceProfile.VerticalSliceFireRateCachePosition);
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
            // energy_gate.png is a 256px transparent canvas whose authored
            // red strip occupies only x=45..226, y=220..235. Rotate the strip
            // into the vertical EE5 gate, scale it to the 3.8-unit collider,
            // and cancel the source canvas offset so artwork and collision
            // share one center instead of leaving the gate apparently absent.
            visual.transform.localScale = Vector3.one * Ee5SliceProfile.EnergyGateArtworkScale;
            visual.transform.localPosition = Ee5SliceProfile.EnergyGateArtworkLocalPosition;

            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = gateSprite;
            renderer.color = Color.white;
            renderer.sortingOrder = 5;
        }

        static void CreateSquareOutline(Transform parent, Vector2 size, Color color)
        {
            if (!parent)
                return;

            LineRenderer line = parent.gameObject.AddComponent<LineRenderer>();
            if (!line)
                return;

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
            AssignLineMaterial(line);
        }

        static void CreateCircleOutline(
            Transform parent,
            float radius,
            Color color,
            int segments,
            float width)
        {
            if (!parent)
                return;

            // Builder input is authored data, so keep a malformed radius or
            // segment count from turning a presentation-only helper into a
            // scene-build exception. The collider remains the gameplay source
            // of truth; this only bounds the decorative renderer.
            radius = Mathf.Max(0f, radius);
            width = Mathf.Max(0.001f, width);
            segments = Mathf.Clamp(segments, 8, 128);

            // A hazard can intentionally have more than one ring. Unity does
            // not allow multiple LineRenderer components on one GameObject,
            // so each decorative ring gets its own child instead of silently
            // returning a null component on the second call.
            GameObject outlineObject = new GameObject("Circle Outline");
            if (!outlineObject)
                return;

            outlineObject.transform.SetParent(parent, false);
            LineRenderer line = outlineObject.AddComponent<LineRenderer>();
            if (!line)
                return;

            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segments;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = 14;
            line.numCornerVertices = 2;
            AssignLineMaterial(line);

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }
        }

        static Material CreateLineMaterial()
        {
            if (generatedLineMaterial)
                return generatedLineMaterial;

            // Unity 6 projects using URP may not resolve the legacy
            // Sprites/Default shader in editor code. Prefer the URP sprite
            // shader, then keep the old path as a compatibility fallback.
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (!shader)
                shader = Shader.Find("Sprites/Default");
            if (!shader)
                shader = Shader.Find("Unlit/Color");

            if (shader)
            {
                try
                {
                    generatedLineMaterial = new Material(shader);
                    generatedLineMaterial.name = "FlightTest Generated Line Material";
                    return generatedLineMaterial;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning(
                        $"FlightTest decorative line material could not be created: {exception.Message}");
                }
            }

            // The built-in line material is a safe final fallback for scene
            // composition. A missing decorative material must not prevent the
            // gameplay slice from being saved.
            generatedLineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");
            return generatedLineMaterial;
        }

        static void AssignLineMaterial(LineRenderer line)
        {
            if (!line)
                return;

            Material material = CreateLineMaterial();
            // A LineRenderer can still serialize its geometry without a
            // material. Do not assign null through the material property: in
            // Unity 6 that setter can throw inside the native renderer and
            // abort the entire scene build.
            if (material)
                line.sharedMaterial = material;
            else if (!loggedMissingLineMaterial)
            {
                loggedMissingLineMaterial = true;
                Debug.LogWarning(
                    "FlightTest decorative outlines were generated without a material; "
                    + "the gameplay scene remains valid, but the outline shader could not be resolved.");
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
