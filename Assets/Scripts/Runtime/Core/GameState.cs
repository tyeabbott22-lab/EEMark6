namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Explains why the application entered GameOver. Keeping the reason next
    /// to the state prevents UI and analytics from guessing from scene objects.
    /// </summary>
    public enum GameOverReason
    {
        Unknown,
        HullLost,
        ExtractionComplete
    }

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
