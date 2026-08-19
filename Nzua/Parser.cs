using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NzuaMcp.Nzua;

// ============================================================================
// Форма уроку
// ============================================================================

public record SelectOption(string Id, string Label);
public record GroupedSelectOption(string Id, string Label, string Group);

public record LessonFormData(
    string Csrf,
    List<SelectOption> Buzzers,
    List<SelectOption> Rooms,
    List<SelectOption> LessonTypes,
    Dictionary<string, string>? CurrentValues,
    bool IsNus,
    List<GroupedSelectOption>? NusIndices = null,
    List<SelectOption>? NusLessonTypes = null
);

public record HomeTaskFormData(
    string Csrf,
    List<SelectOption> HomeworkToOptions,
    List<SelectOption> Teachers,
    List<SelectOption> Subjects,
    Dictionary<string, string> CurrentValues
);

public record MarkValueDef(int MarkValueId, int Code, string Value, string Description);

// ============================================================================
// Парсер
// ============================================================================

public static partial class NzuaParser
{
    private static IBrowsingContext CreateContext() =>
        BrowsingContext.New(Configuration.Default);

    private static async Task<IDocument> ParseHtml(string html) =>
        await CreateContext().OpenAsync(req => req.Content(html));

    // ========================================================================
    // Список журналів (journal/list)
    // ========================================================================

    public static async Task<JournalListData> ParseJournalList(string html)
    {
        var doc = await ParseHtml(html);

        // Журнали з таблиці journal-choose
        var journals = new List<JournalListItem>();
        foreach (var tr in doc.QuerySelectorAll("table.journal-choose tbody tr"))
        {
            var subjectTd = tr.QuerySelector("td:first-child");
            var subject = subjectTd?.TextContent.Trim() ?? "";
            if (string.IsNullOrEmpty(subject)) continue;

            foreach (var link in tr.QuerySelectorAll("td:nth-child(2) a"))
            {
                var href = link.GetAttribute("href") ?? "";
                var journalMatch = Regex.Match(href, @"journal=(\d+)");
                if (!journalMatch.Success) continue;

                journals.Add(new JournalListItem(
                    JournalId: journalMatch.Groups[1].Value,
                    Subject: subject,
                    ClassName: link.TextContent.Trim()
                ));
            }
        }

        // Класи з фільтра
        var classes = new List<SelectOption>();
        foreach (var opt in doc.QuerySelectorAll("select[name='class_id'] option"))
        {
            var val = (opt as IHtmlOptionElement)?.Value ?? "";
            if (!string.IsNullOrEmpty(val) && val != "" && val != "all")
                classes.Add(new SelectOption(val, opt.TextContent.Trim()));
        }

        // Предмети з фільтра
        var subjects = new List<SelectOption>();
        foreach (var opt in doc.QuerySelectorAll("select[name='predmet_id'] option"))
        {
            var val = (opt as IHtmlOptionElement)?.Value ?? "";
            if (!string.IsNullOrEmpty(val) && val != "" && val != "all")
                subjects.Add(new SelectOption(val, opt.TextContent.Trim()));
        }

        // Семестри
        var semesters = new List<SemesterInfo>();
        foreach (var opt in doc.QuerySelectorAll("#personalselectform-semester_id option"))
        {
            var val = (opt as IHtmlOptionElement)?.Value ?? "";
            var isCurrent = (opt as IHtmlOptionElement)?.IsSelected ?? false;
            if (!string.IsNullOrEmpty(val))
                semesters.Add(new SemesterInfo(val, opt.TextContent.Trim(), isCurrent));
        }

        var currentSemester = semesters.FirstOrDefault(s => s.IsCurrent)?.Label ?? "";

        if (journals.Count == 0 && classes.Count == 0 && subjects.Count == 0 && semesters.Count == 0)
        {
            var fullText = doc.DocumentElement?.TextContent ?? string.Empty;
            var title = doc.Title ?? string.Empty;

            if (fullText.Contains("Trying to get property 'school' of non-object", StringComparison.OrdinalIgnoreCase) ||
                fullText.Contains("Trying to get property \"school\" of non-object", StringComparison.OrdinalIgnoreCase))
                throw new AuthException("Кабінет не визначено (school=null). Зайдіть у вчительський кабінет і оберіть школу.");

            if (title.Contains("just a moment", StringComparison.OrdinalIgnoreCase) ||
                fullText.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase) ||
                fullText.Contains("challenge-running", StringComparison.OrdinalIgnoreCase))
                throw new CloudflareException();

            if (fullText.Contains("id=\"login-form\"", StringComparison.OrdinalIgnoreCase) ||
                fullText.Contains("LoginForm[login]", StringComparison.OrdinalIgnoreCase))
                throw new AuthException("Потрібен логін у кабінет перед доступом до журналів.");

            throw new NzuaException($"Невпізнана відповідь /journal/list (title: {title}).");
        }

