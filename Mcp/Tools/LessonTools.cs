using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;
using NzuaMcp.Nzua.Api;

namespace NzuaMcp.Mcp.Tools;

[McpServerToolType]
public class LessonTools(LessonsApi lessonsApi, JournalApi journalApi)
{
    [McpServerTool(Name = "nzua_add_lessons", Title = "Додавання уроків", Destructive = false, Idempotent = false, OpenWorld = false), Description(
        "Додає уроки (колонки) в журнал. Кілька уроків — ОДИН виклик з entriesJson, а не кілька викликів поспіль. " +
        "lessonTypeId, buzzerId і roomId беріть із nzua_get_form(kind:\"lesson\"), не вгадуйте їх. " +
        "Для НУШ передайте forNus=true і nusLessonTypeId лише з nzua_get_form(kind:\"lesson\", forNus:true). " +
        "💡 Після додавання перевірте через nzua_get_journal.")]
    public async Task<string> AddLessons(
        [Description("ID журналу")] string journalId,
        IProgress<ProgressNotificationValue> progress,
        [Description("lesson_type_id з nzua_get_form(kind:\"lesson\")")] int? lessonTypeId = null,
        [Description("Дата уроку (YYYY-MM-DD)")] string? lessonDate = null,
        [Description("ID часу уроку (buzzer_id)")] string? buzzerId = null,
        [Description("ID кабінету (room_id)")] string? roomId = null,
        [Description("Тип повторення (not, every_week тощо)")] string repeateType = "not",
        [Description("true для НУШ-журналу")] bool forNus = false,
        [Description("nus_lesson_type_id з живої НУШ-форми; обов'язковий, коли forNus=true")] int? nusLessonTypeId = null,
        [Description("JSON масив для кількох: [{\"lessonTypeId\":110, \"lessonDate\":\"2025-01-20\", \"buzzerId\":\"...\", \"roomId\":\"...\", \"forNus\":false, \"nusLessonTypeId\":null, \"repeateType\":\"not\"}]")] string? entriesJson = null)
    {
        try
        {
            // Batch mode
            if (!string.IsNullOrEmpty(entriesJson))
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<List<AddLessonEntry>>(entriesJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new NzuaException("Невірний формат entries");

                var paramsList = entries.Select(e =>
                {
                    var entryForNus = e.ForNus ?? forNus;
                    var entryLessonTypeId = e.LessonTypeId;
                    var entryNusTypeId = e.NusLessonTypeId ?? nusLessonTypeId;
                    if (entryForNus && !entryNusTypeId.HasValue)
                        throw new NzuaException("Для ГР вкажіть nusLessonTypeId з nzua_get_form(kind:\"lesson\", forNus:true).");
                    return new AddLessonParams(journalId, entryLessonTypeId, e.LessonDate, e.BuzzerId, e.RoomId,
                        e.RepeateType ?? "not", entryForNus, entryNusTypeId);
                }).ToList();

                var results = await lessonsApi.BatchAddLessons(paramsList, MarkTools.AsCallback(progress, "Додавання уроків"));
                var ok = results.Count(r => r.Success);
                var fail = results.Where(r => !r.Success).ToList();
                var text = $"✅ Додано {ok}/{results.Count} уроків\n";
                if (fail.Count > 0)
                {
                    text += "\n❌ Помилки:\n";
                    foreach (var f in fail) text += $"  - {f.Id}: {f.Error}\n";
                }
                return text;
            }

            // Single mode
            if (!lessonTypeId.HasValue || string.IsNullOrEmpty(lessonDate) || string.IsNullOrEmpty(buzzerId) || string.IsNullOrEmpty(roomId))
                return "❌ Для одного уроку вкажіть lessonTypeId, lessonDate, buzzerId, roomId. Для кількох — entriesJson.";

            if (forNus && !nusLessonTypeId.HasValue)
                return "❌ Для ГР вкажіть nusLessonTypeId з nzua_get_form(kind:\"lesson\", forNus:true).";

            await lessonsApi.AddLesson(new AddLessonParams(
                journalId, lessonTypeId.Value, lessonDate, buzzerId, roomId, repeateType, forNus, nusLessonTypeId));
            return $"✅ Урок (type={lessonTypeId}) на {lessonDate} додано";
        }
        catch (Exception ex)
        {
            return $"❌ {ex.Message}";
        }
    }

