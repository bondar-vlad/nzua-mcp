namespace NzuaMcp.Nzua.Api;

public class HomeTasksApi(NzuaClient client)
{
    public async Task<string> SetHomework(SetHomeworkParams p)
    {
        var body = new Dictionary<string, string>();

        if (p.LessonTopic is not null)
            body["OsvitaScheduleReal[lesson_topic]"] = p.LessonTopic;
        if (p.LessonNumberInPlan is not null)
            body["OsvitaScheduleReal[lesson_number_in_plan]"] = p.LessonNumberInPlan;
        if (p.Homework is not null)
            body["OsvitaScheduleReal[hometask]"] = p.Homework;
        if (p.HomeworkTo is not null)
            body["OsvitaScheduleReal[hometask_to]"] = p.HomeworkTo;
        if (p.SecondPersonalId is not null)
            body["OsvitaScheduleReal[second_personal_id]"] = p.SecondPersonalId;
        if (p.SecondPredmetId is not null)
            body["OsvitaScheduleReal[second_predmet_id]"] = p.SecondPredmetId;

        var nusParam = p.ForNus ? "&for_nus=1" : "";
        return await client.Post(
            $"/journal/add-edit-home-task?schedule={p.ScheduleId}&journal={p.JournalId}{nusParam}", body);
    }

    public async Task<List<BulkResult>> BatchSetHomework(
        string journalId,
        List<SetHomeworkEntry> entries,
        Action<int, int>? onProgress = null)
    {
        var requests = entries.Select(entry =>
        {
            var body = new Dictionary<string, string>();
            if (entry.Topic is not null)
                body["OsvitaScheduleReal[lesson_topic]"] = entry.Topic;
            if (entry.LessonNumber is not null)
                body["OsvitaScheduleReal[lesson_number_in_plan]"] = entry.LessonNumber;
            if (entry.Homework is not null)
                body["OsvitaScheduleReal[hometask]"] = entry.Homework;
            if (entry.HomeworkTo is not null)
                body["OsvitaScheduleReal[hometask_to]"] = entry.HomeworkTo;
            if (entry.SecondPersonalId is not null)
                body["OsvitaScheduleReal[second_personal_id]"] = entry.SecondPersonalId;
            if (entry.SecondPredmetId is not null)
                body["OsvitaScheduleReal[second_predmet_id]"] = entry.SecondPredmetId;
            var nusParam = entry.ForNus ? "&for_nus=1" : "";
            return (Path: $"/journal/add-edit-home-task?schedule={entry.ScheduleId}&journal={journalId}{nusParam}", Body: body);
        }).ToList();

        List<(string Body, int Status)> responses;
        try
        {
            responses = await client.BatchPost(requests, onProgress);
        }
        catch (Exception ex)
        {
            return entries.Select(e => new BulkResult(e.ScheduleId, false, ex.Message)).ToList();
        }

        return entries.Zip(responses, (entry, resp) =>
        {
            var error = DetectResponseError(resp.Body, resp.Status);
            return error is not null
                ? new BulkResult(entry.ScheduleId, false, error)
                : new BulkResult(entry.ScheduleId, true);
        }).ToList();
    }

    private static string? DetectResponseError(string response, int status)
    {
        if (status >= 400)
        {
            var snippet = response.Length > 300 ? response[..300] : response;
            return $"HTTP {status}: {snippet}";
        }
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
}

public record SetHomeworkEntry(
    string ScheduleId,
    string? Topic = null,
    string? LessonNumber = null,
    string? Homework = null,
    string? HomeworkTo = null,
    string? SecondPersonalId = null,
    string? SecondPredmetId = null,
    bool ForNus = false);
