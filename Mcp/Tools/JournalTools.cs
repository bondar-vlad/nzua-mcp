using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;
using NzuaMcp.Nzua.Api;

namespace NzuaMcp.Mcp.Tools;

[McpServerToolType]
public class JournalTools(JournalApi journalApi, NzuaClient client, NzuaSessionStore sessionStore)
{
    [McpServerTool(Name = "nzua_session"), Description(
        "Керує сесією nz.ua. action=status — показує стан сесії без жодного мережевого запиту (чи є активна сесія, " +
        "скільки лишилось до спливання, чи є збережена на диску); викликайте на питання 'чи я залогінений'. " +
        "action=login — відкриває видиме вікно браузера для ручного входу: Cloudflare, кабінет вчителя й вибір школи " +
        "проходить користувач. Кожен вхід починає з чистого профілю, бо застарілий cf_clearance частіше провокує " +
        "нову перевірку Cloudflare, ніж допомагає. action=logout — закриває вікно браузера і скидає сесію.")]
    public async Task<string> Session(
        [Description("Дія: status, login або logout")] string action = "status")
        => action.Trim().ToLowerInvariant() switch
        {
            "status" => SessionStatus(),
            "login" => await ManualLogin(),
            "logout" => await Logout(),
            _ => $"❌ Невідома дія '{action}'. Доступні: status, login, logout.",
        };

    private string SessionStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Стан сесії nz.ua\n");

        var active = client.Session;
        sb.AppendLine(active is not null
            ? "Сесія в пам'яті сервера: ✅ активна"
            : "Сесія в пам'яті сервера: ❌ відсутня — наступний запит відкриє вікно ручного входу.");

