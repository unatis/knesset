using System.Text.Encodings.Web;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Kneset.Core.Abstractions;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Перевод готового разбора на другой язык через Claude.
///
/// Модель дешевле, чем у анализа, и это обосновано: на входе не документ
/// на иврите, а уже готовый структурированный разбор, и решать, чего в тексте
/// нет, здесь не нужно — именно на этом решении модели и расходятся.
///
/// Усилие medium, а не high: перевод механичнее анализа, но на low модели
/// путают омонимы — на переводах названий законопроектов Sonnet прочёл
/// «העצמאיות» (самозанятые) как «независимость».
/// </summary>
public class ClaudeAnalysisTranslator(
    AnthropicClient client,
    string model = "claude-sonnet-5") : IAnalysisTranslator
{
    public string ModelVersion => model;

    public async Task<BillAnalysisResult> TranslateAsync(
        BillAnalysisResult source, string targetLanguage, CancellationToken ct = default)
    {
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = new List<TextBlockParam>
            {
                new()
                {
                    Text = TranslationPrompt.System,
                    CacheControl = new CacheControlEphemeral(),
                },
            },
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Medium,
                Format = new JsonOutputFormat { Schema = AnalysisJsonSchema.ForClaude() },
            },
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = TranslationPrompt.User(source, targetLanguage),
                },
            ],
        }, ct);

        var json = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(b => b.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?? throw new InvalidOperationException(
                $"Модель {model} не вернула текстовый блок с переводом");

        return JsonSerializer.Deserialize<BillAnalysisResult>(json)
               ?? throw new InvalidOperationException(
                   $"Перевод от {model} не разобрался по схеме BillAnalysisResult");
    }
}

/// <summary>
/// Промпт перевода, общий для провайдеров: у Claude и Gemini различается
/// транспорт, а требования к переводу одни и те же.
/// </summary>
internal static class TranslationPrompt
{
    public const string System = """
        Ты переводишь готовый разбор законопроекта Кнессета на другой язык.

        Это перевод, а не новый анализ. Содержание менять нельзя: политика
        платформы запрещает смягчать или усиливать выводы в зависимости
        от языка аудитории.

        Требования:
        - Сохрани структуру в точности: столько же пунктов в каждом разделе,
          в том же порядке, с теми же значениями kind, confidence и effect.
        - source_ref переводи как указание места, но указывай ровно то же
          место: «Пояснительная записка, абзац 5» остаётся тем же абзацем.
        - Ничего не добавляй и не убирай — ни пунктов, ни оговорок, ни оценок.
        - Юридические термины передавай принятыми в целевом языке
          эквивалентами, а не калькой.
        - Иврит не воспроизводи: при переносе ивритских слов в другую
          письменность модели подставляют внутрь слова чужие буквы.
        - Ответ строго по JSON-схеме, без текста вокруг.
        """;

    public static string User(BillAnalysisResult source, string targetLanguage) => $"""
        Целевой язык: {targetLanguage}

        Разбор для перевода:
        {JsonSerializer.Serialize(source, JsonOptions)}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Без экранирования не-ASCII: иначе иврит и кириллица уедут
        // в \uXXXX и промпт распухнет в разы. Полный путь к энкодеру писать
        // нельзя: константа System в этом классе затеняет пространство имён.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}
