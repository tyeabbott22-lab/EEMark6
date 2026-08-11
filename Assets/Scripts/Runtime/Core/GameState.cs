namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// The small set of application states shared by menus and gameplay scenes.
    /// Scene-specific systems should react to these states instead of modifying time directly.
    /// </summary>
    public enum GameState
    {
        Boot,
        Playing,
        Paused,
        GameOver
    }
}
