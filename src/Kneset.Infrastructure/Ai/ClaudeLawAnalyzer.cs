using Anthropic;
using Kneset.Core.Abstractions;
using Kneset.Core.Models;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// Разбор действующего закона через Claude API.
///
/// Политика анализа и схема ответа — те же, что у законопроектов: симметрия
/// доводов, отделение факта от вывода, обязательное влияние на права.
/// Своя здесь только подводка, и разница в ней принципиальная.
///
/// Законопроект — предложение: у него есть авторы, стадия и судьба, и разбор
/// отвечает «что будет, если примут». Действующий закон уже работает:
/// вопрос не «что предлагают», а «что он требует сегодня, кого обязывает,
/// какие права даёт и чем ограничивает». Доводы за и против у него тоже
/// другие: не позиции инициаторов, а то, в чём закон защищают и в чём
/// оспаривают на практике — в суде, в комиссиях, в общественном споре.
///
/// И отдельно про источник текста: у нас копия сводной редакции из «ספר
/// החוקים הפתוח», а не официальная публикация. Модель об этом знает,
/// чтобы не приписывать тексту силы, которой у него нет.
/// </summary>
public class ClaudeLawAnalyzer(
    AnthropicClient client,
    string model = "claude-opus-5") : ILawAnalyzer
{
    public string ModelVersion => model;

    public Task<BillAnalysisResult> AnalyzeAsync(
        LawAnalysisRequest request, CancellationToken ct = default)
    {
        var system = ClaudeAnalysisRunner.SystemPrompt(
            """
            Ты аналитик действующего законодательства Израиля. Ниже — обязательная
            политика анализа платформы. Она часть твоих инструкций, а не справочный
            материал: следуй ей буквально.

            Разбирается закон, который уже действует, а не предложение. Поэтому:

            - summary — что закон требует сегодня, а не что кто-то предлагал;
            - affected_groups — кого он обязывает и кого защищает;
            - potential_benefits — что он даёт и кому, potential_risks — чем
              ограничивает и кого; это не прогноз, а действующее положение дел,
              поэтому опирайся на текст статей, а не на предположения о будущем;
            - arguments_for и arguments_against — в чём закон защищают и в чём
              оспаривают: решения суда, споры в комиссиях, публичная критика.
              Симметрия обязательна и здесь: если сильных доводов одной стороны
              не нашлось в источнике, скажи об этом через insufficient_data,
              а не выдумывай слабый довод для равновесия;
            - rights_impact — ядро разбора действующего закона: какие права он
              затрагивает, у кого, каким образом и на каком основании в тексте;
            - open_questions — что по тексту решить нельзя.

            Ссылайся на номера статей, когда они есть в тексте.
            """,
            request.LanguageCode);

        return ClaudeAnalysisRunner.RunAsync(client, model, system, BuildInput(request), ct);
    }

    private static string BuildInput(LawAnalysisRequest request) => $"""
        Действующий закон Израиля{(request.IsBasicLaw ? " (Основной закон — конституционного веса)" : "")}.

        Название (иврит): {request.NameHebrew}
        Статус действия: {Or(request.ValidityDesc, "не указан")}
        Опубликован: {Date(request.PublicationDate)}
        Поправок Кнессета: {request.AmendmentCount}{LastAmended(request)}
        Подзаконных актов, менявших закон: {request.RegulationCount}

        {(string.IsNullOrWhiteSpace(request.FullText)
            ? "Текст закона недоступен. Это ограничивает разбор: отмечай нехватку "
              + "через insufficient_data и в open_questions, а не восполняй догадками."
            : $"""
               Текст закона — сводная редакция из открытого источника
               ({Or(request.TextSource, "источник не указан")}), ревизия
               {Date(request.TextRevisionAt)}. Это не официальная публикация:
               юридическую силу имеет текст в «Рэумот». Если различие важно
               для вывода, скажи об этом.
               ---
               {request.FullText}
               ---
               """)}
        """;

    private static string LastAmended(LawAnalysisRequest request) =>
        request.LastAmendedAt is { } at ? $", последняя {at:dd.MM.yyyy}" : "";

    private static string Date(DateTime? value) =>
        value is { } d ? d.ToString("dd.MM.yyyy") : "—";

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
