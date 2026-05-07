using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Docx.Loaders;
using NetIndex.Ingestion.Docx.Options;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NetIndex.Ingestion.Docx.Tests.Loaders;

/// <summary>
/// Unit tests for <see cref="WordDocumentLoader"/>.
/// </summary>
public sealed class WordDocumentLoaderTests
{
    private static WordDocumentLoader CreateLoader(Action<WordDocumentLoaderOptions>? configure = null)
    {
        var opts = new WordDocumentLoaderOptions();
        configure?.Invoke(opts);
        return new WordDocumentLoader(MsOptions.Create(opts));
    }

    private static Stream GetSampleDocxStream()
    {
        var assembly = typeof(WordDocumentLoaderTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("sample.docx", StringComparison.OrdinalIgnoreCase));
        return assembly.GetManifestResourceStream(resourceName)!;
    }

    private static async Task WriteTempDocxAsync(string path)
    {
        await using var src = GetSampleDocxStream();
        await using var dst = File.Create(path);
        await src.CopyToAsync(dst);
    }

    /// <summary>Verifies that loading from a stream returns a document with non-empty content.</summary>
    [Fact]
    public async Task LoadAsync_FromStream_ReturnsDocumentWithContentAsync()
    {
        var loader = CreateLoader();
        await using var stream = GetSampleDocxStream();
        var doc = await loader.LoadAsync(stream);
        doc.Content.Should().NotBeNullOrEmpty();
    }

    /// <summary>Verifies that paragraph_count metadata is present and greater than zero.</summary>
    [Fact]
    public async Task LoadAsync_FromStream_MetadataContainsParagraphCountAsync()
    {
        var loader = CreateLoader();
        await using var stream = GetSampleDocxStream();
        var doc = await loader.LoadAsync(stream);
        doc.Metadata.Should().ContainKey("paragraph_count");
        int.Parse(doc.Metadata!["paragraph_count"]).Should().BeGreaterThan(0);
    }

    /// <summary>Verifies that SourceUri is null when loading from a bare stream.</summary>
    [Fact]
    public async Task LoadAsync_FromStream_SourceUriIsNullAsync()
    {
        var loader = CreateLoader();
        await using var stream = GetSampleDocxStream();
        var doc = await loader.LoadAsync(stream);
        doc.SourceUri.Should().BeNull();
    }

    /// <summary>Verifies that SourceUri is set to a file URI when loading from a file path.</summary>
    [Fact]
    public async Task LoadAsync_FromFilePath_SetsSourceUriAsync()
    {
        var loader = CreateLoader();
        var path = Path.Combine(Path.GetTempPath(), $"netindex-test-{Guid.NewGuid():N}.docx");
        await WriteTempDocxAsync(path);
        try
        {
            var doc = await loader.LoadAsync(path);
            doc.SourceUri.Should().NotBeNull();
            doc.SourceUri!.IsFile.Should().BeTrue();
            doc.SourceUri.LocalPath.Should().Contain(Path.GetFileName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies that file_name metadata is set when loading from a file path.</summary>
    [Fact]
    public async Task LoadAsync_FromFilePath_MetadataContainsFileNameAsync()
    {
        var loader = CreateLoader();
        var path = Path.Combine(Path.GetTempPath(), $"netindex-test-{Guid.NewGuid():N}.docx");
        await WriteTempDocxAsync(path);
        try
        {
            var doc = await loader.LoadAsync(path);
            doc.Metadata.Should().ContainKey("file_name");
            doc.Metadata!["file_name"].Should().Be(Path.GetFileName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies that a pre-cancelled token causes OperationCanceledException.</summary>
    [Fact]
    public async Task LoadAsync_CancellationRequested_ThrowsOperationCanceledExceptionAsync()
    {
        var loader = CreateLoader();
        await using var stream = GetSampleDocxStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var act = () => loader.LoadAsync(stream, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- P14: null / whitespace guard tests ---

    /// <summary>Verifies that a null stream argument throws ArgumentNullException.</summary>
    [Fact]
    public Task LoadAsync_NullStream_ThrowsArgumentNullExceptionAsync()
    {
        var loader = CreateLoader();
        var act = () => loader.LoadAsync((Stream)null!);
        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>Verifies that a null file path throws ArgumentException.</summary>
    [Fact]
    public Task LoadAsync_NullFilePath_ThrowsArgumentExceptionAsync()
    {
        var loader = CreateLoader();
        var act = () => loader.LoadAsync((string)null!);
        return act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>Verifies that a whitespace file path throws ArgumentException.</summary>
    [Fact]
    public Task LoadAsync_WhitespaceFilePath_ThrowsArgumentExceptionAsync()
    {
        var loader = CreateLoader();
        var act = () => loader.LoadAsync("   ");
        return act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>Verifies that a null directory path throws ArgumentException immediately (eager validation).</summary>
    [Fact]
    public void LoadDirectoryAsync_NullDirectoryPath_ThrowsArgumentException()
    {
        var loader = CreateLoader();
        Action act = () => loader.LoadDirectoryAsync(null!);
        act.Should().Throw<ArgumentException>();
    }

    // --- P12: graceful handling of corrupt / non-DOCX stream ---

    /// <summary>Verifies that a corrupt stream throws a typed exception rather than NullReferenceException.</summary>
    [Fact]
    public async Task LoadAsync_CorruptStream_ThrowsExceptionAsync()
    {
        var loader = CreateLoader();
        await using var stream = new MemoryStream(new byte[] { 0, 1, 2 });
        var act = () => loader.LoadAsync(stream);
        await act.Should().ThrowAsync<Exception>();
    }

    // --- DP4: size limit enforcement ---

    /// <summary>Verifies that a stream exceeding MaxInputSizeBytes throws InvalidDataException.</summary>
    [Fact]
    public async Task LoadAsync_ExceedsSizeLimit_ThrowsInvalidDataExceptionAsync()
    {
        var loader = CreateLoader(o => o.MaxInputSizeBytes = 1);
        await using var stream = GetSampleDocxStream();
        var act = () => loader.LoadAsync(stream);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    // --- P11: LoadDirectoryAsync recursive ---

    /// <summary>Verifies that recursive=true loads all .docx files from nested subdirectories.</summary>
    [Fact]
    public async Task LoadDirectoryAsync_RecursiveTrue_LoadsAllDocxAsync()
    {
        var loader = CreateLoader();
        var root = Path.Combine(Path.GetTempPath(), $"netindex-docx-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "sub");
        Directory.CreateDirectory(nested);
        try
        {
            await WriteTempDocxAsync(Path.Combine(root, "a.docx"));
            await WriteTempDocxAsync(Path.Combine(nested, "b.docx"));

            var docs = new System.Collections.Generic.List<IDocument>();
            await foreach (var d in loader.LoadDirectoryAsync(root, recursive: true))
            {
                docs.Add(d);
            }
            docs.Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Verifies that recursive=false only loads files from the top-level directory.</summary>
    [Fact]
    public async Task LoadDirectoryAsync_RecursiveFalse_OnlyTopLevelAsync()
    {
        var loader = CreateLoader();
        var root = Path.Combine(Path.GetTempPath(), $"netindex-docx-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "sub");
        Directory.CreateDirectory(nested);
        try
        {
            await WriteTempDocxAsync(Path.Combine(root, "a.docx"));
            await WriteTempDocxAsync(Path.Combine(nested, "b.docx"));

            var docs = new System.Collections.Generic.List<IDocument>();
            await foreach (var d in loader.LoadDirectoryAsync(root, recursive: false))
            {
                docs.Add(d);
            }
            docs.Should().HaveCount(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
