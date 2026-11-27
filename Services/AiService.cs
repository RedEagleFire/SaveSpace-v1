using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SaveSpace.Services
{
    public class AiService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
        private const string AnthropicVersion = "2023-06-01";
        private const int AnthropicMaxTokens = 2048;
        private readonly SettingsService _settings;

        public AiService(SettingsService settings)
        {
            _settings = settings;
        }

        public bool IsConfigured => _settings.AiProvider is "ollama" or "openai" or "anthropic" or "compatible";

        public string ProviderLabel => _settings.AiProvider switch
        {
            "ollama" => "Ollama (lokaal)",
            "openai" => "OpenAI",
            "anthropic" => "Anthropic",
            "compatible" => "OpenAI-compatibel",
            _ => "Niet ingesteld"
        };

        public static string CredentialTarget(string provider) => "SaveSpace.AI." + provider;

        public bool HasKey(string provider) => !string.IsNullOrEmpty(CredentialStore.Read(CredentialTarget(provider)));

        public bool SaveKey(string provider, string key) => CredentialStore.Save(CredentialTarget(provider), key);

        public void DeleteKey(string provider) => CredentialStore.Delete(CredentialTarget(provider));

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            var provider = _settings.AiProvider;
            if (provider is "none" or "")
                throw new AiException("Er is nog geen AI-provider ingesteld. Open Instellingen → AI en kies een provider.");
            var model = string.IsNullOrWhiteSpace(_settings.AiModel) ? "llama3.2" : _settings.AiModel.Trim();
            var endpoint = string.IsNullOrWhiteSpace(_settings.AiEndpoint) ? DefaultEndpoint(provider) : _settings.AiEndpoint.Trim();

            return provider switch
            {
                "ollama" => await OllamaAsync(endpoint, model, systemPrompt, userPrompt, ct),
                "anthropic" => await AnthropicAsync(endpoint, model, systemPrompt, userPrompt, ct),
                "openai" or "compatible" => await OpenAiAsync(endpoint, model, systemPrompt, userPrompt, provider, ct),
                _ => throw new AiException("Onbekende AI-provider.")
            };
        }

        public static string DefaultEndpointFor(string provider) => DefaultEndpoint(provider);

        private static string DefaultEndpoint(string provider) => provider switch
        {
            "ollama" => "http://localhost:11434",
            "openai" => "https://api.openai.com",
            "anthropic" => "https://api.anthropic.com",
            _ => ""
        };

        /// <summary>Sleutel is verplicht voor de clouddiensten; een lokale OpenAI-compatibele server heeft er vaak geen.</summary>
        private string RequireKey(string provider)
        {
            var key = CredentialStore.Read(CredentialTarget(provider));
            if (string.IsNullOrWhiteSpace(key))
                throw new AiException("Er is geen API-sleutel opgeslagen. Vul die in bij Instellingen → AI. De sleutel wordt veilig bewaard in Windows Referentiebeheer.");
            return key;
        }

        private static string OptionalKey(string provider) =>
            CredentialStore.Read(CredentialTarget(provider)) ?? "";

        /// <summary>Een API-sleutel mag alleen over een versleutelde verbinding (of naar je eigen computer).</summary>
        private static void EnsureSecureEndpoint(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                throw new AiException("Het endpoint is geen geldig webadres.");
            if (uri.Scheme == Uri.UriSchemeHttps) return;
            if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) return;
            throw new AiException("Om je API-sleutel veilig te versturen moet het endpoint https gebruiken.");
        }

        private static string JoinUrl(string endpoint, string suffix)
        {
            endpoint = endpoint.TrimEnd('/');
            if (endpoint.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return endpoint;
            return endpoint + suffix;
        }

        /// <summary>Maakt van een basis-URL een /v1-URL zonder dubbele /v1 als die er al in staat.</summary>
        private static string V1Url(string endpoint, string path)
        {
            var baseUrl = endpoint.TrimEnd('/');
            if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl += "/v1";
            return baseUrl + path;
        }

        private async Task<string> OllamaAsync(string endpoint, string model, string system, string user, CancellationToken ct)
        {
            var url = JoinUrl(endpoint, "/api/chat");
            var body = new
            {
                model,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            using var res = await SendAsync(req, ct, "Ollama");
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                return content.GetString()?.Trim() ?? "";
            throw new AiException("Ollama gaf een onverwacht antwoord. Controleer of het model draait.");
        }

        private async Task<string> OpenAiAsync(string endpoint, string model, string system, string user, string provider, CancellationToken ct)
        {
            var key = provider == "compatible" ? OptionalKey(provider) : RequireKey(provider);
            if (!string.IsNullOrWhiteSpace(key)) EnsureSecureEndpoint(endpoint);

            var url = endpoint.TrimEnd('/');
            if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                url = V1Url(url, "/chat/completions");

            // Geen temperature meegeven: sommige nieuwere modellen weigeren een eigen waarde.
            var body = new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await SendAsync(req, ct, "OpenAI");
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                    return content.GetString()?.Trim() ?? "";
            }
            throw new AiException("De AI-provider gaf een onverwacht antwoord.");
        }

        private async Task<string> AnthropicAsync(string endpoint, string model, string system, string user, CancellationToken ct)
        {
            var key = RequireKey("anthropic");
            EnsureSecureEndpoint(endpoint);
            var url = endpoint.TrimEnd('/');
            if (!url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
                url = V1Url(url, "/messages");
            var body = new
            {
                model,
                max_tokens = AnthropicMaxTokens,
                system,
                messages = new object[] { new { role = "user", content = user } }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", key);
            req.Headers.Add("anthropic-version", AnthropicVersion);
            using var res = await SendAsync(req, ct, "Anthropic");
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
            {
                var text = content[0].TryGetProperty("text", out var t) ? t.GetString() : null;
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            throw new AiException("Anthropic gaf een onverwacht antwoord.");
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct, string label)
        {
            HttpResponseMessage res;
            try
            {
                res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new AiException($"{label} reageerde niet op tijd. Controleer je internetverbinding of endpoint.");
            }
            catch (HttpRequestException ex)
            {
                throw new AiException($"{label} is niet bereikbaar: {ex.Message}");
            }

            if (!res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                var snippet = text.Length > 300 ? text[..300] + "…" : text;
                res.Dispose();
                throw new AiException($"{label} gaf fout {(int)res.StatusCode}. {snippet}");
            }
            return res;
        }

        public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
        {
            var provider = _settings.AiProvider;
            var endpoint = string.IsNullOrWhiteSpace(_settings.AiEndpoint) ? DefaultEndpoint(provider) : _settings.AiEndpoint.Trim();
            var models = new List<string>();
            if (provider == "ollama")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, JoinUrl(endpoint, "/api/tags"));
                using var res = await SendAsync(req, ct, "Ollama");
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("models", out var arr))
                    foreach (var model in arr.EnumerateArray())
                        if (model.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } value)
                            models.Add(value);
            }
            else if (provider is "openai" or "compatible")
            {
                var key = provider == "compatible" ? OptionalKey(provider) : RequireKey(provider);
                if (!string.IsNullOrWhiteSpace(key)) EnsureSecureEndpoint(endpoint);
                using var req = new HttpRequestMessage(HttpMethod.Get, V1Url(endpoint, "/models"));
                if (!string.IsNullOrWhiteSpace(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var res = await SendAsync(req, ct, "OpenAI");
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("data", out var arr))
                    foreach (var model in arr.EnumerateArray())
                        if (model.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value)
                            models.Add(value);
            }
            else if (provider == "anthropic")
            {
                // Modellenlijst komt live van Anthropic, zodat er geen verouderde namen in de lijst staan.
                var key = RequireKey("anthropic");
                EnsureSecureEndpoint(endpoint);
                using var req = new HttpRequestMessage(HttpMethod.Get, V1Url(endpoint, "/models"));
                req.Headers.Add("x-api-key", key);
                req.Headers.Add("anthropic-version", AnthropicVersion);
                using var res = await SendAsync(req, ct, "Anthropic");
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("data", out var arr))
                    foreach (var model in arr.EnumerateArray())
                        if (model.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value)
                            models.Add(value);
            }
            else
            {
                throw new AiException("Kies eerst een AI-provider.");
            }
            models.Sort(StringComparer.OrdinalIgnoreCase);
            return models;
        }

        public static readonly (string Key, string Label, string Icon, string Hint)[] Providers =
        {
            ("ollama", "Ollama", "\uE950", "Draait lokaal op je computer. Geen sleutel nodig."),
            ("openai", "OpenAI", "\uE8F2", "Gebruikt de OpenAI API met een persoonlijke sleutel."),
            ("anthropic", "Anthropic", "\uE8D4", "Claude via de Anthropic API."),
            ("compatible", "OpenAI-compatibel", "\uE774", "Elke dienst met een OpenAI-compatibel endpoint. Sleutel is optioneel, bijvoorbeeld voor lokale servers.")
        };
    }
}
