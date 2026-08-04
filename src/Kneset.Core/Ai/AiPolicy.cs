using System.Reflection;

namespace Kneset.Core.Ai;

/// <summary>
/// Загрузчик политики AI-анализа (Ai/analysis-policy.md, embedded resource).
/// Каждый AI-провайдер обязан включать этот текст в свой системный промпт —
/// правила (маркировка утверждений, жёсткая симметрия, запреты) едины
/// независимо от того, какая модель подключена.
/// </summary>
public static class AiPolicy
{
    private static readonly Lazy<string> _policy = new(Load);

    /// <summary>Полный текст политики анализа для системного промпта.</summary>
    public static string AnalysisPolicy => _policy.Value;

    private static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Kneset.Core.Ai.analysis-policy.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Не найден embedded resource {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
