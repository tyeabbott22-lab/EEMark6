# Extraterrestrial Exhaust - EE MARK 6

EE MARK 6 is a focused Unity 6 vertical slice for *Extraterrestrial Exhaust*,
a 2D physics-action game about piloting a small spacecraft through a hostile
moon basin. It is the public, code-focused companion to EE MARK 5: this
repository demonstrates the gameplay architecture and an end-to-end playable
route, while EE MARK 5 footage represents the more content-complete and
carefully tuned prototype.

## What is included

One scene, `Assets/Scenes/FlightTest.unity`, contains the complete sample
loop:

1. Fly with a physics-driven craft and fire projectiles.
2. Fight a ranged gunner and a close-range hunter.
3. Break through a brittle passage.
4. Defeat the gunner to release and collect an energy key.
5. Deliver the key to the laser gate and reach extraction.

The project intentionally avoids custom scene-generation steps. `FlightTest`
is the only enabled Build Settings scene and can be opened and played directly.

## Requirements and quick start

- Unity `6000.3.21f1`
- Open this repository as a Unity project.
- Open `Assets/Scenes/FlightTest.unity` and press Play.

Controls:

- `W`, `Up Arrow`, or `Space` - thrust
- `A` / `D` or `Left` / `Right Arrow` - rotate
- `S` or `Down Arrow` - stabilize
- `Z`, `Enter`, or left mouse - fire
- `X` - flip visual/weapon facing

## Architecture at a glance

Runtime code lives in `Assets/Scripts/Runtime` and separates input,
simulation, presentation, and objective ownership.

| Area | Primary components |
| --- | --- |
| Flight | `PlayerFlightInput`, `PlayerFlightMotor`, `PlayerFlightStateMachine` |
| Combat | `PlayerWeapon`, `PlayerProjectile`, `HealthComponent`, `DamageInfo` |
| Enemies | `EnemyController`, `EnemyWeapon`, `EnemyContactDamage` |
| Objective flow | `EncounterController`, `EnergyKey`, `EnergyGate`, `LevelExit`, `SliceObjectiveDirector` |
| Presentation | Player/enemy presentation components, `ProgrammableLaserGate`, `GameplayHud` |

The key handoff and extraction route are explicit state transitions rather
than scene-name checks. Runtime comments document non-obvious compatibility
and physics decisions close to the code that owns them.

## Tests

`Assets/Tests/PlayMode/PublicResumeSlicePlayModeTests.cs` verifies the public
route with live production components: keyboard flight input, player projectile
damage (including a visual-edge hit), enemy defeat, key release and collection,
gate opening, and extraction.

Run the suite from Unity's **Test Runner > PlayMode**. The scene is also
configured as the only Build Settings entry for a direct local build.

## Scope and limitations

This is a deliberately small resume sample, not a full-game release. It has
one authored room, two enemy roles, and a single objective route. Bosses,
additional scenes, broader progression, and frame-perfect EE MARK 5 tuning are
out of scope. Death/reload behavior is functional but intentionally less
developed than the showcased combat-to-extraction path.

For the strongest overview, pair this codebase with EE MARK 5 gameplay footage:
EE MARK 6 explains how the systems are structured; EE MARK 5 shows the broader
prototype's content and game feel.
