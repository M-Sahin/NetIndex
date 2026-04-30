namespace NetIndex.Testing.Common;

/// <summary>
/// Implement on collection fixtures that need deterministic reset between tests.
/// </summary>
public interface IResetable
{
    /// <summary>
    /// Resets the fixture to a clean state. Called before each test.
    /// </summary>
    Task ResetAsync();
}
