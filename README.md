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

The project is intentionally organized so this loop can grow into the full game without moving prototype scripts wholesale into production code. The current builder already uses the EE5 wall, gate, objective, player, and enemy art references; its generated colliders remain separate from those presentation assets.

## Runtime architecture

The player foundation is intentionally split into three responsibilities:

| System | Responsibility |
| --- | --- |
| `PlayerFlightInput` | Converts Unity Input System actions into a frame-independent flight command. |
| `PlayerFlightMotor` | Applies movement and stabilization forces to the player's `Rigidbody2D`. |
| `PlayerFlightStateMachine` | Controls whether flight simulation is active, scripted, or disabled. |
| `GameStateMachine` | Coordinates playing, pausing, game over, and the short EE5 defeat slowdown without scattering global time writes. |
| `HealthComponent` | Shared health and damage contract for players, enemies, and hazards. |
| `ProjectileTeam` | Prevents player fire from harming the player and enemy fire from harming enemy allies. |
| `Ee5SliceProfile` | Named EE5 realScene tuning for player flight, weapon cadence, projectile speed, and camera feel; runtime locks are opt-out for experiments. |
| `SliceObjectiveDirector` | Event-driven encounter, key, gate, and extraction state machine that gives the HUD one authoritative EE5 slice objective. |
| `PlayerCharacter` | Composition root exposing the playable character's core systems and shared gameplay-eligibility contract, including game-state gating. |
| `PlayerRespawnController` | Reproduces the EE5 current-room reload on death, with an explicit in-place respawn fallback for reusable rooms. |
| `PlayerWeapon` / `PlayerWeaponPresentation` | Handles player firing, cooldowns, fire-rate boosts, recoil, aim line, and the immediate EE5-style muzzle punctuation without coupling VFX to damage rules. |
| `PlayerProjectile` / `ProjectileExhaustPresentation` | Shared projectile movement, lifetime, owner filtering, damage impacts, source-specific speed/tint overrides, and EE5-style team-colored exhaust. |
| `PlayerFlightPresentation` | Drives particle-backed exhaust, EE5-style asymmetric rotation boost flames, optional sprite-frame sequencing, and one-shot squash feedback without touching physics. |
| `PlayerDamageFeedback` | Converts damage events into the EE5 alternating red/yellow hit flash and clears transient state on death/disable. |
| `PlayerCameraFollow` | Provides EE5-style velocity lead, speed zoom, starfield parallax, coherent shake, enemy-death feedback, and wall-impact feedback. |
| `PlayerHealthDisplay` | Presents the player health sheet briefly at spawn and after damage/healing, then fades it without owning combat rules. |
| `ContactHazard` / `HazardPresentation` | Optional reusable heat volume with EE5-style knockback, pulse telegraph, damage accent, and ember presentation. |
| `PlayerCollisionDamage` | Converts high-speed impacts into collision damage. |
| `BrittleWall` | Recreates the EE5 thrust-assisted high-speed slam through interior brittle shelves while leaving arena boundaries permanent. |
| `HealthPickup` / `FireRatePickup` / `PickupPresentation` | Optional recovery and weapon-power beats with authored idle motion and collection confirmation, while remaining non-required room pressure. |
| `EnemyController` | Provides explicit dormant, waking, chasing, attacking, and defeated states with EE5-style wall steering, ranged orbit movement, and line-of-sight wake charging. |
| `EnemySpritePresentation` | Maps enemy states to imported idle, wake-alert, active, and defeat animation frames. |
| `EnemyWakePresentation` | Renders the controller-owned blocked/clear wake telegraph with a final warning flash so activation is readable before combat begins. |
| `EnemyHealthDisplay` | Presents the imported six-frame health sheet briefly at spawn and after damage, scaled to each enemy's max health. |
| `EnemyDeathPresentation` / `EnemyDeathBurst` | Plays the imported EE5 defeat animation and audio independently from enemy cleanup. |
| `EnemyContactDamage` | Applies close-range melee damage through the shared contract; ranged enemies stay ranged. |
| `EnemyWeapon` | Adds the EE5 white-gunner cadence, wall-aware line of sight, and readable pre-shot telegraph without coupling enemies to player weapons. |
| `EncounterController` / `LevelExit` | Define the explicit combat roster while the EE5 carrier-key-gate-extraction sequence owns exit progression. |
| `ExtractionPortalPresentation` | Provides the layered, pulsing extraction visual without scene-specific prototype dependencies. |
| `EnergyKey` / `EnergyGate` | Recreate the EE5 key-to-gate traversal objective, including release-point-preserving player orbit motion and the authored upward gate lift. |
| `EnergyKeyPresentation` / `EnergyGatePresentation` / `ObjectiveSignalBurst` | Keep the key release, player collection, gate flight, and gate unlock beats readable without coupling VFX to objective rules. |
| `ScoreSystem` | Provides event-driven arcade scoring for combat and objectives. |
| `GameplayHud` | Presents score, live hull, action score callouts, and the current vertical-slice objective. |
| `SliceInstructionDisplay` / `SliceInstructionTrigger` | Recreate EE5 realScene's trigger-driven center instructions for controls, key flow, gate flow, and extraction. |
| `DamageFlashFeedback` / `ProjectileImpactBurst` | Provide immediate combat readability while authored VFX are migrated. |

