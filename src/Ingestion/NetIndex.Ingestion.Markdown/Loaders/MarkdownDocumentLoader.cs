using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Markdown.Documents;

namespace NetIndex.Ingestion.Markdown.Loaders;

/// <summary>
/// Loads Markdown documents from streams, files, or directories.
/// </summary>
/// <remarks>
/// Front-matter parsing supports the simple <c>key: value</c> form (one pair per line, optional surrounding
/// quotes). YAML lists, nested maps, and multi-line scalars are not supported. Keys are preserved verbatim;
/// the metadata dictionary is the parsed scalar projection of the front-matter block.
/// </remarks>
public sealed class MarkdownDocumentLoader : IDocumentLoader<MarkdownFormat>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownDocumentLoader"/> class.
    /// </summary>
    public MarkdownDocumentLoader()
    {
    }

    /// <summary>
    /// Loads a Markdown document from the specified stream.
    /// <see cref="IDocument.SourceUri"/> is <see langword="null"/> when loading from a bare stream.
    /// </summary>
    /// <param name="stream">The raw Markdown stream to parse.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A populated <see cref="IDocument"/> with body content and metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    public Task<IDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return LoadCoreAsync(stream, sourceUri: null, extraMetadata: null, cancellationToken);
    }

    /// <summary>
    /// Loads a Markdown document from the specified file path.
    /// Sets <see cref="IDocument.SourceUri"/> to the full file path URI and adds <c>file_name</c> to metadata.
    /// </summary>
    /// <param name="filePath">The path to the Markdown file.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A populated <see cref="IDocument"/> with body content, metadata, and file URI.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null, empty, or whitespace.</exception>
    public async Task<IDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        var extraMetadata = new Dictionary<string, string>
        {
            ["file_name"] = Path.GetFileName(filePath)
        };
        return await LoadCoreAsync(fileStream, FileUri.Create(filePath), extraMetadata, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads all Markdown documents from the specified directory asynchronously.
    /// Matches files with <c>.md</c> or <c>.markdown</c> extensions case-insensitively.
    /// </summary>
    /// <param name="directoryPath">The directory path to scan for Markdown files.</param>
    /// <param name="recursive">Whether to include subdirectories.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An asynchronous enumerable of <see cref="IDocument"/> instances.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="directoryPath"/> is null, empty, or whitespace.</exception>
    public IAsyncEnumerable<IDocument> LoadDirectoryAsync(string directoryPath, bool recursive = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return LoadDirectoryInternalAsync(directoryPath, recursive, cancellationToken);
    }

    private async IAsyncEnumerable<IDocument> LoadDirectoryInternalAsync(string directoryPath, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directoryPath, "*", searchOption)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase));

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IDocument> LoadCoreAsync(Stream stream, Uri? sourceUri, Dictionary<string, string>? extraMetadata, CancellationToken cancellationToken)
    {
        // detectEncodingFromByteOrderMarks: true so a leading UTF-8 BOM is consumed and not retained as ﻿.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var rawText = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var (body, frontMatter, hasFrontMatter) = ExtractFrontMatter(rawText);
        var metadata = new Dictionary<string, string>
        {
            ["has_front_matter"] = hasFrontMatter ? "true" : "false"
        };
        foreach (var (key, value) in frontMatter)
        {
            metadata[key] = value;
        }

        if (extraMetadata is not null)
        {
            foreach (var (key, value) in extraMetadata)
            {
                metadata[key] = value;
            }
        }

        return new MarkdownDocument(Guid.NewGuid().ToString("N"), body, metadata, sourceUri);
    }

    private static (string body, Dictionary<string, string> frontMatter, bool hasFrontMatter) ExtractFrontMatter(string rawText)
    {
        // Strip a leading UTF-8 BOM that StreamReader did not consume (defensive — ReadToEnd may keep it on some encodings).
        if (rawText.Length > 0 && rawText[0] == '\uFEFF')
        {
            rawText = rawText[1..];
        }

        rawText = rawText.TrimStart();

        // Opening fence must be `---` on its own line (followed by LF, CRLF, or EOF).
        var openerLength = MatchOpeningFence(rawText);
        if (openerLength == 0)
        {
            return (rawText, new Dictionary<string, string>(), false);
        }

        // Search for closing fence: `\n---` followed by LF, CRLF, EOF, or whitespace-only line.
        // Scanning from after the opener so we don't re-match it.
        var closerStart = FindClosingFence(rawText, openerLength);
        if (closerStart < 0)
        {
            // Unclosed front matter — strip the opener line so body does not start with `---`,
            // and treat as if no front matter was present.
            var fallbackBody = openerLength < rawText.Length ? rawText[openerLength..] : string.Empty;
            return (fallbackBody, new Dictionary<string, string>(), false);
        }

        // closerStart points to the '\n' before `---`. Skip past `\n---` and any trailing CR/LF on that line.
        var yamlBlock = rawText[openerLength..closerStart].Trim('\r', '\n', ' ', '\t');
        var afterCloser = closerStart + 4; // length of "\n---"
        // Skip remainder of the closing-fence line (which may be `\n`, `\r\n`, or trailing spaces+EOL).
        while (afterCloser < rawText.Length && rawText[afterCloser] != '\n')
        {
            afterCloser++;
        }
        if (afterCloser < rawText.Length && rawText[afterCloser] == '\n')
        {
            afterCloser++;
        }

        var body = afterCloser < rawText.Length ? rawText[afterCloser..] : string.Empty;
        var metadata = new Dictionary<string, string>();

        // Normalize line endings inside the YAML block, then parse simple key: value pairs.
        foreach (var rawLine in yamlBlock.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var colonIdx = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIdx > 0)
            {
                var key = line[..colonIdx].Trim();
                var value = line[(colonIdx + 1)..].Trim().Trim('"', '\'');
                if (!string.IsNullOrEmpty(key))
                {
                    metadata[key] = value;
                }
            }
        }

        return (body, metadata, true);
    }

    private static int MatchOpeningFence(string text)
    {
        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return 4;
        }
        if (text.StartsWith("---\r\n", StringComparison.Ordinal))
        {
            return 5;
        }
        // Bare "---" (no trailing newline) is malformed; reject so the file is not treated as front matter.
        return 0;
    }

    private static int FindClosingFence(string text, int searchStart)
    {
        var idx = searchStart - 1;
        while (idx >= 0 && idx < text.Length)
        {
            idx = text.IndexOf("\n---", idx + 1, StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }

            // Require closing `---` to be on its own line: next char after `---` must be `\r`, `\n`, or end-of-string.
            var afterDashes = idx + 4;
            if (afterDashes == text.Length
                || text[afterDashes] == '\n'
                || text[afterDashes] == '\r')
            {
                return idx;
            }
            // False positive (e.g., `\n---abc` inside body) — keep scanning.
        }
        return -1;
    }
}

/// <summary>
/// Constructs file URIs that tolerate paths containing characters reserved by the URI grammar
/// (e.g. <c>#</c>, <c>%</c>) which the bare <see cref="Uri"/> constructor mis-parses.
/// </summary>
internal static class FileUri
{
    public static Uri Create(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var escaped = fullPath
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal);
        return new Uri(new Uri("file:///"), escaped.Replace('\\', '/'));
    }
}
