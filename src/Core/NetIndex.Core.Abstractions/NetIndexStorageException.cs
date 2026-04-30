using System;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Thrown when a vector store operation fails.
/// </summary>
/// <remarks>
/// This wraps storage-specific errors (e.g., database connection failures, query errors).
/// Consumers should catch this to handle storage outages or data integrity issues.
/// </remarks>
public class NetIndexStorageException : NetIndexException
{
    /// <summary>
    /// Gets the name of the vector store that failed.
    /// </summary>
    /// <remarks>
    /// Example: "SqliteVectorStore", "PgvectorStore", "InMemoryVectorStore".
    /// </remarks>
    public string? StoreName { get; }

    /// <summary>
    /// Gets the operation that was being performed when the error occurred.
    /// </summary>
    /// <remarks>
    /// Example: "Upsert", "Query", "Delete".
    /// </remarks>
    public string? Operation { get; }

    /// <summary>
    /// Gets the document ID involved in the operation, if applicable.
    /// </summary>
    public string? DocumentId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexStorageException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public NetIndexStorageException(string? message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexStorageException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public NetIndexStorageException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance with structured storage error data.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="storeName">The name of the vector store.</param>
    /// <param name="operation">The operation being performed.</param>
    /// <param name="documentId">The document ID involved, if applicable.</param>
    public NetIndexStorageException(string? message, string? storeName, string? operation, string? documentId)
        : base(message)
    {
        StoreName = storeName;
        Operation = operation;
        DocumentId = documentId;
    }
}
