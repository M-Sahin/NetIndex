using System;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Base exception for all NetIndex framework errors.
/// </summary>
/// <remarks>
/// All NetIndex-specific exceptions derive from this class, enabling a single
/// <c>catch (NetIndexException)</c> clause to handle all framework errors without
/// catching upstream SDK exceptions.
/// 
/// Subtypes:
/// <list type="bullet">
///   <item><term><see cref="NetIndexConfigurationException"/></term><description>Configuration errors (e.g., dimension mismatch).</description></item>
///   <item><term><see cref="NetIndexAuthorizationException"/></term><description>Authorization failures (e.g., tenant resolution failed).</description></item>
///   <item><term><see cref="NetIndexOcrNotInstalledException"/></term><description>OCR package not available.</description></item>
///   <item><term><see cref="NetIndexProviderException"/></term><description>Provider failures (e.g., LLM, embedding).</description></item>
///   <item><term><see cref="NetIndexStorageException"/></term><description>Vector store failures.</description></item>
/// </list>
/// </remarks>
public abstract class NetIndexException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    protected NetIndexException(string? message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of this exception.</param>
    protected NetIndexException(string? message, Exception? innerException) : base(message, innerException) { }
}
