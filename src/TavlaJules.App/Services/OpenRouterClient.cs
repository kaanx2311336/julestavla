using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class OpenRouterClient
{
    private static readonly HttpClient HttpClient = new();

    public async Task<string> TestConnectionAsync(ProjectSettings settings, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENROUTER_API_KEY .env icinde bulunamadi.");
        }

        var errors = new List<string>();
        foreach (var model in GetModelChain(settings))
        {
            try
            {
                var content = await SendTestRequestAsync(settings, apiKey, model, cancellationToken);
                return $"[{model}] {content}";
            }
            catch (OpenRouterRequestException exception) when (exception.IsRetryable)
            {
                errors.Add($"{model}: {exception.Message}");
            }
        }

        throw new InvalidOperationException("OpenRouter fallback zinciri basarisiz: " + string.Join(" | ", errors));
    }

    public async Task<OpenRouterCompletionResult> CompleteAsync(
        ProjectSettings settings,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        int maxTokens = 1600,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENROUTER_API_KEY .env icinde bulunamadi.");
        }

        var errors = new List<string>();
        foreach (var model in GetModelChain(settings, settings.AgentModel))
        {
            try
            {
                var content = await SendCompletionRequestAsync(settings, apiKey, model, systemPrompt, userPrompt, maxTokens, cancellationToken);
                return new OpenRouterCompletionResult
                {
                    Model = model,
                    Content = content
                };
            }
            catch (OpenRouterRequestException exception) when (exception.IsRetryable)
            {
                errors.Add($"{model}: {exception.Message}");
            }
        }

        throw new InvalidOperationException("OpenRouter ajan fallback zinciri basarisiz: " + string.Join(" | ", errors));
    }

    private static async Task<string> SendTestRequestAsync(ProjectSettings settings, string apiKey, string model, CancellationToken cancellationToken)
    {
        return await SendCompletionRequestAsync(
            settings,
            apiKey,
            model,
            "",
            "TavlaJules OpenRouter baglanti testi. Sadece 'baglanti tamam' yaz.",
            32,
            cancellationToken);
    }

    private static async Task<string> SendCompletionRequestAsync(
        ProjectSettings settings,
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.OpenRouterEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", $"https://github.com/{settings.GitHubRepo}");
        request.Headers.TryAddWithoutValidation("X-Title", "TavlaJules");

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        messages.Add(new { role = "user", content = userPrompt });

        var payload = new
        {
            model,
            messages,
            temperature = 0,
            max_tokens = maxTokens
        };

        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            throw new OpenRouterRequestException(
                $"OpenRouter hata verdi: {statusCode} {response.ReasonPhrase} - {TrimForLog(responseText)}",
                IsRetryable(statusCode));
        }

        using var document = JsonDocument.Parse(responseText);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(content) ? "Baglanti basarili, bos yanit geldi." : content.Trim();
    }

    private static IReadOnlyList<string> GetModelChain(ProjectSettings settings, string? preferredModel = null)
    {
        var models = new List<string>();
        AddModel(preferredModel ?? settings.OpenRouterModel);

        foreach (var model in settings.OpenRouterFallbackModels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddModel(model);
        }

        return models;

        void AddModel(string model)
        {
            if (!string.IsNullOrWhiteSpace(model) && !models.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                models.Add(model);
            }
        }
    }

    private static bool IsRetryable(int statusCode)
    {
        return statusCode is 400 or 408 or 429 or 500 or 502 or 503 or 504;
    }

    private static string TrimForLog(string value)
    {
        const int maxLength = 500;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed class OpenRouterRequestException(string message, bool isRetryable) : Exception(message)
    {
        public bool IsRetryable { get; } = isRetryable;
    }
}
