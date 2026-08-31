namespace NzuaMcp.Nzua.Api;

public class JournalApi(NzuaClient client)
{
    // Кеш повного журналу (усі сторінки): nzua_get_journal у межах однієї розмови зазвичай викликають
    // кілька разів для того самого journalId. Без кешу кожен виклик заново проганяє ті самі сторінки
    // через браузер. Кеш інвалідується автоматично, щойно WriteGeneration клієнта змінюється (будь-який
    // успішний запис), і має короткий TTL як запобіжник.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (DateTimeOffset FetchedAt, int Generation, JournalPage Page)> _fullJournalCache = new();
    private readonly object _cacheLock = new();

    // Останній успішний список журналів — для MCP completions без мережевих запитів і попапів логіну.
    private volatile IReadOnlyList<JournalListItem> _cachedJournals = [];
    public IReadOnlyList<JournalListItem> CachedJournals => _cachedJournals;

    public async Task<JournalListData> GetJournalList()
    {
        var html = await client.Get("/journal/list");
        var data = await NzuaParser.ParseJournalList(html);
        _cachedJournals = data.Journals;
        return data;
    }

    public async Task<JournalListData> ChangeSemester(string semesterId)
    {
        // Спершу отримуємо CSRF зі сторінки журналів
        var listHtml = await client.Get("/journal/list");
        var listData = await NzuaParser.ParseJournalList(listHtml);

        // POST на зміну семестру
        await client.Post("/site/semester-change", new Dictionary<string, string>
        {
            ["semester_id"] = semesterId,
        });

        // Повертаємо оновлений список журналів
        var updatedHtml = await client.Get("/journal/list");
        var updated = await NzuaParser.ParseJournalList(updatedHtml);
        _cachedJournals = updated.Journals;
        return updated;
    }

    public async Task<JournalPage> GetPage(string journalId, int page = 1)
    {
        var html = await client.Get("/journal/index", new Dictionary<string, string>
        {
            ["journal"] = journalId,
            ["page"] = page.ToString(),
        });
        return await NzuaParser.ParseJournalPage(html);
    }

    public async Task<JournalPage> GetAll(string journalId, Action<int, int>? onPageLoaded = null)
    {
        lock (_cacheLock)
        {
            if (_fullJournalCache.TryGetValue(journalId, out var cached) &&
                cached.Generation == client.WriteGeneration &&
                DateTimeOffset.UtcNow - cached.FetchedAt < CacheTtl)
                return cached.Page;
        }

        var firstPage = await GetPage(journalId, 1);
        var totalPages = firstPage.Pagination.TotalPages;
        onPageLoaded?.Invoke(1, Math.Max(totalPages, 1));

        JournalPage result;
        if (totalPages <= 1)
        {
            result = firstPage;
        }
        else
        {
            var allLessons = new List<Lesson>(firstPage.Lessons);
            var allMarks = new List<Mark>(firstPage.Marks);
            var allHomework = new List<HomeworkEntry>(firstPage.Homework);

            var paths = Enumerable.Range(2, totalPages - 1)
                .Select(p => $"/journal/index?journal={journalId}&page={p}")
                .ToList();

            Action<int, int>? pageProgress = onPageLoaded is null
                ? null
                : (done, _) => onPageLoaded(done + 1, totalPages);
            var htmls = await client.BatchGet(paths, pageProgress);
            foreach (var html in htmls)
            {
                var page = await NzuaParser.ParseJournalPage(html);
                allLessons.AddRange(page.Lessons);
                allMarks.AddRange(page.Marks);
                allHomework.AddRange(page.Homework);
            }

            result = firstPage with
            {
                Lessons = allLessons,
                Marks = allMarks,
                Homework = allHomework,
                Pagination = new Pagination(1, totalPages, Enumerable.Range(1, totalPages).ToList()),
            };
        }

        lock (_cacheLock)
            _fullJournalCache[journalId] = (DateTimeOffset.UtcNow, client.WriteGeneration, result);

        return result;
    }

    public async Task<JournalPage> GetFiltered(string journalId, string lessonTypeId, int page = 1)
    {
        var html = await client.Get("/journal/index", new Dictionary<string, string>
        {
            ["journal"] = journalId,
            ["page"] = page.ToString(),
            ["lesson_type_id"] = lessonTypeId,
        });
        return await NzuaParser.ParseJournalPage(html);
    }

    public async Task<LessonFormData> GetLessonForm(string journalId, string? scheduleId = null, bool forNus = false)
    {
        var url = $"/journal/add-edit-lesson?journal={journalId}";
        if (!string.IsNullOrEmpty(scheduleId)) url += $"&schedule={scheduleId}";
        if (forNus) url += "&for_nus=1";
        var html = await client.Get(url);
        return await NzuaParser.ParseLessonForm(html);
    }

    public async Task<List<(string ScheduleId, LessonFormData Form)>> BatchGetLessonForms(string journalId, List<string> scheduleIds, bool forNus = false)
    {
        var nusParam = forNus ? "&for_nus=1" : "";
        var paths = scheduleIds
            .Select(sid => $"/journal/add-edit-lesson?journal={journalId}&schedule={sid}{nusParam}")
            .ToList();

        var htmls = await client.BatchGet(paths);
        var results = new List<(string, LessonFormData)>();
        for (int i = 0; i < scheduleIds.Count; i++)
        {
            var form = await NzuaParser.ParseLessonForm(htmls[i]);
            results.Add((scheduleIds[i], form));
        }
        return results;
    }

    public async Task<HomeTaskFormData> GetHomeTaskForm(string scheduleId, string journalId)
    {
        var html = await client.Get("/journal/add-edit-home-task", new Dictionary<string, string>
        {
            ["schedule"] = scheduleId,
            ["journal"] = journalId,
        });
        return await NzuaParser.ParseHomeTaskForm(html);
    }

    public async Task<List<(string ScheduleId, HomeTaskFormData Form)>> BatchGetHomeTaskForms(string journalId, List<string> scheduleIds)
    {
        var paths = scheduleIds
            .Select(sid => $"/journal/add-edit-home-task?schedule={sid}&journal={journalId}")
            .ToList();

        var htmls = await client.BatchGet(paths);
        var results = new List<(string, HomeTaskFormData)>();
        for (int i = 0; i < scheduleIds.Count; i++)
        {
            var form = await NzuaParser.ParseHomeTaskForm(htmls[i]);
            results.Add((scheduleIds[i], form));
        }
        return results;
    }

}