        if (active?.ExpiresAt is long expiresAtMs)
        {
            var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs);
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            sb.AppendLine(remaining > TimeSpan.Zero
                ? $"Спливає через: {remaining:hh\\:mm} (о {expiresAt.ToLocalTime():HH:mm dd.MM})"
                : "⚠️ Термін дії сесії вже сплив — при наступному запиті відкриється ручний вхід.");
        }

        var storedExists = File.Exists(sessionStore.FilePath);
        sb.AppendLine($"\nЗбережена сесія на диску ({sessionStore.FilePath}): {(storedExists ? "✅ є" : "❌ немає")}");
        sb.AppendLine("Якщо файл є — при перезапуску сервера повторний ручний вхід не потрібен, доки сесія не спливла.");
        sb.AppendLine("\nℹ️ Це лише локальна перевірка без мережевого запиту. Якщо тут все 'активне', але nz.ua все одно вимагає входу — викличте nzua_session(action:\"logout\"), потім nzua_session(action:\"login\").");

        return sb.ToString();
    }

    private async Task<string> ManualLogin()
    {
        try
        {
            var session = await NzuaAuth.ManualLogin();
            client.SetSession(session);
            return "✅ Ручний вхід завершено. Сесію збережено.";
        }
        catch (Exception ex)
        {
            return $"❌ Помилка ручного входу: {ex.Message}";
        }
    }

    private async Task<string> Logout()
    {
        await NzuaAuth.CloseBrowser();
        client.SetSession(null);
        sessionStore.Clear();
        return "✅ Сесію завершено, вікно браузера закрито. nzua_session(action:\"login\") відкриє нове вікно з повністю чистим профілем.";
    }

    [McpServerTool(Name = "nzua_list_journals"), Description(
        "Список усіх журналів вчителя — предмети, класи, journal_id, а також класи, предмети й семестри. " +
        "Почніть звідси, щоб дістати journal_id для решти інструментів. " +
        "semesterId перемикає семестр для всієї сесії й одразу повертає оновлений список журналів; " +
        "беріть його зі списку семестрів у відповіді цього ж інструменту.")]
    public async Task<string> ListJournals(
        [Description("semester_id зі списку семестрів. Якщо вказано — спершу перемикає семестр для всієї сесії.")] string? semesterId = null)
    {
        try
        {
            var data = string.IsNullOrWhiteSpace(semesterId)
                ? await journalApi.GetJournalList()
                : await journalApi.ChangeSemester(semesterId);

            var text = string.IsNullOrWhiteSpace(semesterId)
                ? "# Мої журнали\n"
                : $"✅ Семестр змінено\n\n# Мої журнали\n";
            text += $"Семестр: {data.CurrentSemester}\n\n";

            if (data.Journals.Count == 0)
            {
                text += "Журналів не знайдено.\n";
                return text;
            }

            text += "| Предмет | Клас | journal_id |\n";
            text += "|---------|------|------------|\n";
            foreach (var j in data.Journals)
                text += $"| {j.Subject} | {j.ClassName} | {j.JournalId} |\n";

            if (data.Classes.Count > 0)
            {
                text += $"\n## Класи ({data.Classes.Count})\n";
                foreach (var c in data.Classes)
                    text += $"- {c.Label} (class_id: {c.Id})\n";
            }

            if (data.Subjects.Count > 0)
            {
                text += $"\n## Предмети ({data.Subjects.Count})\n";
                foreach (var s in data.Subjects)
                    text += $"- {s.Label} (predmet_id: {s.Id})\n";
            }

            if (data.Semesters.Count > 0)
            {
                text += $"\n## Семестри ({data.Semesters.Count})\n";
                foreach (var sem in data.Semesters)
                {
                    var current = sem.IsCurrent ? " ← поточний" : "";
                    text += $"- {sem.Label} (semester_id: {sem.SemesterId}){current}\n";
                }
            }

            return text;
        }
        catch (Exception ex)
        {
            return $"❌ Помилка: {ex.Message}";
        }
    }

    [McpServerTool(Name = "nzua_get_journal"), Description(
        "Читає журнал: учні, уроки, оцінки, теми та ДЗ — усе за ОДИН виклик, бо все це лежить на одній сторінці nz.ua. " +
        "За замовчуванням тягне всі сторінки пагінації. " +
        "Не викликайте повторно, щоб звузити вибірку: використайте include, scheduleIds або studentIds — " +
        "фільтрація виконується в пам'яті над уже завантаженими даними, без нових запитів. " +
        "Цим же інструментом перевіряйте результат після будь-якого запису.")]
    public async Task<string> GetJournal(
        [Description("ID журналу")] string journalId,
        [Description("Що включити у відповідь (через кому): students, lessons, marks, homework. За замовчуванням — все.")] string include = "students,lessons,marks,homework",
        [Description("Номер сторінки (1-based). Якщо не вказано — всі сторінки.")] int? page = null,
        [Description("Залишити лише ці уроки — schedule_id через кому. Фільтр у пам'яті, без додаткових запитів.")] string? scheduleIds = null,
        [Description("Залишити лише цих учнів — student_id через кому. Фільтр у пам'яті, без додаткових запитів.")] string? studentIds = null,
        [Description("Фільтр по типу уроку: 111=К/р, 115=Тематична, 116=Семестрова тощо.")] int? lessonTypeId = null)
    {
        try
        {
            var sections = include.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant()).ToHashSet();
            bool wantStudents = sections.Contains("students");
            bool wantLessons = sections.Contains("lessons");
            bool wantMarks = sections.Contains("marks");
            bool wantHomework = sections.Contains("homework");

            // Якщо потрібні тільки учні — достатньо 1 сторінки
            bool onlyStudents = wantStudents && !wantLessons && !wantMarks && !wantHomework;

            JournalPage data;
            if (lessonTypeId.HasValue)
                data = await journalApi.GetFiltered(journalId, lessonTypeId.Value.ToString(), page ?? 1);
            else if (page.HasValue || onlyStudents)
                data = await journalApi.GetPage(journalId, page ?? 1);
            else
                data = await journalApi.GetAll(journalId);

            data = JournalFilter.Apply(data, scheduleIds, studentIds);

            var text = $"# Журнал {data.Journal.ClassName} [{data.Journal.Subject}]\n";
            text += $"{NzuaPrivacy.Notice}\n";
            text += $"Викладач: {NzuaPrivacy.PersonLabel(data.Journal.TeacherName, "приховано")}\n";
            if (!string.IsNullOrEmpty(data.Journal.AssistantName))
                text += $"Помічник: {NzuaPrivacy.PersonLabel(data.Journal.AssistantName, "приховано")}\n";
            if (!onlyStudents)
                text += $"Сторінок: {data.Pagination.TotalPages}\n";
            text += "\n";

            if (wantStudents)
            {
                text += $"## Учні ({data.Students.Count})\n";
                foreach (var s in data.Students)
                    text += $"{s.Index + 1}. {NzuaPrivacy.StudentLabel(s)} (id: {s.StudentId})\n";
            }

            if (wantLessons)
            {
                text += $"\n## Уроки ({data.Lessons.Count})\n";
                var hasHiddenDates = false;
                for (int i = 0; i < data.Lessons.Count; i++)
                {
                    var l = data.Lessons[i];
                    var type = !string.IsNullOrEmpty(l.LessonType) ? $" [{l.LessonType}]" : "";
                    var hw = wantHomework ? null : data.Homework.Find(h => h.ScheduleId == l.ScheduleId);
                    var topic = (!wantHomework && !string.IsNullOrEmpty(hw?.Topic)) ? $" — {hw!.Topic}" : "";
                    text += $"{i + 1}. {l.Month} {l.Day}{type}{topic} (schedule: {l.ScheduleId})\n";
                    if (l.Day == 0) hasHiddenDates = true;
                }
                if (hasHiddenDates)
                    text += "💡 Уроки з датою '0' — реальну дату/час отримайте через nzua_get_form(kind:\"lesson\").\n";
            }

            if (wantMarks && data.Marks.Count > 0)
            {
                text += $"\n## Оцінки ({data.Marks.Count} записів)\n";
                var byStudent = data.Marks.GroupBy(m => m.StudentId);
                foreach (var group in byStudent)
                {
                    var student = data.Students.Find(s => s.StudentId == group.Key);
                    var marksStr = string.Join(", ", group.Select(m =>
                    {
                        var comment = !string.IsNullOrEmpty(m.Comment) ? $" ({m.Comment})" : "";
                        return $"{m.Value}{comment}";
                    }));
                    text += $"{(student is null ? group.Key : NzuaPrivacy.StudentLabel(student))}: {marksStr}\n";
                }
            }

            if (wantHomework && data.Homework.Count > 0)
            {
                text += $"\n## Теми та ДЗ ({data.Homework.Count})\n";
                foreach (var hw in data.Homework)
                {
                    text += $"- {hw.Date} №{hw.LessonNumber}: \"{hw.Topic}\" | ДЗ: {(string.IsNullOrEmpty(hw.Homework) ? "—" : hw.Homework)}\n";
                    text += $"  (schedule: {hw.ScheduleId})\n";
                }
            }

            return text;
        }
        catch (Exception ex)
        {
            return $"❌ {ex.Message}";
        }
    }
}
