namespace NetIndex.Providers.Ollama.Options;

/// <summary>Options for the Ollama embedding provider.</summary>
public sealed class OllamaOptions
{
    /// <summary>Gets or sets the Ollama base URL. Default: <c>http://localhost:11434</c>.</summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Gets or sets the embedding model name. Default: <c>nomic-embed-text</c>.</summary>
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>Gets or sets expected embedding dimensions. Default: <c>768</c> (nomic-embed-text).</summary>
    public int Dimensions { get; set; } = 768;

    /// <summary>Gets or sets the HTTP timeout. Default: 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
