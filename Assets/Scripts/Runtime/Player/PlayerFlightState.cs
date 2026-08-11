namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// High-level simulation modes for the player craft.
    /// Keeping these explicit prevents scripted sequences from fighting player input.
    /// </summary>
    public enum PlayerFlightState
    {
        FreeFlight,
        Scripted,
        Disabled
    }
}
