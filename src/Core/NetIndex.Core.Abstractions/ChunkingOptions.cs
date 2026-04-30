namespace NetIndex.Core.Abstractions;

/// <summary>
/// Configuration options for text chunking strategies.
/// </summary>
/// <remarks>
/// <para>Constraints:</para>
/// <list type="bullet">
///   <item><term><see cref="ChunkSize"/></term><description>Must be &gt; 0.</description></item>
///   <item><term><see cref="ChunkOverlap"/></term><description>Must be &gt;= 0 and &lt; <see cref="ChunkSize"/>.</description></item>
///   <item><term><see cref="Separator"/></term><description>Must not be empty. Interpreted as a literal string or regex pattern depending on the strategy.</description></item>
/// </list>
/// <para>Implementations should validate these constraints and throw <see cref="ArgumentException"/> for invalid values.</para>
/// </remarks>
/// <param name="ChunkSize">Target number of tokens or characters per chunk (must be &gt; 0).</param>
/// <param name="ChunkOverlap">Number of tokens or characters to overlap between consecutive chunks (must be &gt;= 0 and &lt; ChunkSize).</param>
/// <param name="Separator">String or regex pattern used to split text before chunking (must not be empty).</param>
public record ChunkingOptions(
    int ChunkSize,
    int ChunkOverlap,
    string Separator);
