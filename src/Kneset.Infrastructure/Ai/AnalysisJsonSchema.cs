using System.Text.Json;

namespace Kneset.Infrastructure.Ai;

/// <summary>
/// JSON-схема результата анализа для structured output и общие части промпта.
///
/// Схема держится строкой, а не собирается из типов: она обязана в точности
/// соответствовать <see cref="Core.Models.BillAnalysisResult"/> и одинаково
/// уходить и в Claude, и в Gemini. Одно место правки надёжнее, чем два
/// генератора, которые однажды разойдутся.
/// </summary>
public static class AnalysisJsonSchema
{
    private const string Point = """
        {
          "type": "object",
          "properties": {
            "text": { "type": "string" },
            "kind": { "type": "string",
              "enum": ["fact", "inference", "risk", "position", "insufficient_data"] },
            "source_ref": { "type": "string" },
            "confidence": { "type": "number" }
          },
          "additionalProperties": false,
          "required": ["text", "kind", "source_ref", "confidence"]
        }
        """;

    private static readonly string Json = $$"""
        {
          "type": "object",
          "properties": {
            "translated_name": { "type": "string" },
            "summary": { "type": "string" },
            "affected_groups": { "type": "array", "items": { "type": "string" } },
            "potential_benefits": { "type": "array", "items": {{Point}} },
            "potential_risks": { "type": "array", "items": {{Point}} },
            "arguments_for": { "type": "array", "items": {{Point}} },
            "arguments_against": { "type": "array", "items": {{Point}} },
            "rights_impact": {
              "type": "object",
              "properties": {
                "affected_rights": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "right": { "type": "string" },
                      "holder": { "type": "string" },
                      "effect": { "type": "string",
                        "enum": ["restricts", "protects", "expands"] },
                      "basis": { "type": "string" },
                      "kind": { "type": "string",
                        "enum": ["fact", "inference", "risk", "position", "insufficient_data"] },
                      "source_ref": { "type": "string" }
                    },
                    "additionalProperties": false,
                    "required": ["right", "holder", "effect", "basis", "kind", "source_ref"]
                  }
                },
                "stated_purpose": { "type": "string" },
                "proportionality": {{Point}}
              },
              "additionalProperties": false,
              "required": ["affected_rights", "stated_purpose", "proportionality"]
            },
            "open_questions": { "type": "array", "items": { "type": "string" } },
            "financial_impact": {
              "type": "object",
              "properties": {
                "citizens": { "type": "string" },
                "government": { "type": "string" }
              },
              "additionalProperties": false,
              "required": ["citizens", "government"]
            }
          },
          "additionalProperties": false,
          "required": ["translated_name", "summary", "affected_groups",
            "potential_benefits", "potential_risks", "arguments_for",
            "arguments_against", "rights_impact", "open_questions",
            "financial_impact"]
        }
        """;

    /// <summary>Схема в виде, который принимает C#-SDK Anthropic.</summary>
    public static Dictionary<string, JsonElement> ForClaude() =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json)!;

    /// <summary>Та же схема как есть — для responseSchema у Gemini.</summary>
    public static JsonElement AsElement() => JsonDocument.Parse(Json).RootElement.Clone();

    /// <summary>
    /// Технические требования к ответу, общие для всех провайдеров.
    /// Содержательные правила живут в analysis-policy.md и подставляются
    /// отдельно — здесь только то, что касается формы.
    /// </summary>
    public const string FormatRules = """
        Требования к ответу:
        - Строго по запрошенной JSON-схеме, без текста вокруг.
        - kind у каждого пункта — из набора: fact, inference, risk, position,
          insufficient_data.
        - Для kind="fact" в source_ref обязательна ссылка на конкретное место:
          статья, пункт, абзац пояснительной записки, номер судебного дела.
        - Для kind="position" в тексте пункта обязана быть атрибуция
          с названным субъектом.
        - confidence — число от 0 до 1.
        - rights_impact заполняется всегда: какие права затронуты и чьи,
          заявленная цель ограничения, проверка пропорциональности.
        - Иврит в тексте ответа не воспроизводится: ссылайся на место
          и передавай смысл на языке ответа.
        """;
}
