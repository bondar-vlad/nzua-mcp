namespace NzuaMcp.Nzua;

/// <summary>
/// Звужує вже завантажену сторінку журналу в пам'яті, щоб не робити повторних запитів до nz.ua.
/// </summary>
public static class JournalFilter
{
    public static JournalPage Apply(JournalPage page, string? scheduleIds, string? studentIds)
    {
        var schedules = Split(scheduleIds);
        var students = Split(studentIds);
        if (schedules is null && students is null)
            return page;

        return page with
        {
            Students = students is null
                ? page.Students
                : page.Students.Where(s => students.Contains(s.StudentId)).ToList(),
            Lessons = schedules is null
                ? page.Lessons
                : page.Lessons.Where(l => schedules.Contains(l.ScheduleId)).ToList(),
            Homework = schedules is null
                ? page.Homework
                : page.Homework.Where(h => schedules.Contains(h.ScheduleId)).ToList(),
            Marks = page.Marks
                .Where(m => (schedules is null || schedules.Contains(m.ScheduleId))
                         && (students is null || students.Contains(m.StudentId)))
                .ToList(),
        };
    }

    private static HashSet<string>? Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .ToHashSet(StringComparer.Ordinal);
}
