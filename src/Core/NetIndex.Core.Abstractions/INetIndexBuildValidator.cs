namespace NetIndex.Core.Abstractions;

/// <summary>
/// Validates feature-specific configuration when <see cref="INetIndexBuilder.Build"/> is called.
/// </summary>
/// <remarks>
/// Feature packages register implementations to force validation without loading optional
/// native dependencies or constructing runtime services.
/// </remarks>
public interface INetIndexBuildValidator
{
    /// <summary>
    /// Validates the feature configuration.
    /// </summary>
    void Validate();
}
