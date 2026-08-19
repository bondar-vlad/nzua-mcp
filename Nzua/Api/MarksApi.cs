namespace NzuaMcp.Nzua.Api;

public class MarksApi(NzuaClient client)
{
    public async Task<string> SetMark(SetMarkParams p)
    {
        var body = new Dictionary<string, string>
        {
            ["schedule"] = p.ScheduleId,
            ["student"] = p.StudentId,
            ["mark"] = p.MarkId.ToString(),
        };
        if (!string.IsNullOrEmpty(p.Comment))
            body["comment"] = p.Comment;

        return await client.Post("/journal/set-mark", body);
    }

    public async Task<string> SetGrade(string scheduleId, string studentId, int grade, string? comment = null)
    {
        return await SetMark(new SetMarkParams(scheduleId, studentId, MarkMappings.GradeToMarkId(grade), comment));
    }

    public async Task<string> SetAbsent(string scheduleId, string studentId, string? comment = null)
    {
        return await SetMark(new SetMarkParams(scheduleId, studentId, SpecialMarks.Absent, comment));
    }

    public async Task<string> SetSick(string scheduleId, string studentId, string? comment = null)
    {
        return await SetMark(new SetMarkParams(scheduleId, studentId, SpecialMarks.Sick, comment));
    }

    public async Task<string> DeleteMark(string scheduleId, string studentId)
    {
        return await SetMark(new SetMarkParams(scheduleId, studentId, SpecialMarks.Delete));
    }

    public async Task<List<BulkResult>> BulkSetMarks(
        string scheduleId,
        List<BulkMarkEntry> entries)
    {
        var flat = entries.Select(e => new FlatMarkEntry(scheduleId, e.StudentId, e.MarkId, e.Comment)).ToList();
        return await BulkSetMarksFlat(flat);
    }

    public async Task<List<BulkResult>> BulkSetMarksFlat(List<FlatMarkEntry> entries)
    {
        var requests = entries.Select(entry =>
        {
            var body = new Dictionary<string, string>
            {
                ["schedule"] = entry.ScheduleId,
                ["student"] = entry.StudentId,
                ["mark"] = entry.MarkId.ToString(),
            };
            if (!string.IsNullOrEmpty(entry.Comment))
                body["comment"] = entry.Comment;
            return (Path: "/journal/set-mark", Body: body);
        }).ToList();

        List<(string Body, int Status)> responses;
        try
        {
            responses = await client.BatchPost(requests);
        }
        catch (Exception ex)
        {
            return entries.Select(e => new BulkResult($"{e.ScheduleId}/{e.StudentId}", false, ex.Message)).ToList();
        }

        return entries.Zip(responses, (entry, resp) =>
        {
            var error = DetectResponseError(resp.Body, resp.Status);
            return error is not null
                ? new BulkResult($"{entry.ScheduleId}/{entry.StudentId}", false, error)
                : new BulkResult($"{entry.ScheduleId}/{entry.StudentId}", true);
        }).ToList();
    }

    private static string? DetectResponseError(string response, int status)
    {
        if (status >= 400)
            return $"HTTP {status}";
        if (string.IsNullOrWhiteSpace(response))
            return "Порожня відповідь від сервера";
        if (response.Contains("cf-challenge-running", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
            return "Cloudflare challenge";
        if (response.Contains("LoginForm[login]", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("id=\"login-form\"", StringComparison.OrdinalIgnoreCase))
            return "Сесія протухла";
        if (response.Contains("Trying to get property", StringComparison.OrdinalIgnoreCase))
            return "Не обрано школу в кабінеті";
        return null;
    }

    public async Task<List<BulkResult>> BulkSetAbsent(
        string scheduleId,
        List<string> studentIds,
        string? comment = null)
    {
        var entries = studentIds.Select(id => new BulkMarkEntry(id, SpecialMarks.Absent, comment)).ToList();
        return await BulkSetMarks(scheduleId, entries);
    }
}

public record BulkMarkEntry(string StudentId, int MarkId, string? Comment = null);

// Запис для cross-lesson batch (кожен зі своїм scheduleId)
public record FlatMarkEntry(string ScheduleId, string StudentId, int MarkId, string? Comment = null);
public record BulkResult(string Id, bool Success, string? Error = null);
