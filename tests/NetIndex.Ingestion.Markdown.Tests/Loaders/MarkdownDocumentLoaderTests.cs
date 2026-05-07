using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NetIndex.Ingestion.Markdown.Loaders;
using Xunit;

namespace NetIndex.Ingestion.Markdown.Tests.Loaders;

/// <summary>
/// Unit tests for <see cref="MarkdownDocumentLoader"/>.
/// </summary>
public sealed class MarkdownDocumentLoaderTests
{
    private static Stream GetMarkdownStream(string filename)
    {
        var assembly = typeof(MarkdownDocumentLoaderTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith(filename, StringComparison.OrdinalIgnoreCase));
        return assembly.GetManifestResourceStream(resourceName)!;
    }

    private static string WriteTempMarkdown(string filename)
    {
        var path = Path.Combine(Path.GetTempPath(), $"netindex-test-{Guid.NewGuid():N}-{filename}");
        using var src = GetMarkdownStream(filename);
        using var dst = File.Create(path);
        src.CopyTo(dst);
        return path;
    }

    private static Stream StringStream(string text, Encoding? encoding = null)
        => new MemoryStream((encoding ?? Encoding.UTF8).GetBytes(text));

    /// <summary>Verifies that YAML front matter keys and values are extracted into metadata.</summary>
    [Fact]
    public async Task LoadAsync_WithFrontMatter_ExtractsMetadataAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = GetMarkdownStream("sample-with-frontmatter.md");
        var doc = await loader.LoadAsync(stream);
        doc.Metadata.Should().ContainKey("title");
        doc.Metadata!["title"].Should().Be("Test Document");
        doc.Metadata.Should().ContainKey("author");
        doc.Metadata["author"].Should().Be("Test Author");
    }

    /// <summary>Verifies that has_front_matter is "true" when front matter is present.</summary>
    [Fact]
    public async Task LoadAsync_WithFrontMatter_HasFrontMatterIsTrueAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = GetMarkdownStream("sample-with-frontmatter.md");
        var doc = await loader.LoadAsync(stream);
        doc.Metadata!["has_front_matter"].Should().Be("true");
    }

    /// <summary>Verifies that has_front_matter is "false" when no front matter block is present.</summary>
    [Fact]
    public async Task LoadAsync_WithoutFrontMatter_HasFrontMatterIsFalseAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = GetMarkdownStream("sample-no-frontmatter.md");
        var doc = await loader.LoadAsync(stream);
        doc.Metadata!["has_front_matter"].Should().Be("false");
    }

    /// <summary>Verifies that content does not include the front matter delimiters or key/value pairs.</summary>
    [Fact]
    public async Task LoadAsync_ContentExcludesFrontMatterAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = GetMarkdownStream("sample-with-frontmatter.md");
        var doc = await loader.LoadAsync(stream);
        doc.Content.Should().NotContain("---");
        doc.Content.Should().Contain("Body content here.");
    }

    /// <summary>Verifies that SourceUri is null when loading from a bare stream.</summary>
    [Fact]
    public async Task LoadAsync_FromStream_SourceUriIsNullAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = GetMarkdownStream("sample-no-frontmatter.md");
        var doc = await loader.LoadAsync(stream);
        doc.SourceUri.Should().BeNull();
    }

    /// <summary>Verifies that SourceUri is set to a file URI when loading from a file path.</summary>
    [Fact]
    public async Task LoadAsync_FromFilePath_SetsSourceUriAsync()
    {
        var loader = new MarkdownDocumentLoader();
        var path = WriteTempMarkdown("sample-no-frontmatter.md");
        try
        {
            var doc = await loader.LoadAsync(path);
            doc.SourceUri.Should().NotBeNull();
            doc.SourceUri!.IsFile.Should().BeTrue();
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
        var loader = new MarkdownDocumentLoader();
        var path = WriteTempMarkdown("sample-no-frontmatter.md");
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
        var loader = new MarkdownDocumentLoader();
        await using var stream = GetMarkdownStream("sample-no-frontmatter.md");
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
        var loader = new MarkdownDocumentLoader();
        var act = () => loader.LoadAsync((Stream)null!);
        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>Verifies that a null file path throws ArgumentException.</summary>
    [Fact]
    public Task LoadAsync_NullFilePath_ThrowsArgumentExceptionAsync()
    {
        var loader = new MarkdownDocumentLoader();
        var act = () => loader.LoadAsync((string)null!);
        return act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>Verifies that a whitespace file path throws ArgumentException.</summary>
    [Fact]
    public Task LoadAsync_WhitespaceFilePath_ThrowsArgumentExceptionAsync()
    {
        var loader = new MarkdownDocumentLoader();
        var act = () => loader.LoadAsync("   ");
        return act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>Verifies that a null directory path throws ArgumentException immediately (eager validation).</summary>
    [Fact]
    public void LoadDirectoryAsync_NullDirectoryPath_ThrowsArgumentException()
    {
        var loader = new MarkdownDocumentLoader();
        Action act = () => loader.LoadDirectoryAsync(null!);
        act.Should().Throw<ArgumentException>();
    }

    // --- P13: edge cases ---

    /// <summary>Verifies that an empty file returns empty body and has_front_matter=false.</summary>
    [Fact]
    public async Task LoadAsync_EmptyContent_ReturnsEmptyBodyAndNoFrontMatterAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = StringStream(string.Empty);
        var doc = await loader.LoadAsync(stream);
        doc.Content.Should().BeEmpty();
        doc.Metadata!["has_front_matter"].Should().Be("false");
    }

    /// <summary>Verifies that unclosed front matter strips the opener line and marks has_front_matter=false.</summary>
    [Fact]
    public async Task LoadAsync_UnclosedFrontMatter_StripsOpenerAndMarksAbsentAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = StringStream("---\ntitle: Oops\n# heading\nbody");
        var doc = await loader.LoadAsync(stream);
        doc.Metadata!["has_front_matter"].Should().Be("false");
        doc.Content.Should().NotStartWith("---");
        doc.Content.Should().Contain("body");
    }

    /// <summary>Verifies that CRLF line endings inside front matter are parsed without trailing CR in values.</summary>
    [Fact]
    public async Task LoadAsync_CrlfFrontMatter_ParsesCleanlyAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = StringStream("---\r\ntitle: Test Document\r\nauthor: Test Author\r\n---\r\n# Heading\r\nBody.");
        var doc = await loader.LoadAsync(stream);
        doc.Metadata!["title"].Should().Be("Test Document");
        doc.Metadata!["author"].Should().Be("Test Author");
        doc.Metadata!["has_front_matter"].Should().Be("true");
        doc.Content.Should().NotContain("---");
        doc.Content.Should().Contain("Body.");
    }

    /// <summary>Verifies that a UTF-8 BOM at the start of the file does not prevent front matter detection.</summary>
    [Fact]
    public async Task LoadAsync_Utf8BomFrontMatter_DetectsFrontMatterAsync()
    {
        var loader = new MarkdownDocumentLoader();
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("---\ntitle: Bom\n---\nBody"))
            .ToArray();
        await using var stream = new MemoryStream(bytes);
        var doc = await loader.LoadAsync(stream);
        doc.Metadata!["has_front_matter"].Should().Be("true");
        doc.Metadata!["title"].Should().Be("Bom");
    }

    /// <summary>Verifies that `---abc` is not treated as a front matter opening fence.</summary>
    [Fact]
    public async Task LoadAsync_OpenerNotOnOwnLine_NotTreatedAsFrontMatterAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = StringStream("---abc\nbody\n---\nmore");
        var doc = await loader.LoadAsync(stream);
        doc.Metadata!["has_front_matter"].Should().Be("false");
    }

    /// <summary>Verifies that front matter key casing is preserved verbatim (no lowercasing).</summary>
    [Fact]
    public async Task LoadAsync_PreservesOriginalKeyCasingAsync()
    {
        var loader = new MarkdownDocumentLoader();
        await using var stream = StringStream("---\nTitle: Cased\n---\nbody");
        var doc = await loader.LoadAsync(stream);
        doc.Metadata.Should().ContainKey("Title");
        doc.Metadata!["Title"].Should().Be("Cased");
    }

    // --- P11: directory ingestion ---

    /// <summary>Verifies that recursive=true loads .md, .markdown, and case-variant extensions from nested folders.</summary>
    [Fact]
    public async Task LoadDirectoryAsync_RecursiveTrue_LoadsAllMatchingFilesAsync()
    {
        var loader = new MarkdownDocumentLoader();
        var root = Path.Combine(Path.GetTempPath(), $"netindex-md-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "sub");
        Directory.CreateDirectory(nested);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.md"), "# a\nbody");
            await File.WriteAllTextAsync(Path.Combine(nested, "b.markdown"), "# b\nbody");

            var docs = new System.Collections.Generic.List<NetIndex.Core.Abstractions.IDocument>();
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
        var loader = new MarkdownDocumentLoader();
        var root = Path.Combine(Path.GetTempPath(), $"netindex-md-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "sub");
        Directory.CreateDirectory(nested);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.md"), "# a");
            await File.WriteAllTextAsync(Path.Combine(nested, "b.md"), "# b");

            var docs = new System.Collections.Generic.List<NetIndex.Core.Abstractions.IDocument>();
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
