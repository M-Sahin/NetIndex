namespace NetIndex.Testing.Common;

/// <summary>
/// Centralized constants for test collection names. Prevents naming divergence across test projects.
/// </summary>
public static class TestingConstants
{
    /// <summary>Collection name constants grouped by area.</summary>
    public static class Collections
    {
        /// <summary>pgvector storage tests: "NetIndex.Storage.Pgvector"</summary>
        public const string Pgvector = "NetIndex.Storage.Pgvector";

        /// <summary>SQLite storage tests: "NetIndex.Storage.Sqlite"</summary>
        public const string Sqlite = "NetIndex.Storage.Sqlite";

        /// <summary>In-memory storage tests: "NetIndex.Storage.InMemory"</summary>
        public const string InMemory = "NetIndex.Storage.InMemory";

        /// <summary>Ollama provider tests: "NetIndex.Providers.Ollama"</summary>
        public const string Ollama = "NetIndex.Providers.Ollama";

        /// <summary>OpenAI provider tests: "NetIndex.Providers.OpenAI"</summary>
        public const string OpenAI = "NetIndex.Providers.OpenAI";

        /// <summary>Azure OpenAI provider tests: "NetIndex.Providers.AzureOpenAI"</summary>
        public const string AzureOpenAI = "NetIndex.Providers.AzureOpenAI";

        /// <summary>Tesseract OCR native tests (requires Tesseract installed): "NetIndex.Ingestion.Tesseract.Native"</summary>
        public const string Tesseract = "NetIndex.Ingestion.Tesseract.Native";

        /// <summary>Tesseract managed tests that mutate the process-global TESSDATA_PREFIX env var: "NetIndex.Ingestion.Tesseract.OptionsEnv"</summary>
        public const string TesseractOptionsEnv = "NetIndex.Ingestion.Tesseract.OptionsEnv";
    }
}
