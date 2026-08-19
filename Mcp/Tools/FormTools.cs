using System.ComponentModel;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;
using NzuaMcp.Nzua.Api;

namespace NzuaMcp.Mcp.Tools;

[McpServerToolType]
public class FormTools(JournalApi journalApi)
{
    [McpServerTool(Name = "nzua_get_form"), Description(
        "Повертає ID і поточні значення, потрібні ПЕРЕД записом — беріть їх звідси, а не вгадуйте. " +
        "kind=\"lesson\" — час уроку (buzzer_id), кабінети (room_id), типи уроків (lesson_type_id) і поточні значення уроку; " +
        "з forNus=true додатково показує ГР-індекси й nus_lesson_type_id для НУШ. " +
        "kind=\"homework\" — поточна тема, номер у плані, ДЗ, а також вчителі й предмети для заміни. " +
        "Кілька уроків — один виклик: scheduleIds через кому.")]
    public async Task<string> GetForm(
        [Description("Тип форми: lesson або homework")] string kind,
        [Description("ID журналу")] string journalId,
        [Description("schedule_id — один або кілька через кому. Для kind=lesson можна не вказувати, щоб побачити довідники для нового уроку.")] string? scheduleIds = null,
        [Description("true для НУШ-журналу (показує ГР-індекси). Стосується лише kind=lesson.")] bool forNus = false)
    {
        try
        {
            var ids = string.IsNullOrWhiteSpace(scheduleIds)
                ? []
                : scheduleIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            return kind.Trim().ToLowerInvariant() switch
            {
                "lesson" => await LessonForm(journalId, ids, forNus),
                "homework" => await HomeworkForm(journalId, ids),
                _ => $"❌ Невідомий kind '{kind}'. Доступні: lesson, homework.",
            };
        }
        catch (Exception ex)
        {
            return $"❌ {ex.Message}";
        }
    }

    private async Task<string> LessonForm(string journalId, List<string> ids, bool forNus)
    {
        if (ids.Count > 1)
        {
            var results = await journalApi.BatchGetLessonForms(journalId, ids, forNus);

            var text = $"## Деталі уроків ({ids.Count})\n\n";
            text += "| schedule_id | Дата | Урок | Тип | buzzer_id |\n";
            text += "|-------------|------|------|-----|----------|\n";

            foreach (var (sid, form) in results)
            {
                var cv = form.CurrentValues ?? new();
                var date = cv.GetValueOrDefault("OsvitaScheduleReal[lesson_date]", "?");
                var buzId = cv.GetValueOrDefault("OsvitaScheduleReal[buzzer_id]", "?");
                var typeId = cv.GetValueOrDefault("OsvitaScheduleReal[lesson_type_id]", "?");

                var buzzerLabel = form.Buzzers.FirstOrDefault(b => b.Id == buzId)?.Label ?? buzId;
                var typeLabel = form.LessonTypes.FirstOrDefault(t => t.Id == typeId)?.Label ?? typeId;

                text += $"| {sid} | {date} | {buzzerLabel} | {typeLabel} | {buzId} |\n";
            }
            return text;
        }

        var singleForm = await journalApi.GetLessonForm(journalId, ids.FirstOrDefault(), forNus);
        var singleText = $"## Форма уроку{(singleForm.IsNus ? " (НУШ)" : "")}\n\n";

        if (singleForm.IsNus && singleForm.NusIndices is { Count: > 0 })
        {
            singleText += "### ГР-індекси (lesson_type_id)\n";
            var currentGroup = "";
            foreach (var idx in singleForm.NusIndices)
            {
                if (idx.Group != currentGroup)
                {
                    currentGroup = idx.Group;
                    singleText += $"\n**{currentGroup}**\n";
                }
                singleText += $"- {idx.Id}: {idx.Label}\n";
            }

            if (singleForm.NusLessonTypes is { Count: > 0 })
            {
                singleText += "\n### Тип індексу НУШ (nus_lesson_type_id)\n";
                foreach (var lt in singleForm.NusLessonTypes)
                    singleText += $"- {lt.Id}: {lt.Label}\n";
            }
        }
        else
        {
            singleText += "### Типи уроків\n";
            foreach (var lt in singleForm.LessonTypes)
                singleText += $"- {lt.Id}: {lt.Label}\n";
        }

        singleText += "\n### Час уроку (buzzer_id)\n";
        foreach (var b in singleForm.Buzzers)
            singleText += $"- {b.Id}: {b.Label}\n";

        singleText += "\n### Кабінети (room_id)\n";
        foreach (var r in singleForm.Rooms)
            singleText += $"- {r.Id}: {r.Label}\n";

        if (singleForm.CurrentValues is { Count: > 0 })
        {
            singleText += "\n### Поточні значення\n";
            foreach (var (k, v) in singleForm.CurrentValues)
                singleText += $"- {k}: {v}\n";
        }

        return singleText;
    }

