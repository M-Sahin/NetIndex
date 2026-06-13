using System;
using System.Collections.Generic;
using NetIndex.Core.Abstractions;

namespace NetIndex.SemanticKernel.Internal;

/// <summary>
/// Minimal <see cref="IDocument"/> implementation used to ingest agent-supplied content
/// through the <c>IngestDocument</c> plugin function.
/// </summary>
internal sealed class PluginDocument : IDocument
{
    public PluginDocument(string id, string content)
    {
        Id = id;
        Content = content;
    }

    public string Id { get; }

    public string Content { get; }

    public IReadOnlyDictionary<string, string>? Metadata => null;

    public Uri? SourceUri => null;
}