    [McpServerTool(Name = "nzua_edit_lessons", Title = "Редагування уроків", Destructive = true, Idempotent = true, OpenWorld = false), Description(
        "Редагує уроки: тип, дата, час, кабінет. Перенесення уроку на іншу дату робиться теж тут — просто вкажіть lessonDate. " +
        "Кілька уроків — ОДИН виклик з entriesJson, а не кілька викликів поспіль. " +
        "Незазначені поля автоматично беруться з поточної форми уроку, тож перезатирання не буде. " +
        "💡 Після редагування перевірте через nzua_get_journal.")]
    public async Task<string> EditLessons(
        [Description("ID журналу")] string journalId,
        IProgress<ProgressNotificationValue> progress,
        [Description("ID уроку (для одного)")] string? scheduleId = null,
        [Description("Тип уроку або НУШ ГР-індекс")] int? lessonTypeId = null,
        [Description("Дата уроку (YYYY-MM-DD) — вкажіть, щоб перенести урок на іншу дату")] string? lessonDate = null,
        [Description("buzzer_id")] string? buzzerId = null,
        [Description("room_id")] string? roomId = null,
        [Description("true для НУШ")] bool forNus = false,
        [Description("Тип індексу НУШ")] int? nusLessonTypeId = null,
        [Description("JSON масив для кількох: [{\"scheduleId\":\"...\", \"lessonDate\":\"YYYY-MM-DD\", \"buzzerId\":\"...\", \"lessonTypeId\":181, \"roomId\":\"...\"}]")] string? entriesJson = null)
    {
        try
        {
            // Batch mode
            if (!string.IsNullOrEmpty(entriesJson))
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<List<BulkEditLessonEntry>>(entriesJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new NzuaException("Невірний формат entries");

                if (entries.Count == 0) return "❌ Порожній масив";

                var ids = entries.Select(e => e.ScheduleId).ToList();
                var forms = await journalApi.BatchGetLessonForms(journalId, ids, forNus);
                var formMap = forms.ToDictionary(f => f.ScheduleId, f => f.Form);

                var paramsList = new List<EditLessonParams>();
                var errors = new List<string>();

                foreach (var entry in entries)
                {
                    if (!formMap.TryGetValue(entry.ScheduleId, out var form))
                    {
                        errors.Add($"{entry.ScheduleId}: форма не знайдена");
                        continue;
                    }

                    var cv = form.CurrentValues ?? new();
                    var typeId = entry.LessonTypeId ?? (int.TryParse(cv.GetValueOrDefault("OsvitaScheduleReal[lesson_type_id]", "110"), out var t) ? t : 110);
                    var date = entry.LessonDate ?? cv.GetValueOrDefault("OsvitaScheduleReal[lesson_date]", "");
                    var buzzer = entry.BuzzerId ?? cv.GetValueOrDefault("OsvitaScheduleReal[buzzer_id]", "");
                    var room = entry.RoomId ?? cv.GetValueOrDefault("OsvitaScheduleReal[room_id]", "");
                    var nusTypeId = int.TryParse(cv.GetValueOrDefault("OsvitaScheduleReal[nus_lesson_type_id]", ""), out var nid) ? (int?)nid : null;

                    paramsList.Add(new EditLessonParams(entry.ScheduleId, journalId, typeId, date, buzzer, room, ForNus: forNus, NusLessonTypeId: nusTypeId));
                }

                var results = await lessonsApi.BatchEditLessons(paramsList, MarkTools.AsCallback(progress, "Редагування уроків"));
                var ok = results.Count(r => r.Success);
                var batchFail = results.Where(r => !r.Success).Select(r => $"{r.Id}: {r.Error}").Concat(errors).ToList();

                var text = $"✅ Відредаговано {ok}/{entries.Count} уроків\n";
                if (batchFail.Count > 0)
                {
                    text += "\n❌ Помилки:\n";
                    foreach (var e in batchFail) text += $"  - {e}\n";
                }
                return text;
            }

            // Single mode
            if (string.IsNullOrEmpty(scheduleId))
                return "❌ Вкажіть scheduleId (один урок) або entriesJson (масив)";

            // Fetch current values for missing params
            if (lessonTypeId == null || lessonDate == null || buzzerId == null || roomId == null || nusLessonTypeId == null)
            {
                var form = await journalApi.GetLessonForm(journalId, scheduleId, forNus);
                var cv = form.CurrentValues ?? new();
                lessonTypeId ??= int.TryParse(cv.GetValueOrDefault("OsvitaScheduleReal[lesson_type_id]", "110"), out var lt) ? lt : 110;
                lessonDate ??= cv.GetValueOrDefault("OsvitaScheduleReal[lesson_date]", "");
                buzzerId ??= cv.GetValueOrDefault("OsvitaScheduleReal[buzzer_id]", "");
                roomId ??= cv.GetValueOrDefault("OsvitaScheduleReal[room_id]", "");
                nusLessonTypeId ??= int.TryParse(cv.GetValueOrDefault("OsvitaScheduleReal[nus_lesson_type_id]", ""), out var nlt) ? nlt : (int?)null;
            }

            await lessonsApi.EditLesson(new EditLessonParams(
                scheduleId, journalId, lessonTypeId.Value, lessonDate, buzzerId, roomId, ForNus: forNus, NusLessonTypeId: nusLessonTypeId));
            return $"✅ Урок {scheduleId} відредаговано";
        }
        catch (Exception ex)
        {
            return $"❌ {ex.Message}";
        }
    }

