# Extraterrestrial Exhaust — EE MARK 6

EE MARK 6 is the Unity 6 production rebuild of *Extraterrestrial Exhaust*, a 2D physics-action game about piloting a small spacecraft through hostile environments.

## Development goals

- Build a complete, playable game around physics-based flight.
- Keep input, simulation, presentation, and game rules in separate systems.
- Prefer small, testable components over large scene-specific scripts.
- Make the project understandable to another developer without the history of the prototype.

## Vertical-slice contract

The target is a focused Unity 6 vertical slice that preserves the playable loop of EE MARK 5:

1. Pilot the craft through physics-based flight.
2. Defeat the encounter while managing health, wall impacts, and ranged pressure.
3. Collect the energy key, deactivate the gate, and reach extraction.
4. Watch the player capture sequence resolve into a clear completion state.

The project is intentionally organized so this loop can grow into the full game without moving prototype scripts wholesale into production code. The public-slice builder uses the EE5 wall, gate, objective, player, enemy, and moon-terrain references. Its basin and brittle shelves use their visible SpriteShape polygons as the actual collision surfaces, so the room shown to a reviewer is also the room used by gameplay.

## Runtime architecture

The player foundation is intentionally split into three responsibilities:

| System | Responsibility |
| --- | --- |
| `PlayerFlightInput` | Converts Unity Input System actions into a frame-independent flight command. |
| `PlayerFlightMotor` | Applies movement and stabilization forces to the player's `Rigidbody2D`, including the EE5 stopper-zone control lock and tangential wall slide behavior. |
| `PlayerFlightStateMachine` | Controls whether flight simulation is active, scripted, or disabled. |
| `GameStateMachine` | Coordinates playing, pausing, game over, and the short EE5 defeat slowdown without scattering global time writes. |
| `HealthComponent` | Shared health and damage contract for players, enemies, and hazards. |
| `ProjectileTeam` | Prevents player fire from harming the player and enemy fire from harming enemy allies. |
| `Ee5SliceProfile` | Named EE5 realScene tuning for player flight, weapon cadence, projectile speed, and camera feel; runtime locks are opt-out for experiments. |
| `SliceObjectiveDirector` | Event-driven encounter, key, gate, and extraction state machine that gives the HUD one authoritative EE5 slice objective. |
| `PlayerCharacter` | Composition root exposing the playable character's core systems and shared gameplay-eligibility contract, including game-state gating. |
| `PlayerRespawnController` | Reproduces the EE5 current-room reload on death, with an explicit in-place respawn fallback for reusable rooms. |
| `PlayerWeapon` / `PlayerWeaponPresentation` | Handles player firing, cooldowns, fire-rate boosts, recoil, aim line, and the immediate EE5-style muzzle punctuation without coupling VFX to damage rules. |
| `PlayerProjectile` / projectile presentation components | Shared projectile movement, owner filtering, damage impacts, six-point EE5 trail, enemy-only sprite ghosts, hard-stop/fade cleanup, source-specific speed/tint overrides, and team-colored exhaust. |
| `PlayerFlightPresentation` | Drives particle-backed exhaust, EE5-style asymmetric rotation boost flames, optional sprite-frame sequencing, and one-shot squash feedback without touching physics. |
| `PlayerDamageFeedback` | Converts damage events into the EE5 alternating red/yellow hit flash and clears transient state on death/disable. |
| `PlayerCameraFollow` | Provides EE5-style velocity lead, speed zoom, starfield parallax, coherent shake, enemy-death feedback, and wall-impact feedback. |
| `PlayerHealthDisplay` | Presents the player health sheet briefly at spawn and after damage/healing, then fades it without owning combat rules. |
| `ContactHazard` / `HazardPresentation` | Optional reusable heat volume with EE5-style knockback, pulse telegraph, damage accent, and ember presentation. |
| `PlayerCollisionDamage` | Optional high-speed impact experiment; disabled on the EE5 gold path because ordinary wall contact is not player damage in `realScene`. |
| `BrittleWall` | Recreates the EE5 thrust-assisted high-speed slam through interior brittle shelves while leaving arena boundaries permanent. |
| `FlightStopperZone` | Marks the EE5 lower-basin no-flight volume explicitly; the player motor owns the temporary control lock while the legacy tag remains only as scene compatibility. |
| `HealthPickup` / `FireRatePickup` / `PickupPresentation` | Optional recovery and weapon-power beats with authored idle motion and collection confirmation, while remaining non-required room pressure. |
| `EnemyController` | Provides explicit dormant, waking, chasing, attacking, and defeated states with EE5-style wall steering, ranged orbit movement, and line-of-sight wake charging. |
| `EnemySpritePresentation` | Maps enemy states to imported idle, wake-alert, active, and defeat animation frames. |
| `EnemyWakePresentation` | Renders the controller-owned blocked/clear wake telegraph with a final warning flash so activation is readable before combat begins. |
| `EnemyHealthDisplay` | Presents the imported six-frame health sheet briefly at spawn and after damage, scaled to each enemy's max health. |
| `EnemyDeathPresentation` / `EnemyDeathBurst` | Plays the imported EE5 defeat animation and audio independently from enemy cleanup. |
| `EnemyContactDamage` | Applies the EE5 close-pass contact damage contract to authored enemy bodies without coupling damage to their attack state. |
| `EnemyWeapon` | Adds the EE5 white-gunner cadence, wall-aware line of sight, and readable pre-shot telegraph without coupling enemies to player weapons. |
| `EncounterController` / `LevelExit` | Define the explicit combat roster while the EE5 carrier-key-gate-extraction sequence owns exit progression. |
| `ExtractionPortalPresentation` | Provides the layered, pulsing extraction visual without scene-specific prototype dependencies. |
| `EnergyKey` / `EnergyGate` | Recreate the EE5 key-to-gate traversal objective, including release-point-preserving player orbit motion and the authored upward gate lift. |
| `EnergyKeyPresentation` / `EnergyGatePresentation` / `ObjectiveSignalBurst` | Keep the key release, player collection, gate flight, timed gate-unlock pulse, and objective beats readable without coupling VFX to objective rules. |
| `ScoreSystem` | Provides EE5-style event-driven arcade scoring with speed, flips, near misses, combat/objective beats, a live chain timer, and a bounded multiplier. |
| `GameplayHud` | Presents score, live hull, action score callouts, and the current vertical-slice objective. |
| `SliceInstructionDisplay` / `SliceInstructionTrigger` | Recreate EE5 realScene's trigger-driven center instructions for controls, key flow, gate flow, and extraction. |
| `DamageFlashFeedback` / `ProjectileImpactBurst` | Provide immediate combat readability while authored VFX are migrated. |

