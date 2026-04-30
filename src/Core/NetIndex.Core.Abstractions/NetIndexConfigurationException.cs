using System;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Thrown when a configuration error is detected at pipeline startup.
/// </summary>
/// <remarks>
/// Common scenarios:
/// <list type="bullet">
///   <item><term>Dimension mismatch</term><description>Embedding generator dimensions do not match vector store dimensions.</description></item>
///   <item><term>Invalid configuration</term><description>Required settings are missing or malformed.</description></item>
/// </list>
/// 
/// This exception is thrown at <c>Build()</c> time via <c>IValidateOptions{T}</c> — never deferred to first pipeline use (NFR10).
/// </remarks>
public class NetIndexConfigurationException : NetIndexException
{
    /// <summary>
    /// Gets the name of the configuration property or setting that caused this error.
    /// </summary>
    /// <remarks>
    /// Example: "Dimensions", "ConnectionString", "ApiKey".
    /// </remarks>
    public string? PropertyName { get; }

    /// <summary>
    /// Gets the expected value, if applicable.
    /// </summary>
    /// <remarks>
    /// Use for dimension mismatch scenarios where the expected dimension is known.
    /// </remarks>
    public object? ExpectedValue { get; }

    /// <summary>
    /// Gets the actual value that was provided.
    /// </summary>
    public object? ActualValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public NetIndexConfigurationException(string? message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public NetIndexConfigurationException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance with structured configuration data.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="propertyName">The configuration property that failed validation.</param>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="actualValue">The actual value provided.</param>
    public NetIndexConfigurationException(string? message, string? propertyName, object? expectedValue, object? actualValue)
        : base(message)
    {
        PropertyName = propertyName;
        ExpectedValue = expectedValue;
        ActualValue = actualValue;
    }

    /// <summary>
    /// Initializes a new instance with structured configuration data and an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="propertyName">The configuration property that failed validation.</param>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="actualValue">The actual value provided.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public NetIndexConfigurationException(string? message, string? propertyName, object? expectedValue, object? actualValue, Exception? innerException)
        : base(message, innerException)
    {
        PropertyName = propertyName;
        ExpectedValue = expectedValue;
        ActualValue = actualValue;
    }
}
