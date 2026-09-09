using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Kneset.Core.Ai;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Один вызов модели за разбором: общая часть для законопроектов и законов.
///
/// Выделено не ради красоты. Здесь живут два дорого купленных решения —
/// потолок вывода со стримингом и проверка обрыва по лимиту до разбора
/// JSON. Пока это лежало внутри разборщика законопроектов, разборщик
/// законов начинался бы с копии, и первая же правка расходилась бы
/// в одной из копий.
/// </summary>
internal static class ClaudeAnalysisRunner
{
    /// <summary>
    /// Политика анализа плюс правила формата — общая часть системного промпта.
    /// Она одинакова во всех запросах и кэшируется, поэтому и собирается
    /// в одном месте.
    /// </summary>
    public static string SystemPrompt(string role, string languageCode) => $"""
        {role}

        {AiPolicy.AnalysisPolicy}

        {AnalysisJsonSchema.FormatRules}
        - Язык ответа: {languageCode}.
        """;

    public static async Task<BillAnalysisResult> RunAsync(
        AnthropicClient client, string model, string system, string input,
        CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = model,
            // Потолок вывода на 64 тысячи и стримингом — из-за законопроекта 42.
            // Там документ на 126 504 символа, и с прежними 16 тысячами ответ
            // обрывался на девятом праве в rights_impact: JSON приходил
            // недописанным, десериализация падала, и разбора не оставалось
            // вообще. Лимит общий на размышление и на ответ, а на усилии high
            // размышление съедает основную часть. Такой потолок без стриминга
            // упирается в таймаут HTTP, поэтому и то и другое сразу.
            MaxTokens = 64000,
            // Политика одинакова во всех запросах и занимает основную часть
            // промпта — кэшируем, иначе платим за неё на каждом разборе.
            System = new List<TextBlockParam>
            {
                new() { Text = system, CacheControl = new CacheControlEphemeral() },
            },
            OutputConfig = new OutputConfig
            {
                Effort = Effort.High,
                Format = new JsonOutputFormat { Schema = AnalysisJsonSchema.ForClaude() },
            },
            Messages = [new() { Role = Role.User, Content = input }],
        };

        var json = new StringBuilder();
        string? stopReason = null;

        await foreach (var streamEvent in client.Messages.CreateStreaming(parameters, ct))
        {
            if (streamEvent.TryPickContentBlockDelta(out var block) &&
                block.Delta.TryPickText(out var text))
            {
                json.Append(text.Text);
            }
            else if (streamEvent.TryPickDelta(out var messageDelta))
            {
                stopReason = messageDelta.Delta.StopReason;
            }
        }

        // Обрыв по лимиту проверяем до разбора JSON. Иначе причина приходит
        // как «ожидалось начало имени свойства, но данные кончились» —
        // сообщение про синтаксис там, где дело в лимите.
        if (stopReason == "max_tokens")
        {
            throw new InvalidOperationException(
                $"Разбор от {model} обрезан по лимиту вывода: пришло " +
                $"{json.Length} символов JSON. Нужен потолок выше или текст короче.");
        }

        if (json.Length == 0)
        {
            throw new InvalidOperationException(
                $"Модель {model} не вернула текст разбора (stop_reason: {stopReason ?? "—"})");
        }

        return JsonSerializer.Deserialize<BillAnalysisResult>(json.ToString())
               ?? throw new InvalidOperationException(
                   $"Разбор от {model} не разобрался по схеме BillAnalysisResult");
    }
}
