using ModelContextProtocol.Protocol;
using NzuaMcp.Nzua;

namespace NzuaMcp.Mcp;

/// <summary>
/// Автодоповнення аргументів промптів і шаблону nzua://journal/{journalId}.
/// Працює лише з кешованих даних: жодних мережевих запитів і вікон логіну з completion-запитів.
/// </summary>
public static class NzuaCompletions
{
    private static readonly string[] GradingSystems = ["nus_5_9", "nus_1_4", "traditional", "custom"];
    private static readonly string[] Semesters = ["1", "2"];

    public static CompleteResult Resolve(CompleteRequestParams? request, IReadOnlyList<JournalListItem> cachedJournals)
    {
        if (request is null)
            return new CompleteResult();

        var values = request.Argument.Name switch
        {
            "journalId" => cachedJournals.Select(j => j.JournalId),
            "gradingSystem" => GradingSystems,
            "semester" => Semesters,
            _ => [],
        };

        var matched = values
            .Where(v => v.StartsWith(request.Argument.Value, StringComparison.OrdinalIgnoreCase))
            .Take(100)
            .ToList();

        return new CompleteResult
        {
            Completion = new Completion { Values = matched, Total = matched.Count, HasMore = false },
        };
    }
}
