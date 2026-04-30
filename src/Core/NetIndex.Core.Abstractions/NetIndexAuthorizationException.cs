using System;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Thrown when authorization fails — e.g., tenant resolution fails or access is denied.
/// </summary>
/// <remarks>
/// The default tenant resolver (<c>DenyAllTenantResolver</c>) throws this exception
/// on every call, enforcing deny-all authorization by default (FR9).
/// 
/// Consumers should catch this exception to handle unauthenticated or unauthorized requests.
/// </remarks>
public class NetIndexAuthorizationException : NetIndexException
{
    /// <summary>
    /// Gets the tenant ID that was attempted, if available.
    /// </summary>
    public string? TenantId { get; }

    /// <summary>
    /// Gets the required claim that was missing or invalid.
    /// </summary>
    public string? RequiredClaim { get; }

    /// <summary>
    /// Gets a description of why authorization failed.
    /// </summary>
    /// <remarks>
    /// Example values: "NoTenantResolverConfigured", "MissingTenantIdClaim", "InsufficientPermissions".
    /// </remarks>
    public string? FailureReason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexAuthorizationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public NetIndexAuthorizationException(string? message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexAuthorizationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public NetIndexAuthorizationException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance with structured authorization data.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="tenantId">The attempted tenant ID, if available.</param>
    /// <param name="requiredClaim">The missing or invalid claim.</param>
    /// <param name="failureReason">The reason for the authorization failure.</param>
    public NetIndexAuthorizationException(string? message, string? tenantId, string? requiredClaim, string? failureReason)
        : base(message)
    {
        TenantId = tenantId;
        RequiredClaim = requiredClaim;
        FailureReason = failureReason;
    }
}
