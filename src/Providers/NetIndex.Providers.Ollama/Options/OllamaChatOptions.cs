namespace NetIndex.Providers.Ollama.Options;

/// <summary>Options for the Ollama chat client (LLM generation).</summary>
public sealed class OllamaChatOptions
{
    /// <summary>Gets or sets the Ollama base URL. Default: <c>http://localhost:11434</c>.</summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Gets or sets the chat model name. Default: <c>llama3.2</c>.</summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>Gets or sets the HTTP timeout. Default: 120 seconds (chat generation is slower than embedding).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);
}
