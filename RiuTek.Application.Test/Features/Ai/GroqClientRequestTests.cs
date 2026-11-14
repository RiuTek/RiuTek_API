using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Infrastructure.Services;
using RiuTek.Infrastructure.Settings;

namespace RiuTek.Application.Test.Features.Ai;

public class GroqClientRequestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredRequest_PreservesStrictJsonSchemaKeywords(bool synthesize)
    {
        using var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.groq.com/openai/v1/")
        };
        var client = new GroqClient(httpClient, new AiSettings
        {
            Groq = new GroqSettings { ApiKey = "test-only" }
        }, NullLogger<GroqClient>.Instance);

        if (synthesize)
        {
            var request = new GroqSynthesisRequest("Card này còn hàng không?", "product_info",
                [], new Dictionary<string, IReadOnlyDictionary<string, string>>(), [], []);
            await client.SynthesizeAnswerAsync(request);
        }
        else
        {
            await client.AnalyzeQuestionAsync("Card này còn hàng không?", "sample-gpu", null);
        }

        using var body = JsonDocument.Parse(handler.RequestBody);
        var payload = body.RootElement;
        Assert.Equal("openai/gpt-oss-20b", payload.GetProperty("model").GetString());
        Assert.True(payload.TryGetProperty("max_completion_tokens", out _));
        var responseFormat = payload.GetProperty("response_format").GetProperty("json_schema");
        Assert.True(responseFormat.GetProperty("strict").GetBoolean());
        var schema = responseFormat.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(schema.TryGetProperty("additional_properties", out _));
        if (!synthesize)
        {
            var items = schema.GetProperty("properties").GetProperty("technical_criteria")
                .GetProperty("items");
            Assert.False(items.GetProperty("additionalProperties").GetBoolean());
            Assert.False(items.TryGetProperty("additional_properties", out _));
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[]}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
