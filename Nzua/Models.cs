namespace NzuaMcp.Nzua;

// ============================================================================
// Сесія та облікові дані
// ============================================================================

public record NzuaSession(
    string Phpsessid,
    string Identity,
    string CsrfToken,
    string? CsrfCookie = null,
    string? CfClearance = null,
    long? ExpiresAt = null,
    string IdentityCookieName = "_identity"
);

// ============================================================================
// Список журналів (journal/list)
// ============================================================================

public record JournalListItem(
    string JournalId,
    string Subject,
    string ClassName
);

public record SemesterInfo(
    string SemesterId,
    string Label,
    bool IsCurrent
);

public record JournalListData(
    List<JournalListItem> Journals,
    List<SelectOption> Classes,
    List<SelectOption> Subjects,
    List<SemesterInfo> Semesters,
    string CurrentSemester
);

// ============================================================================
// Журнал — заголовок
// ============================================================================

public record Journal(
    string JournalId,
    string ClassName,
    string Subject,
    string TeacherName,
    string? AssistantName = null,
    string? ClassId = null,
    string? PredmetId = null
);

// ============================================================================
// Учень
// ============================================================================

public record Student(
    string StudentId,
    string Name,
    string ProfileUrl,
    int Index
);

// ============================================================================
// Урок (колонка журналу)
// ============================================================================

public record Lesson(
    string ScheduleId,
    int Day,
    string Month,
    string? LessonType,
    int ColumnIndex,
    List<string> CssClasses
);

// ============================================================================
// Оцінка
// ============================================================================

public record Mark(
    string ScheduleId,
    string StudentId,
    string MarkId,
    string Value,
    string? Comment = null,
    string? RatedBy = null
);

// ============================================================================
// Домашнє завдання
// ============================================================================

public record HomeworkEntry(
    string ScheduleId,
    string Date,
    string LessonNumber,
    string Topic,
    string HomeworkDate,
    string Homework,
    string Substitution
);

// ============================================================================
// Пагінація
// ============================================================================

public record Pagination(
    int CurrentPage,
    int TotalPages,
    List<int> Pages
);

// ============================================================================
// Сторінка журналу — повна відповідь
// ============================================================================

public record JournalPage(
    Journal Journal,
    List<Student> Students,
    List<Lesson> Lessons,
    List<Mark> Marks,
    List<HomeworkEntry> Homework,
    Pagination Pagination,
    string SemesterId
);

// ============================================================================
// Маппінги оцінок grade (1-12) ↔ mark_value_id
// ============================================================================

public static class MarkMappings
{
    private static readonly Dictionary<int, int> GradeToMark = new()
    {
        [1] = 6,
        [2] = 7,
        [3] = 8,
        [4] = 9,
        [5] = 10,
        [6] = 11,
        [7] = 12,
        [8] = 13,
        [9] = 14,
        [10] = 15,
        [11] = 16,
        [12] = 17,
    };

    private static readonly Dictionary<int, int> MarkToGrade;

    static MarkMappings()
    {
        MarkToGrade = new Dictionary<int, int>();
        foreach (var (grade, mark) in GradeToMark)
            MarkToGrade[mark] = grade;
    }

    public static int GradeToMarkId(int grade)
    {
        if (GradeToMark.TryGetValue(grade, out var markId))
            return markId;
        throw new NzuaException($"Невірна оцінка: {grade}. Допустимі значення: 1-12.");
    }

    public static int? MarkIdToGrade(int markId)
    {
        return MarkToGrade.TryGetValue(markId, out var grade) ? grade : null;
    }
}

// ============================================================================
// Спеціальні оцінки
// ============================================================================

public static class SpecialMarks
{
    public const int Delete = 0;
    public const int Absent = 1;       // Н
    public const int AbsentExc = 2;    // Н/А
    public const int Credited = 3;     // зар
    public const int Released = 4;     // зв
    public const int Studied = 5;      // вивч
    public const int Sick = 23;        // хв
    public const int NusBeginner = 24; // П
    public const int NusAverage = 25;  // С
    public const int NusGood = 26;     // Д
    public const int NusHigh = 27;     // В
    public const int NotEvaluated = 28; // Н/О
    public const int Remark = 29;       // заув
    public const int Attended = 30;     // п/п
    public const int Check = 31;        // √
    public const int Comment = 32;       // к (Коментар)
    public const int NotCounted = 33;   // Н/З
}

// ============================================================================
// Відображення оцінок (markId → string)
// ============================================================================

public static class MarkDisplay
{
    private static readonly Dictionary<int, string> Display = new()
    {
        [0] = "(видалено)",
        [1] = "Н",
        [2] = "Н/А",
        [3] = "зар",
        [4] = "зв",
        [5] = "вивч",
        [6] = "1",
        [7] = "2",
        [8] = "3",
        [9] = "4",
        [10] = "5",
        [11] = "6",
        [12] = "7",
        [13] = "8",
        [14] = "9",
        [15] = "10",
        [16] = "11",
        [17] = "12",
        [23] = "хв",
        [24] = "П",
        [25] = "С",
        [26] = "Д",
        [27] = "В",
        [28] = "Н/О",
        [29] = "заув",
        [30] = "п/п",
        [31] = "√",
        [32] = "к",
        [33] = "Н/З",
    };

    public static string Get(int markId) =>
        Display.TryGetValue(markId, out var display) ? display : markId.ToString();

