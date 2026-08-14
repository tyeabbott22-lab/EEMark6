# Extraterrestrial Exhaust - EE MARK 6

EE MARK 6 is a Unity 6 vertical slice of a 2D physics-action game featuring flight, combat, destructible terrain, enemy encounters, and a key-gate-extraction objective route. This repository is a public code sample built around one maintainable, end-to-end playable route. EE MARK 5 gameplay footage shows the broader prototype and its more carefully tuned game feel.

## Open and play

- Use Unity `6000.3.21f1`.
- Open `Assets/Scenes/FlightTest.unity` and press Play.
- `W` / `Up Arrow` / `Space`: thrust; `A` / `D` or arrow keys: rotate; `S` / `Down Arrow`: stabilize; `Z`, `Enter`, or left mouse: fire; `X`: flip facing.

## Verification

`Assets/Tests/PlayMode/PublicResumeSlicePlayModeTests.cs` covers flight input, projectile damage, enemy defeat, key collection, gate opening, and extraction using the live scene components. Run it from Unity's **Test Runner > PlayMode**.