    private async Task<string> HomeworkForm(string journalId, List<string> ids)
    {
        if (ids.Count == 0)
            return "❌ Для kind=\"homework\" вкажіть scheduleIds.";

        if (ids.Count == 1)
            return FormatSingleHomeworkForm(await journalApi.GetHomeTaskForm(ids[0], journalId));

        var forms = await journalApi.BatchGetHomeTaskForms(journalId, ids);

        var text = $"## Дані ДЗ-форм ({forms.Count})\n";
        text += $"{NzuaPrivacy.Notice}\n\n";
        text += "| schedule_id | Тема | № в плані | ДЗ | ДЗ \"на\" |\n";
        text += "|-------------|------|-----------|----|---------|\n";
        foreach (var (sid, form) in forms)
        {
            form.CurrentValues.TryGetValue("OsvitaScheduleReal[lesson_topic]", out var topic);
            form.CurrentValues.TryGetValue("OsvitaScheduleReal[lesson_number_in_plan]", out var num);
            form.CurrentValues.TryGetValue("OsvitaScheduleReal[hometask]", out var hw);
            form.CurrentValues.TryGetValue("OsvitaScheduleReal[hometask_to]", out var hwTo);
            text += $"| {sid} | {topic ?? "—"} | {num ?? "—"} | {hw ?? "—"} | {hwTo ?? "—"} |\n";
        }

        if (forms.Count > 0)
        {
            var first = forms[0].Form;
            text += $"\n### ДЗ \"на\" (hometask_to schedule_id)\n";
            foreach (var o in first.HomeworkToOptions)
                text += $"- {o.Id}: {o.Label}\n";

            text += FormatTeachers(first.Teachers);

            if (first.Subjects.Count > 0)
            {
                text += "\n### Предмети для заміни\n";
                foreach (var s in first.Subjects)
                    text += $"- {s.Id}: {s.Label}\n";
            }
        }

        return text;
    }

    private static string FormatSingleHomeworkForm(HomeTaskFormData form)
    {
        var text = "## Форма домашнього завдання\n";
        text += $"{NzuaPrivacy.Notice}\n\n";

        if (form.CurrentValues.Count > 0)
        {
            text += "### Поточні значення\n";
            foreach (var (k, v) in form.CurrentValues)
                if (!string.IsNullOrEmpty(v)) text += $"- {k}: {v}\n";
        }

        text += "\n### ДЗ \"на\" (hometask_to schedule_id)\n";
        foreach (var o in form.HomeworkToOptions)
            text += $"- {o.Id}: {o.Label}\n";

        text += FormatTeachers(form.Teachers);

        if (form.Subjects.Count > 0)
        {
            text += "\n### Предмети для заміни\n";
            foreach (var s in form.Subjects)
                text += $"- {s.Id}: {s.Label}\n";
        }

        return text;
    }

    // ID вчителів потрібні для secondPersonalId, тому приховується лише ПІБ.
    private static string FormatTeachers(List<SelectOption> teachers)
    {
        if (teachers.Count == 0)
            return "";

        var text = "\n### Вчителі для заміни\n";
        for (int i = 0; i < teachers.Count; i++)
            text += $"- {teachers[i].Id}: {NzuaPrivacy.TeacherLabel(teachers[i].Label, i)}\n";
        return text;
    }
}
