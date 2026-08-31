namespace NzuaMcp.Nzua.Api;

public class LessonsApi(NzuaClient client)
{
    private static string NusParam(bool forNus) => forNus ? "&for_nus=1" : "";

    public async Task<string> AddLesson(AddLessonParams p)
    {
        var body = new Dictionary<string, string>
        {
            ["OsvitaScheduleReal[lesson_type_id]"] = p.LessonTypeId.ToString(),
            ["OsvitaScheduleReal[lesson_date]"] = p.LessonDate,
            ["OsvitaScheduleReal[buzzer_id]"] = p.BuzzerId,
            ["OsvitaScheduleReal[room_id]"] = p.RoomId,
            ["OsvitaScheduleReal[repeate_type]"] = p.RepeateType ?? "not",
        };
        if (p.NusLessonTypeId.HasValue)
            body["OsvitaScheduleReal[nus_lesson_type_id]"] = p.NusLessonTypeId.Value.ToString();

        return await client.Post($"/journal/add-edit-lesson?journal={p.JournalId}{NusParam(p.ForNus == true)}", body);
    }

    public async Task<string> EditLesson(EditLessonParams p)
    {
        var body = new Dictionary<string, string>
        {
            ["OsvitaScheduleReal[lesson_type_id]"] = p.LessonTypeId.ToString(),
            ["OsvitaScheduleReal[lesson_date]"] = p.LessonDate,
            ["OsvitaScheduleReal[buzzer_id]"] = p.BuzzerId,
            ["OsvitaScheduleReal[room_id]"] = p.RoomId,
            ["OsvitaScheduleReal[repeate_type]"] = p.RepeateType ?? "not",
        };
        if (p.NusLessonTypeId.HasValue)
            body["OsvitaScheduleReal[nus_lesson_type_id]"] = p.NusLessonTypeId.Value.ToString();

        return await client.Post(
            $"/journal/add-edit-lesson?journal={p.JournalId}&schedule={p.ScheduleId}{NusParam(p.ForNus == true)}", body);
    }

    public async Task<string> DeleteLesson(DeleteLessonParams p)
    {
        try
        {
            return await client.Post("/journal/delete-lesson", new Dictionary<string, string>
            {
                ["schedule_id"] = p.ScheduleId,
            });
        }
        catch (NzuaException ex) when (ex.Message.Contains("оцінки"))
        {
            throw new LessonHasMarksException(p.ScheduleId);
        }
    }

    public async Task<List<BulkResult>> BatchAddLessons(List<AddLessonParams> lessons, Action<int, int>? onProgress = null)
    {
        var requests = lessons.Select(p =>
        {
            var body = new Dictionary<string, string>
            {
                ["OsvitaScheduleReal[lesson_type_id]"] = p.LessonTypeId.ToString(),
                ["OsvitaScheduleReal[lesson_date]"] = p.LessonDate,
                ["OsvitaScheduleReal[buzzer_id]"] = p.BuzzerId,
                ["OsvitaScheduleReal[room_id]"] = p.RoomId,
                ["OsvitaScheduleReal[repeate_type]"] = p.RepeateType ?? "not",
            };
            if (p.NusLessonTypeId.HasValue)
                body["OsvitaScheduleReal[nus_lesson_type_id]"] = p.NusLessonTypeId.Value.ToString();

            return (Path: $"/journal/add-edit-lesson?journal={p.JournalId}{NusParam(p.ForNus == true)}", Body: body);
        }).ToList();

        List<(string Body, int Status)> responses;
        try
        {
            responses = await client.BatchPost(requests, onProgress);
        }
        catch (Exception ex)
        {
            return lessons.Select((_, i) => new BulkResult(i.ToString(), false, ex.Message)).ToList();
        }

        return lessons.Zip(responses, (lesson, resp) =>
        {
            var error = DetectResponseError(resp.Body, resp.Status);
            return error is not null
                ? new BulkResult(lesson.LessonDate, false, error)
                : new BulkResult(lesson.LessonDate, true);
        }).ToList();
    }

    public async Task<List<BulkResult>> BatchEditLessons(List<EditLessonParams> lessons, Action<int, int>? onProgress = null)
    {
        var requests = lessons.Select(p =>
        {
            var body = new Dictionary<string, string>
            {
                ["OsvitaScheduleReal[lesson_type_id]"] = p.LessonTypeId.ToString(),
                ["OsvitaScheduleReal[lesson_date]"] = p.LessonDate,
                ["OsvitaScheduleReal[buzzer_id]"] = p.BuzzerId,
                ["OsvitaScheduleReal[room_id]"] = p.RoomId,
                ["OsvitaScheduleReal[repeate_type]"] = p.RepeateType ?? "not",
            };
            if (p.NusLessonTypeId.HasValue)
                body["OsvitaScheduleReal[nus_lesson_type_id]"] = p.NusLessonTypeId.Value.ToString();

            return (Path: $"/journal/add-edit-lesson?journal={p.JournalId}&schedule={p.ScheduleId}{NusParam(p.ForNus == true)}", Body: body);
        }).ToList();

        List<(string Body, int Status)> responses;
        try
        {
            responses = await client.BatchPost(requests, onProgress);
        }
        catch (Exception ex)
        {
            return lessons.Select(p => new BulkResult(p.ScheduleId, false, ex.Message)).ToList();
        }

        return lessons.Zip(responses, (lesson, resp) =>
        {
            var error = DetectResponseError(resp.Body, resp.Status);
            return error is not null
                ? new BulkResult(lesson.ScheduleId, false, error)
                : new BulkResult(lesson.ScheduleId, true);
        }).ToList();
    }

    public async Task<List<BulkResult>> BatchDeleteLessons(List<string> scheduleIds, Action<int, int>? onProgress = null)
    {
        var requests = scheduleIds.Select(sid =>
            (Path: "/journal/delete-lesson", Body: new Dictionary<string, string> { ["schedule_id"] = sid })
        ).ToList();

        List<(string Body, int Status)> responses;
        try
        {
            responses = await client.BatchPost(requests, onProgress);
        }
        catch (Exception ex)
        {
            return scheduleIds.Select(id => new BulkResult(id, false, ex.Message)).ToList();
        }

        return scheduleIds.Zip(responses, (sid, resp) =>
        {
            if (resp.Body.Contains("оцінки", StringComparison.OrdinalIgnoreCase))
                return new BulkResult(sid, false, "Урок має оцінки — спочатку видаліть їх");
            var error = DetectResponseError(resp.Body, resp.Status);
            return error is not null
                ? new BulkResult(sid, false, error)
                : new BulkResult(sid, true);
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
}
