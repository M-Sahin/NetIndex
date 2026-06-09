using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NetIndex.Ingestion.Tesseract.Options;

/// <summary>
/// Validates <see cref="TesseractOptions"/> at build time without loading any native binary.
/// </summary>
public sealed class TesseractOptionsValidator : IValidateOptions<TesseractOptions>
{
    private static readonly Regex LanguageNamePattern = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TesseractOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TessDataPath))
        {
            return ValidateOptionsResult.Fail("TesseractOptions.TessDataPath must not be empty.");
        }

        var envPrefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrWhiteSpace(envPrefix) &&
            !string.Equals(options.TessDataPath, envPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"TesseractOptions.TessDataPath ('{options.TessDataPath}') conflicts with the " +
                $"TESSDATA_PREFIX environment variable ('{envPrefix}'). " +
                "Clear one of them to avoid ambiguity.");
        }

        if (options.RasterizationDpi < 72 || options.RasterizationDpi > 600)
        {
            return ValidateOptionsResult.Fail(
                $"TesseractOptions.RasterizationDpi must be between 72 and 600 (got {options.RasterizationDpi}).");
        }

        if (options.MaxInputBytes <= 0)
        {
            return ValidateOptionsResult.Fail("TesseractOptions.MaxInputBytes must be positive.");
        }

        if (options.MaxPages <= 0)
        {
            return ValidateOptionsResult.Fail("TesseractOptions.MaxPages must be positive.");
        }

        if (options.MaxPixelsPerPage <= 0)
        {
            return ValidateOptionsResult.Fail("TesseractOptions.MaxPixelsPerPage must be positive.");
        }

        if (!Directory.Exists(options.TessDataPath))
        {
            return ValidateOptionsResult.Fail(
                $"TesseractOptions.TessDataPath directory '{options.TessDataPath}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(options.Languages))
        {
            return ValidateOptionsResult.Fail("TesseractOptions.Languages must not be empty.");
        }

        var languages = options.Languages.Split('+');
        foreach (var lang in languages)
        {
            var normalizedLanguage = lang.Trim();
            if (normalizedLanguage.Length == 0 || !LanguageNamePattern.IsMatch(normalizedLanguage))
            {
                return ValidateOptionsResult.Fail(
                    $"TesseractOptions.Languages contains an invalid language token: '{lang}'.");
            }

            var dataFile = Path.Combine(options.TessDataPath, normalizedLanguage + ".traineddata");
            if (!File.Exists(dataFile))
            {
                return ValidateOptionsResult.Fail(
                    $"Tesseract trained-data file not found: '{dataFile}'. " +
                    "Download it from https://github.com/tesseract-ocr/tessdata_fast.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