Gameplay systems should communicate with these public contracts rather than reaching into Rigidbody2D directly.

Combat uses `IDamageable` and `DamageInfo`, so weapons do not need to know whether they hit a player, enemy, or destructible object.

The editor menu `Extraterrestrial Exhaust > Build Public Resume Slice (Preserve Prefabs)` rebuilds the narrow playable `FlightTest` scene without rewriting the reusable `PlayerCraft`, `EnemyMelee`, or `EnemyGunner` prefabs. `Build Flight Test Scene (Preserve Prefabs)` points to the same public route. The scene contains only the required player, camera, melee hunter, gunner, encounter, basin terrain, brittle shelves, no-flight stopper, carrier key, laser gate, extraction portal, HUD, and instruction flow. The broader non-preserving builder remains available for deliberate prefab repair and optional hazard experiments.

The preserved prefabs still retain their old triangle/square composition guides for forensic comparison, but the public builder serializes those renderers disabled on scene instances. The saved demo therefore presents imported sprites, SpriteShape terrain, the programmable laser barrier, and the extraction portal directly rather than depending on a first-frame cleanup pass to stop looking like a debug room.

`Extraterrestrial Exhaust > Normalize Imported Sprite Names` is a separate, explicit cleanup command for the EE5-derived filenames that are safe to clarify (`sprSnipe`, `health`, `bullet`, `keyfinal`, `buttonFInal1`, `wallFinal`, `sprStars`, and `sprExplode`). It renames them to semantic EE6 roles through Unity's asset database, preserving their GUIDs; the builder also supports the legacy paths until that command is run. Enemy strips remain on their EE5 names because the older variant builders reuse them by color and pose; those require an intentional art inventory pass before renaming.

Running that builder also registers `FlightTest.unity` as the first Build Settings scene and configures the project identity for a playable vertical-slice build.

## EE5 reference scenes

Reference art is imported under `Assets/Art/Reference` with its Unity metadata intact. Runtime behavior is re-authored in EE MARK 6; legacy prototype scripts and prefabs are not copied into the production path.

The read-only room reference for this public slice is `Assets/Scenes/EE6.unity` in EE MARK 5. The named contracts are `Playable Low Basin - SpriteShape (8)`, `Moon Thrust Stopper - Solid Fall Area`, `Laser Wall - Vertical Forever Gate` / `Laser Glow`, `enemyGun (1)`, and `Enemy 01 - close bruiser`. EE MARK 6 re-authors those observable roles with explicit runtime contracts; it does not copy the legacy scripts or modify the reference project.

## Project conventions

- Runtime scripts live under `Assets/Scripts/Runtime`.
- Editor-only tools live under `Assets/Scripts/Editor`.
- Reusable gameplay compositions live under `Assets/Prefabs`; the scene builder refreshes them from the same authored setup.
- Editor tooling is isolated in the `ExtraterrestrialExhaust.Editor` assembly.
- New gameplay code uses descriptive PascalCase names.
- Prototype code is migrated deliberately; it is not copied wholesale into the production path.

## Unity version

Unity `6000.3.21f1`.

## Build the public slice

1. Open the project in the Unity version above.
2. Optionally run `Extraterrestrial Exhaust > Validate Public Resume Slice Build Inputs` for a non-destructive dependency audit.
3. Run `Extraterrestrial Exhaust > Build Public Resume Slice (Preserve Prefabs)`.
4. Wait for the builder's contract validation to report no missing systems.
5. Open `Assets/Scenes/FlightTest.unity` and press Play.

The build command runs the same preflight automatically and cancels before prompting to save or replacing a scene if a required prefab, gameplay component, input asset, imported sprite, SpriteShape profile, physics material, or audio cue is missing. Once preflight passes, the builder backs up an existing `FlightTest` scene under `Library` before replacing it. It never reads from or writes to an EE MARK 5 project at build time; all approved reference art and configuration live inside this repository.

## Playtest controls

- `W` / `Up Arrow` / `Space`: thrust
- `A` / `D` or `Left` / `Right Arrow`: rotate
- `S` / `Down Arrow`: stabilize
- `X`: flip the craft's visual facing and weapon side
- `Z`, `Enter`, or left mouse: fire