Gameplay systems should communicate with these public contracts rather than reaching into Rigidbody2D directly.

Combat uses `IDamageable` and `DamageInfo`, so weapons do not need to know whether they hit a player, enemy, or destructible object.

The editor menu `Extraterrestrial Exhaust > Build Flight Test Scene` rebuilds the playable FlightTest scene from code and refreshes the reusable `PlayerCraft`, `EnemyMelee`, and `EnemyGunner` prefabs. The two enemy compositions keep close-range contact pressure separate from ranged pressure, matching the distinct roles present in the EE5 scene family. The generated room contains the required encounter, energy key, gate, and extraction landmarks plus deliberately optional hazard, health, and fire-rate beats; none of those optional resources can unlock or complete the objective flow.

`Extraterrestrial Exhaust > Normalize Imported Sprite Names` is a separate, explicit cleanup command for the six ambiguous EE5-derived filenames (`sprSnipe`, `health`, `bullet`, `keyfinal`, `buttonFInal1`, and `wallFinal`). It renames them to semantic EE6 roles through Unity's asset database, preserving their GUIDs; the builder also supports the legacy paths until that command is run.

Running that builder also registers `FlightTest.unity` as the first Build Settings scene and configures the project identity for a playable vertical-slice build.

## EE5 reference scenes

Reference art is imported under `Assets/Art/Reference` with its Unity metadata intact. Runtime behavior is re-authored in EE MARK 6; legacy prototype scripts and prefabs are not copied into the production path.

The player gold standard is the shared `sniper.prefab` used by the `realScene` family in EE MARK 5. `realScene` is the baseline authored scene; `realScene2` adds the boss-health encounter; `realScene3` contains the final-boss variant. EE MARK 6 preserves the player’s tuned movement, camera, scale, health, weapon, and extraction loop while replacing prototype coupling with explicit runtime contracts. The builder keeps the EE5 camera response profile and the deliberate one-second player-shot cadence as named slice tuning, rather than leaving those values implicit in an inspector.

## Project conventions

- Runtime scripts live under `Assets/Scripts/Runtime`.
- Editor-only tools live under `Assets/Scripts/Editor`.
- Reusable gameplay compositions live under `Assets/Prefabs`; the scene builder refreshes them from the same authored setup.
- Editor tooling is isolated in the `ExtraterrestrialExhaust.Editor` assembly.
- New gameplay code uses descriptive PascalCase names.
- Prototype code is migrated deliberately; it is not copied wholesale into the production path.

## Unity version

Unity `6000.3.21f1`.

## Playtest controls

- `W` / `Up Arrow` / `Space`: thrust
- `A` / `D` or `Left` / `Right Arrow`: rotate
- `S` / `Down Arrow`: stabilize
- `X`: flip the craft's visual facing and weapon side
- `Z`, `Enter`, or left mouse: fire
