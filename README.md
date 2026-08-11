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
| `PlayerCharacter` | Composition root exposing the playable character's core systems and shared gameplay-eligibility contract, including game-state gating. |
| `PlayerRespawnController` | Reproduces the EE5 current-room reload on death, with an explicit in-place respawn fallback for reusable rooms. |
| `PlayerWeapon` | Handles player firing, cooldowns, fire-rate boosts, recoil, and the EE5-style wall/enemy aim line. |
| `PlayerProjectile` | Shared projectile movement, lifetime, owner filtering, damage impacts, source-specific speed/tint overrides, and team-colored trails. |
| `PlayerFlightPresentation` | Drives exhaust and EE5-style one-shot squash feedback without touching physics. |
| `PlayerDamageFeedback` | Converts damage events into the EE5 alternating red/yellow hit flash and clears transient state on death/disable. |
| `PlayerCameraFollow` | Provides EE5-style velocity lead, speed zoom, starfield parallax, coherent shake, enemy-death feedback, and wall-impact feedback. |
| `PlayerHealthDisplay` | Presents health state without owning combat rules. |
| `ContactHazard` | Optional reusable damage volume for authored hazard rooms. |
| `PlayerCollisionDamage` | Converts high-speed impacts into collision damage. |
| `HealthPickup` / `FireRatePickup` | Optional reusable player upgrades kept out of the gold-standard slice. |
| `EnemyController` | Provides explicit dormant, waking, chasing, attacking, and defeated states with EE5-style wall steering and optional ranged orbit movement. |
| `EnemySpritePresentation` | Maps enemy states to imported idle, wake-alert, active, and defeat animation frames. |
| `EnemyWakePresentation` | Renders the controller-owned wake telegraph so enemy activation is readable before combat begins. |
| `EnemyHealthDisplay` | Presents the imported six-frame health sheet briefly at spawn and after damage, scaled to each enemy's max health. |
| `EnemyDeathPresentation` / `EnemyDeathBurst` | Plays the imported EE5 defeat animation and audio independently from enemy cleanup. |
| `EnemyContactDamage` | Applies close-range melee damage through the shared contract; ranged enemies stay ranged. |
| `EnemyWeapon` | Adds the EE5 white-gunner cadence and travel speed without coupling enemies to player weapons. |
| `EncounterController` / `LevelExit` | Define the explicit combat roster while the EE5 carrier-key-gate-extraction sequence owns exit progression. |
| `ExtractionPortalPresentation` | Provides the layered, pulsing extraction visual without scene-specific prototype dependencies. |
| `EnergyKey` / `EnergyGate` | Recreate the EE5 key-to-gate traversal objective, including release-point-preserving player orbit motion. |
| `ScoreSystem` | Provides event-driven arcade scoring for combat and objectives. |
| `GameplayHud` | Presents score and the current vertical-slice objective. |
| `DamageFlashFeedback` / `ProjectileImpactBurst` | Provide immediate combat readability while authored VFX are migrated. |

Gameplay systems should communicate with these public contracts rather than reaching into Rigidbody2D directly.

Combat uses `IDamageable` and `DamageInfo`, so weapons do not need to know whether they hit a player, enemy, or destructible object.

The editor menu `Extraterrestrial Exhaust > Build Flight Test Scene` rebuilds the playable FlightTest scene from code and refreshes the reusable `PlayerCraft`, `EnemyMelee`, and `EnemyGunner` prefabs. The two enemy compositions keep close-range contact pressure separate from ranged pressure, matching the distinct roles present in the EE5 scene family. The generated room deliberately contains only the encounter, energy key, gate, and extraction landmarks; optional pickup and hazard components remain available for later authored rooms without polluting this career-facing slice.

Running that builder also registers `FlightTest.unity` in Build Settings and configures the project identity for a playable vertical-slice build.

## EE5 reference scenes

Reference art is imported under `Assets/Art/Reference` with its Unity metadata intact. Runtime behavior is re-authored in EE MARK 6; legacy prototype scripts and prefabs are not copied into the production path.

The player gold standard is the shared `sniper.prefab` used by the `realScene` family in EE MARK 5. `realScene` is the baseline authored scene; `realScene2` adds the boss-health encounter; `realScene3` contains the final-boss variant. EE MARK 6 preserves the player’s tuned movement, camera, scale, health, weapon, and extraction loop while replacing prototype coupling with explicit runtime contracts. The builder keeps the EE5 camera response profile and the responsive `0.12s` player-shot cadence as deliberate slice tuning, rather than leaving those values implicit in an inspector.

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
