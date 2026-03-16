namespace DiscordAiModeration.Infrastructure.Options;

public sealed class AiProviderOptions
{
    public string Provider { get; set; } = "openai";

    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiModel { get; set; } = "gpt-5-mini";

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.2";
}
