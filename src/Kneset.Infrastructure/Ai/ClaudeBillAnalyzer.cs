using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Kneset.Core.Abstractions;
using Kneset.Core.Ai;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Анализ законопроекта через Claude API.
///
/// Модель по умолчанию — Opus 5, и это выбор по замеру, а не по цене.
/// На одной выборке из 11 законопроектов Opus давал 28.7 пунктов разбора
/// против 16 у Sonnet и затрагивал права человека во всех 11 случаях против
/// двух — правда, замер прав делался до того, как rights_impact стал
/// обязательным полем, и с ним Sonnet разрыв закрыл. Кто дешевле подходит
/// для потока, решается конфигом Ai:AnalysisModel, а не правкой кода.
///
/// Усилие high: разделение факта от вывода и симметрия аргументов —
/// не механическая работа, и на низком усилии модели путают омонимы
/// и заполняют пробелы догадками.
/// </summary>
public class ClaudeBillAnalyzer(
    AnthropicClient client,
    string model = "claude-opus-5") : IBillAnalyzer
{
    public string ModelVersion => model;

    public async Task<BillAnalysisResult> AnalyzeAsync(
        BillAnalysisRequest request, CancellationToken ct = default)
    {
        var system = $"""
            Ты аналитик законодательной инициативы Кнессета. Ниже — обязательная
            политика анализа платформы. Она часть твоих инструкций, а не справочный
            материал: следуй ей буквально.

            {AiPolicy.AnalysisPolicy}

            {AnalysisJsonSchema.FormatRules}
            - Язык ответа: {request.LanguageCode}.
            """;

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
            // промпта — кэшируем, иначе платим за неё на каждом законопроекте.
            System = new List<TextBlockParam>
            {
                new() { Text = system, CacheControl = new CacheControlEphemeral() },
            },
            OutputConfig = new OutputConfig
            {
                Effort = Effort.High,
                Format = new JsonOutputFormat { Schema = AnalysisJsonSchema.ForClaude() },
            },
            Messages = [new() { Role = Role.User, Content = BuildInput(request) }],
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

    /// <summary>
    /// Вход для модели. Полный текст документа подаётся целиком, если он есть:
    /// без него политика требует ссылок на источник, а ссылаться не на что,
    /// и разбор выходит пересказом названия.
    /// </summary>
    private static string BuildInput(BillAnalysisRequest request) => $"""
        Законопроект Кнессета.

        Название (иврит): {request.NameHebrew}
        Тип: {request.SubTypeDesc ?? "—"}
        Стадия: {request.StatusDesc ?? "—"}
        Созыв: {request.KnessetNum}
        Официальное описание: {Or(request.SummaryLaw, "отсутствует")}

        {(string.IsNullOrWhiteSpace(request.FullText)
            ? "Текст документа недоступен. Это ограничивает разбор: отмечай "
              + "нехватку через insufficient_data и в open_questions, "
              + "а не восполняй догадками."
            : $"Текст документа:\n---\n{request.FullText}\n---")}
        """;

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
