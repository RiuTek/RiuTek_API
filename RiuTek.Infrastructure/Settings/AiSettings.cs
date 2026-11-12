using RiuTek.Application.Common.Interfaces;

namespace RiuTek.Infrastructure.Settings;

public class AiSettings : IAiRuntimeSettings
{
    public const string SectionName = "Ai";

    public bool Enabled { get; set; } = false;
    public bool AllowAnonymous { get; set; } = false;
    public bool WebSearchEnabled { get; set; } = false;
    public GroqSettings Groq { get; set; } = new();
    public AiLimitsSettings Limits { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Groq.ApiKey) &&
        !Groq.ApiKey.Trim().StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) &&
        !Groq.ApiKey.Trim().StartsWith("placeholder", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Groq.Model);
}

public class GroqSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "openai/gpt-oss-20b";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/";
    public int TimeoutSeconds { get; set; } = 25;
}

public class AiLimitsSettings
{
    public int TurnsPer15Minutes { get; set; } = 5;
    public int ProviderRequestsPerDay { get; set; } = 100;
    public int WebRequestsPerDay { get; set; } = 20;
}