        return new JournalListData(journals, classes, subjects, semesters, currentSemester);
    }

    // ========================================================================
    // Сторінка журналу
    // ========================================================================

    public static async Task<JournalPage> ParseJournalPage(string html)
    {
        var doc = await ParseHtml(html);

        var journal = ParseJournalHeader(doc);
        var students = ParseStudents(doc);
        var lessons = ParseLessons(doc);
        var marks = ParseMarks(doc, students, lessons);
        var homework = ParseHomework(doc);
        var pagination = ParsePagination(doc);
        var semesterId = ParseSemesterId(doc);

        return new JournalPage(journal, students, lessons, marks, homework, pagination, semesterId);
    }

    private static Journal ParseJournalHeader(IDocument doc)
    {
        var titleEl = doc.QuerySelector(".journal-scores__title");
        var titleText = titleEl?.TextContent.Trim() ?? "";

        var classMatch = Regex.Match(titleText, @"для\s+(.+?)(?:\s*\[|$)");
        var subjectMatch = Regex.Match(titleText, @"\[(.+?)]");

        var teacherLinks = doc.QuerySelectorAll(".teacher__link");
        var teacherName = teacherLinks.Length > 0 ? teacherLinks[0].TextContent.Trim() : "";
        var assistantName = teacherLinks.Length > 1 ? teacherLinks[1].TextContent.Trim() : null;

        var journalInput = doc.QuerySelector("form#teacher-filter-form input[name='journal']");
        var journalId = (journalInput as IHtmlInputElement)?.Value ?? "";
        if (string.IsNullOrEmpty(journalId))
        {
            var addColLink = doc.QuerySelector(".journal-scores-add-col")?.GetAttribute("href") ?? "";
            journalId = ExtractJournalFromUrl(addColLink);
        }

        var addGroupLink = doc.QuerySelector(".journal-scores-add-group")?.GetAttribute("href") ?? "";
        string? classId = null;
        string? predmetId = null;
        if (Uri.TryCreate(addGroupLink, UriKind.RelativeOrAbsolute, out var uri))
        {
            var fullUri = uri.IsAbsoluteUri ? uri : new Uri(new Uri("https://nz.ua"), uri);
            var qs = System.Web.HttpUtility.ParseQueryString(fullUri.Query);
            classId = qs["class_id"];
            predmetId = qs["predmet_id"];
        }

        return new Journal(
            JournalId: journalId,
            ClassName: classMatch.Success ? classMatch.Groups[1].Value.Trim() : "",
            Subject: subjectMatch.Success ? subjectMatch.Groups[1].Value.Trim() : "",
            TeacherName: teacherName,
            AssistantName: assistantName,
            ClassId: classId,
            PredmetId: predmetId
        );
    }

    private static List<Student> ParseStudents(IDocument doc)
    {
        var students = new List<Student>();
        var rows = doc.QuerySelectorAll("tbody tr");
        int idx = 0;
        foreach (var tr in rows)
        {
            var studentTd = tr.QuerySelector("td[data-student-id]");
            var studentId = studentTd?.GetAttribute("data-student-id");
            if (string.IsNullOrEmpty(studentId)) continue;

            var nameLink = studentTd!.QuerySelector("a");
            students.Add(new Student(
                StudentId: studentId,
                Name: nameLink?.TextContent.Trim() ?? "",
                ProfileUrl: nameLink?.GetAttribute("href") ?? "",
                Index: idx++
            ));
        }
        return students;
    }

    private static List<Lesson> ParseLessons(IDocument doc)
    {
        var lessons = new List<Lesson>();
        var cols = doc.QuerySelectorAll("colgroup col[data-lesson-id]");
        int i = 0;
        foreach (var col in cols)
        {
            var scheduleId = col.GetAttribute("data-lesson-id");
            if (string.IsNullOrEmpty(scheduleId)) { i++; continue; }

            // Знаходимо header-клітинку за schedule_id через href add-edit-lesson.
            // Не можна покладатися на #cell-0-{i+1}: на сторінці 2+ id продовжуються
            // (наприклад #cell-0-21..40), а не скидаються до 1.
            var headerLink = doc.QuerySelector(
                $"a.modal-box[href*='/journal/add-edit-lesson'][href*='schedule={scheduleId}']");
            var headerCell = headerLink?.Closest("td") ?? headerLink?.Closest("th");

            var dayText = headerLink?.TextContent.Trim() ?? "0";
            var monthText = headerCell?.QuerySelector(".pt-month")?.TextContent.Trim() ?? "";
            var krText = headerCell?.QuerySelector(".pt-kr-head")?.TextContent.Trim() ?? "";
            var semText = headerCell?.QuerySelector(".pt-sem-head")?.TextContent.Trim() ?? "";
            var lessonType = !string.IsNullOrEmpty(krText) ? krText :
                !string.IsNullOrEmpty(semText) ? semText : null;

            var cssClasses = (headerCell?.GetAttribute("class") ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            lessons.Add(new Lesson(
                ScheduleId: scheduleId,
                Day: int.TryParse(dayText, out var d) ? d : 0,
                Month: monthText,
                LessonType: lessonType,
                ColumnIndex: i,
                CssClasses: cssClasses
            ));
            i++;
        }
        return lessons;
    }

    private static List<Mark> ParseMarks(IDocument doc, List<Student> students, List<Lesson> lessons)
    {
        var marks = new List<Mark>();
        var rows = doc.QuerySelectorAll("tbody tr");

        int rowIdx = 0;
        foreach (var tr in rows)
        {
            if (rowIdx >= students.Count) break;
            var student = students[rowIdx];

            var cells = tr.QuerySelectorAll("td.pt-point");
            int colIdx = 0;
            foreach (var td in cells)
            {
                var input = td.QuerySelector("input.mark-cell") as IHtmlInputElement;
                var markId = input?.GetAttribute("data-mark-id") ?? "";
                var value = input?.Value ?? "";

                if (string.IsNullOrEmpty(markId) && string.IsNullOrEmpty(value)) { colIdx++; continue; }
                if (colIdx >= lessons.Count) { colIdx++; continue; }

                var lesson = lessons[colIdx];
                var comment = td.QuerySelector(".tooltiptext")?.TextContent.Trim();
                var ratedBy = td.QuerySelector(".who_rated")?.TextContent.Trim();

                marks.Add(new Mark(
                    ScheduleId: lesson.ScheduleId,
                    StudentId: student.StudentId,
                    MarkId: markId,
                    Value: value,
                    Comment: string.IsNullOrEmpty(comment) ? null : comment,
                    RatedBy: string.IsNullOrEmpty(ratedBy) ? null : ratedBy
                ));
                colIdx++;
            }
            rowIdx++;
        }
        return marks;
    }

    private static List<HomeworkEntry> ParseHomework(IDocument doc)
    {
        var entries = new List<HomeworkEntry>();
        var rows = doc.QuerySelectorAll(".homework-row:not(.homework-row--header)");

        foreach (var row in rows)
        {
            var items = row.QuerySelectorAll(".homework__item");
            if (items.Length < 7) continue;

            var editLink = items[0].QuerySelector("a.modal-box")?.GetAttribute("href") ?? "";
            var scheduleMatch = Regex.Match(editLink, @"schedule=(\d+)");

            entries.Add(new HomeworkEntry(
                ScheduleId: scheduleMatch.Success ? scheduleMatch.Groups[1].Value : "",
                Date: items[1].TextContent.Trim(),
                LessonNumber: items[2].TextContent.Trim(),
                Topic: items[4].TextContent.Trim(),
                HomeworkDate: items[5].TextContent.Trim(),
                Homework: items[6].TextContent.Trim(),
                Substitution: items.Length > 7 ? items[7].TextContent.Trim() : ""
            ));
        }
        return entries;
    }

    private static Pagination ParsePagination(IDocument doc)
    {
        var pages = new List<int>();
        int currentPage = 1;

        foreach (var li in doc.QuerySelectorAll("ul.pagination li"))
        {
            if (li.ClassList.Contains("prev") || li.ClassList.Contains("next")) continue;
            var link = li.QuerySelector("a");
            if (int.TryParse(link?.TextContent.Trim(), out var pageNum))
            {
                pages.Add(pageNum);
                if (li.ClassList.Contains("active")) currentPage = pageNum;
            }
        }

        return new Pagination(
            CurrentPage: currentPage,
            TotalPages: pages.Count > 0 ? pages.Max() : 1,
            Pages: pages
        );
    }

    private static string ParseSemesterId(IDocument doc)
    {
        var option = doc.QuerySelector("#personalselectform-semester_id option[selected]") as IHtmlOptionElement;
        return option?.Value ?? "";
    }

    // ========================================================================
    // Форма уроку
    // ========================================================================

    public static async Task<LessonFormData> ParseLessonForm(string html)
    {
        var doc = await ParseHtml(html);
        var csrf = (doc.QuerySelector("input[name='_csrf']") as IHtmlInputElement)?.Value ?? "";

        var buzzers = ParseSelectOptions(doc, "#osvitaschedulereal-buzzer_id");
        var rooms = ParseSelectOptions(doc, "#osvitaschedulereal-room_id");

        var nusLessonTypeSelect = doc.QuerySelector("#osvitaschedulereal-nus_lesson_type_id");
        var isNus = nusLessonTypeSelect is not null;

        List<SelectOption> lessonTypes;
        List<GroupedSelectOption>? nusIndices = null;
        List<SelectOption>? nusLessonTypes = null;

        if (isNus)
        {
            nusIndices = ParseSelectOptionsWithGroups(doc, "#lesson_type_id");
            if (nusIndices.Count == 0)
                nusIndices = ParseSelectOptionsWithGroups(doc, "select[name='OsvitaScheduleReal[lesson_type_id]']");

            lessonTypes = nusIndices.Select(n => new SelectOption(n.Id, $"{n.Label} [{n.Group}]")).ToList();
            nusLessonTypes = ParseSelectOptions(doc, "#osvitaschedulereal-nus_lesson_type_id");
        }
        else
        {
            lessonTypes = ParseSelectOptions(doc, "#osvitaschedulereal-lesson_type_id");
            if (lessonTypes.Count == 0)
                lessonTypes = ParseSelectOptions(doc, "select[name='OsvitaScheduleReal[lesson_type_id]']");
        }

        var currentValues = new Dictionary<string, string>();
        foreach (var el in doc.QuerySelectorAll("input[name^='OsvitaScheduleReal'], select[name^='OsvitaScheduleReal']"))
        {
            var name = el.GetAttribute("name") ?? "";
            var val = el is IHtmlInputElement input ? input.Value :
                      el is IHtmlSelectElement select ? select.Value : "";
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(val))
                currentValues[name] = val;
        }

        return new LessonFormData(csrf, buzzers, rooms, lessonTypes, currentValues, isNus, nusIndices, nusLessonTypes);
    }

    // ========================================================================
    // Форма домашнього завдання
    // ========================================================================

    public static async Task<HomeTaskFormData> ParseHomeTaskForm(string html)
    {
        var doc = await ParseHtml(html);
        var csrf = (doc.QuerySelector("input[name='_csrf']") as IHtmlInputElement)?.Value ?? "";

        var homeworkToOptions = ParseSelectOptions(doc, "#osvitaschedulereal-hometask_to");
        var teachers = ParseSelectOptions(doc, "#osvitaschedulereal-second_personal_id");
        var subjects = ParseSelectOptions(doc, "#osvitaschedulereal-second_predmet_id");

        var currentValues = new Dictionary<string, string>();
        foreach (var el in doc.QuerySelectorAll(
            "input[name^='OsvitaScheduleReal'], select[name^='OsvitaScheduleReal'], textarea[name^='OsvitaScheduleReal']"))
        {
            var name = el.GetAttribute("name") ?? "";
            var val = el is IHtmlInputElement input ? input.Value :
                      el is IHtmlSelectElement select ? select.Value :
                      el is IHtmlTextAreaElement textarea ? textarea.Value : el.TextContent;
            if (!string.IsNullOrEmpty(name))
                currentValues[name] = val ?? "";
        }

        return new HomeTaskFormData(csrf, homeworkToOptions, teachers, subjects, currentValues);
    }

    // ========================================================================
    // journal.markValues з JavaScript
    // ========================================================================

    public static Dictionary<string, MarkValueDef> ParseMarkValues(string html)
    {
        var match = Regex.Match(html, @"journal\.markValues\s*=\s*(\{[\s\S]*?\})\s*</script>");
        if (!match.Success) return new();

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(match.Groups[1].Value);
            if (parsed is null) return new();

            var result = new Dictionary<string, MarkValueDef>();
            foreach (var (key, val) in parsed)
            {
                result[key] = new MarkValueDef(
                    MarkValueId: val.GetProperty("mark_value_id").GetInt32(),
                    Code: val.GetProperty("code").GetInt32(),
                    Value: val.GetProperty("value").GetString() ?? "",
                    Description: val.GetProperty("description").GetString() ?? ""
                );
            }
            return result;
        }
        catch
        {
            return new();
        }
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static List<SelectOption> ParseSelectOptions(IDocument doc, string selector)
    {
        var options = new List<SelectOption>();
        foreach (var opt in doc.QuerySelectorAll($"{selector} option"))
        {
            var htmlOpt = opt as IHtmlOptionElement;
            var val = htmlOpt?.Value ?? "";
            var label = opt.TextContent.Trim();
            if (!string.IsNullOrEmpty(val))
                options.Add(new SelectOption(val, label));
        }
        return options;
    }

    private static List<GroupedSelectOption> ParseSelectOptionsWithGroups(IDocument doc, string selector)
    {
        var options = new List<GroupedSelectOption>();
        foreach (var optgroup in doc.QuerySelectorAll($"{selector} optgroup"))
        {
            var groupLabel = optgroup.GetAttribute("label")?.Trim() ?? "";
            foreach (var opt in optgroup.QuerySelectorAll("option"))
            {
                var htmlOpt = opt as IHtmlOptionElement;
                var val = htmlOpt?.Value ?? "";
                var label = opt.TextContent.Trim();
                if (!string.IsNullOrEmpty(val))
                    options.Add(new GroupedSelectOption(val, label, groupLabel));
            }
        }
        return options;
    }

    private static string ExtractJournalFromUrl(string url)
    {
        var match = Regex.Match(url, @"journal=(\d+)");
        return match.Success ? match.Groups[1].Value : "";
    }
}
