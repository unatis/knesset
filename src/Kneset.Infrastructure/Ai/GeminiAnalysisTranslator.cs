using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kneset.Core.Abstractions;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Перевод разбора через Gemini API.
///
/// Почему Gemini годится для перевода, но не для анализа: в замере он
/// заполнял нехватку данных правдоподобной позицией с названным субъектом —
/// 7 гипотетических позиций из 42 против нуля у Claude. Это порок анализа,
/// где надо решать, чего в документе нет. Перевод ничего не решает,
/// он перекладывает уже написанное, и на 2 316 названиях законопроектов
/// Gemini справился бесплатно и без нареканий.
///
/// Ограничение — не качество, а квота: на бесплатном тарифе 20 запросов
/// в сутки на каждую модель, и она общая со всеми задачами. Поэтому этот
/// переводчик самостоятельно не используется, только через
/// <see cref="FallbackAnalysisTranslator"/>.
/// </summary>
public class GeminiAnalysisTranslator(
    HttpClient http,
    string apiKey,
    string model = "gemini-3.5-flash") : IAnalysisTranslator
{
    /// <summary>Квота исчерпана — вызывающий должен уйти к другому провайдеру.</summary>
    public class QuotaExhaustedException(string message) : Exception(message);

    public async Task<AnalysisTranslation> TranslateAsync(
        BillAnalysisResult source, string targetLanguage,
        string? sourceDocument = null, CancellationToken ct = default)
    {
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = TranslationPrompt.System } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = TranslationPrompt.User(source, targetLanguage, sourceDocument) } },
                },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = AnalysisJsonSchema.ForGemini(),
                temperature = 0,
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Лимит суточный, повторять в пределах суток бессмысленно.
            throw new QuotaExhaustedException(
                $"Суточная квота Gemini для модели {model} исчерпана");
        }

        if (!response.IsSuccessStatusCode)
        {
            // Тело ответа в сообщении: без него причина 400 неотличима
            // от любой другой, и её приходится угадывать.
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Gemini {(int)response.StatusCode}: {error.ReplaceLineEndings(" ")}");
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var text = payload.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")
            .EnumerateArray()
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?? throw new InvalidOperationException(
                $"Модель {model} не вернула текст с переводом");

        var result = JsonSerializer.Deserialize<BillAnalysisResult>(text)
                     ?? throw new InvalidOperationException(
                         $"Перевод от {model} не разобрался по схеме BillAnalysisResult");

        return new AnalysisTranslation(result, model);
    }
}
