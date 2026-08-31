using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;
using NzuaMcp.Nzua.Api;

namespace NzuaMcp.Mcp.Tools;

[McpServerToolType]
public class HomeTaskTools(HomeTasksApi homeTasksApi)
{
    [McpServerTool(Name = "nzua_set_homework"), Description(
        "Задає тему уроку, номер у календарному плані, домашнє завдання та заміну вчителя/предмета. " +
        "Кілька уроків — ОДИН виклик з entriesJson [{scheduleId, topic?, lessonNumber?, homework?, homeworkTo?, secondPersonalId?, secondPredmetId?}], " +
        "а не кілька викликів поспіль. " +
        "ID для заміни беріть із nzua_get_form(kind:\"homework\"). " +
        "💡 Після змін перевірте через nzua_get_journal.")]
    public async Task<string> SetHomework(
        [Description("ID журналу")] string journalId,
        IProgress<ProgressNotificationValue> progress,
        [Description("ID уроку (для одного)")] string? scheduleId = null,
        [Description("Тема уроку")] string? lessonTopic = null,
        [Description("Номер уроку в календарному плані")] string? lessonNumber = null,
        [Description("Домашнє завдання")] string? homework = null,
        [Description("ДЗ 'на' — schedule_id цільового уроку")] string? homeworkTo = null,
        [Description("ID вчителя-замінника (second_personal_id з nzua_get_form(kind:\"homework\"))")] string? secondPersonalId = null,
        [Description("ID предмета заміщення (second_predmet_id з nzua_get_form(kind:\"homework\"))")] string? secondPredmetId = null,
        [Description("true для НУШ/ГР уроків (додає for_nus=1 в URL)")] bool forNus = false,
        [Description("JSON масив [{scheduleId, topic?, lessonNumber?, homework?, homeworkTo?, secondPersonalId?, secondPredmetId?, forNus?}]")] string? entriesJson = null)
    {
        try
        {
            if (entriesJson is not null)
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<List<SetHomeworkEntry>>(entriesJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new NzuaException("Невірний формат entriesJson");

                var results = await homeTasksApi.BatchSetHomework(journalId, entries, MarkTools.AsCallback(progress));
                var ok = results.Count(r => r.Success);
                var fail = results.Where(r => !r.Success).ToList();

                var text = $"✅ Оновлено {ok}/{results.Count} уроків\n";
                if (fail.Count > 0)
                {
                    text += "\n❌ Помилки:\n";
                    foreach (var f in fail)
                        text += $"  - schedule {f.Id}: {f.Error}\n";
                }
                return text;
            }

            if (string.IsNullOrEmpty(scheduleId))
                return "❌ Вкажіть scheduleId або entriesJson";

            await homeTasksApi.SetHomework(new SetHomeworkParams(
                scheduleId, journalId, lessonTopic, lessonNumber, homework, homeworkTo, secondPersonalId, secondPredmetId, forNus));

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(lessonTopic)) parts.Add($"тему \"{lessonTopic}\"");
            if (!string.IsNullOrEmpty(homework)) parts.Add($"ДЗ \"{homework}\"");
            if (!string.IsNullOrEmpty(lessonNumber)) parts.Add($"№{lessonNumber}");
            var what = parts.Count > 0 ? string.Join(", ", parts) : "дані";
            return $"✅ Встановлено {what} для уроку {scheduleId}";
        }
        catch (Exception ex)
        {
            return $"❌ {ex.Message}";
        }
    }
}