    [McpServerTool(Name = "nzua_delete_lessons", Title = "Видалення уроків", Destructive = true, Idempotent = false, OpenWorld = false), Description(
        "Видаляє уроки з журналу. Кілька уроків — ОДИН виклик, scheduleIds через кому. " +
        "⚠️ Урок з оцінками не видаляється — спершу зніміть оцінки через nzua_set_marks.")]
    public async Task<string> DeleteLessons(
        [Description("ID уроків — один або через кому: '12345' або '12345,12346,12347'")] string scheduleIds,
        IProgress<ProgressNotificationValue> progress)
    {
        try
        {
            var ids = scheduleIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (ids.Count == 0) return "❌ Вкажіть хоча б один schedule_id";

            if (ids.Count == 1)
            {
                await lessonsApi.DeleteLesson(new DeleteLessonParams(ids[0]));
                return $"✅ Урок {ids[0]} видалено";
            }

            var results = await lessonsApi.BatchDeleteLessons(ids, MarkTools.AsCallback(progress, "Видалення уроків"));
            var ok = results.Count(r => r.Success);
            var fail = results.Where(r => !r.Success).ToList();
            var text = $"✅ Видалено {ok}/{results.Count} уроків\n";
            if (fail.Count > 0)
            {
                text += "\n❌ Помилки:\n";
                foreach (var f in fail) text += $"  - {f.Id}: {f.Error}\n";
            }
            return text;
        }
        catch (Exception ex)
        {
            return $"❌ {ex.Message}";
        }
    }
}

public record BulkEditLessonEntry(string ScheduleId, string? LessonDate = null, string? BuzzerId = null, int? LessonTypeId = null, string? RoomId = null);
public record AddLessonEntry(int LessonTypeId, string LessonDate, string BuzzerId, string RoomId, string? RepeateType = null, bool? ForNus = null, int? NusLessonTypeId = null);
