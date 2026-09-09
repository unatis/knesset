using Anthropic;
using Kneset.Core.Abstractions;
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

    public Task<BillAnalysisResult> AnalyzeAsync(
        BillAnalysisRequest request, CancellationToken ct = default)
    {
        var system = ClaudeAnalysisRunner.SystemPrompt(
            """
            Ты аналитик законодательной инициативы Кнессета. Ниже — обязательная
            политика анализа платформы. Она часть твоих инструкций, а не справочный
            материал: следуй ей буквально.
            """,
            request.LanguageCode);

        return ClaudeAnalysisRunner.RunAsync(client, model, system, BuildInput(request), ct);
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