    public static readonly IReadOnlyDictionary<string, int> SpecialMarkMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Н"] = 1,
        ["Н/А"] = 2,
        ["зар"] = 3,
        ["зв"] = 4,
        ["вивч"] = 5,
        ["хв"] = 23,
        ["П"] = 24,
        ["С"] = 25,
        ["Д"] = 26,
        ["В"] = 27,
        ["Н/О"] = 28,
        ["заув"] = 29,
        ["п/п"] = 30,
        ["√"] = 31,
        ["к"] = 32,
        ["Н/З"] = 33,
        ["delete"] = 0,
    };
}

public static class MarkValueResolver
{
    public static int Resolve(string? mark = null, int? grade = null, string? specialMark = null)
    {
        if (!string.IsNullOrWhiteSpace(mark))
        {
            if (int.TryParse(mark, out var parsedGrade))
                return MarkMappings.GradeToMarkId(parsedGrade);

            return ResolveSpecial(mark);
        }

        if (grade.HasValue)
            return MarkMappings.GradeToMarkId(grade.Value);

        if (!string.IsNullOrWhiteSpace(specialMark))
            return ResolveSpecial(specialMark);

        throw new NzuaException("Не вказано оцінку. Передайте mark, grade або specialMark.");
    }

    private static int ResolveSpecial(string value)
    {
        if (MarkDisplay.SpecialMarkMap.TryGetValue(value, out var markId))
            return markId;

        throw new NzuaException(
            $"Невідома спеціальна позначка: {value}. Для видалення використовуйте лише явне значення delete.");
    }
}

// ============================================================================
// НУШ
// ============================================================================

public static class NusLevels
{
    public const int Beginner = 24; // П
    public const int Average = 25;  // С
    public const int Good = 26;     // Д
    public const int High = 27;     // В

    public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [24] = "Початковий",
        [25] = "Середній",
        [26] = "Достатній",
        [27] = "Високий",
    };

    public static readonly IReadOnlyDictionary<int, int> Order = new Dictionary<int, int>
    {
        [24] = 1,
        [25] = 2,
        [26] = 3,
        [27] = 4,
    };

    public static readonly IReadOnlyDictionary<string, int> LevelMap = new Dictionary<string, int>
    {
        ["П"] = Beginner,
        ["С"] = Average,
        ["Д"] = Good,
        ["В"] = High,
    };

    public static bool IsNusLevel(int markId) => markId is >= 24 and <= 27;

    public static int? NusLevelToNumber(int markId) =>
        Order.TryGetValue(markId, out var num) ? num : null;
}

public enum MarkScale
{
    None,
    Numeric,
    Levels,
    Mixed,
}

public static class MarkScaleDetector
{
    public static MarkScale Detect(IEnumerable<int> markIds)
    {
        var hasNumeric = false;
        var hasLevels = false;

        foreach (var markId in markIds)
        {
            hasNumeric |= MarkMappings.MarkIdToGrade(markId).HasValue;
            hasLevels |= NusLevels.IsNusLevel(markId);
        }

        return (hasNumeric, hasLevels) switch
        {
            (true, true) => MarkScale.Mixed,
            (true, false) => MarkScale.Numeric,
            (false, true) => MarkScale.Levels,
            _ => MarkScale.None,
        };
    }

    public static string Label(MarkScale scale) => scale switch
    {
        MarkScale.Numeric => "1-12",
        MarkScale.Levels => "П/С/Д/В",
        MarkScale.Mixed => "змішана 1-12 + П/С/Д/В",
        _ => "немає академічних оцінок",
    };
}

// ============================================================================
// Параметри API
// ============================================================================

public record SetMarkParams(
    string ScheduleId,
    string StudentId,
    int MarkId,
    string? Comment = null
);

public record AddLessonParams(
    string JournalId,
    int LessonTypeId,
    string LessonDate,
    string BuzzerId,
    string RoomId,
    string? RepeateType = null,
    bool? ForNus = null,
    int? NusLessonTypeId = null
);

public record EditLessonParams(
    string ScheduleId,
    string JournalId,
    int LessonTypeId,
    string LessonDate,
    string BuzzerId,
    string RoomId,
    string? RepeateType = null,
    bool? ForNus = null,
    int? NusLessonTypeId = null
);

public record DeleteLessonParams(string ScheduleId);

public record SetHomeworkParams(
    string ScheduleId,
    string JournalId,
    string? LessonTopic = null,
    string? LessonNumberInPlan = null,
    string? Homework = null,
    string? HomeworkTo = null,
    string? SecondPersonalId = null,
    string? SecondPredmetId = null,
    bool ForNus = false
);

// ============================================================================
// Помилки
// ============================================================================

public class NzuaException : Exception
{
    public NzuaException(string message) : base(message) { }
    public NzuaException(string message, Exception inner) : base(message, inner) { }
}

public class AuthException : NzuaException
{
    public AuthException(string message) : base(message) { }
}

public class CsrfException : NzuaException
{
    public CsrfException(string message = "CSRF токен невалідний. Потрібна повторна автентифікація.") : base(message) { }
}

public class CloudflareException : NzuaException
{
    public CloudflareException(string message = "Cloudflare challenge. Потрібен ручний вхід у браузері.") : base(message) { }
}

public class LessonHasMarksException : NzuaException
{
    public LessonHasMarksException(string scheduleId)
        : base($"Урок {scheduleId} має оцінки. Видаліть їх перед видаленням уроку.") { }
}
