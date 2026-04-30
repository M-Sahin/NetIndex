using System;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Thrown when the OCR package is not installed or not available.
/// </summary>
/// <remarks>
/// This exception is thrown by <c>NetIndex.Ingestion.Tesseract</c> when the Tesseract
/// native binaries are not found on the system. The <see cref="InstallInstructions"/>
/// property provides a directed message to help the user install the missing dependency.
/// </remarks>
public class NetIndexOcrNotInstalledException : NetIndexException
{
    /// <summary>
    /// Gets the name of the required OCR package.
    /// </summary>
    /// <remarks>
    /// Example: "tesseract-ocr", "NetIndex.Ingestion.Tesseract".
    /// </remarks>
    public string? RequiredPackage { get; }

    /// <summary>
    /// Gets instructions for installing the required OCR dependency.
    /// </summary>
    /// <remarks>
    /// Example: "Install Tesseract OCR via your package manager: sudo apt-get install tesseract-ocr"
    /// </remarks>
    public string? InstallInstructions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexOcrNotInstalledException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public NetIndexOcrNotInstalledException(string? message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexOcrNotInstalledException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public NetIndexOcrNotInstalledException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance with structured installation data.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="requiredPackage">The name of the required package.</param>
    /// <param name="installInstructions">Instructions for installing the dependency.</param>
    public NetIndexOcrNotInstalledException(string? message, string? requiredPackage, string? installInstructions)
        : base(message)
    {
        RequiredPackage = requiredPackage;
        InstallInstructions = installInstructions;
    }
}
