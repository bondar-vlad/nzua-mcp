using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public class JournalFilterTests
{
    private static JournalPage SamplePage() => new(
        Journal: new Journal("journal-1", "5-А", "Математика", "teacher"),
        Students:
        [
            new Student("student-1", "Учень 1", "", 0),
            new Student("student-2", "Учень 2", "", 1),
        ],
        Lessons:
        [
            new Lesson("lesson-1", 3, "вересень", null, 0, []),
            new Lesson("lesson-2", 5, "вересень", null, 1, []),
        ],
        Marks:
        [
            new Mark("lesson-1", "student-1", "m1", "10"),
            new Mark("lesson-1", "student-2", "m2", "8"),
            new Mark("lesson-2", "student-1", "m3", "Н"),
            new Mark("lesson-2", "student-2", "m4", "12"),
        ],
        Homework:
        [
            new HomeworkEntry("lesson-1", "03.09", "1", "Тема 1", "05.09", "Впр. 1", ""),
            new HomeworkEntry("lesson-2", "05.09", "2", "Тема 2", "07.09", "Впр. 2", ""),
        ],
        Pagination: new Pagination(1, 1, [1]),
        SemesterId: "sem-1");

    [Fact]
    public void Apply_WithoutFilters_ReturnsSameInstance()
    {
        var page = SamplePage();

        Assert.Same(page, JournalFilter.Apply(page, null, null));
        Assert.Same(page, JournalFilter.Apply(page, "  ", ""));
    }

    [Fact]
    public void Apply_FiltersLessonsMarksAndKeepsMatchingTopics()
    {
        var filtered = JournalFilter.Apply(SamplePage(), "lesson-2", null);

        Assert.Equal(["lesson-2"], filtered.Lessons.Select(l => l.ScheduleId));
        Assert.Equal(["m3", "m4"], filtered.Marks.Select(m => m.MarkId));

        // Теми та ДЗ приходять із тієї самої сторінки, тому мусять звузитись разом з уроками.
        var homework = Assert.Single(filtered.Homework);
        Assert.Equal("Тема 2", homework.Topic);
        Assert.Equal("Впр. 2", homework.Homework);

        Assert.Equal(2, filtered.Students.Count);
    }

    [Fact]
    public void Apply_FiltersStudentsAndTheirMarksOnly()
    {
        var filtered = JournalFilter.Apply(SamplePage(), null, "student-2");

        Assert.Equal(["student-2"], filtered.Students.Select(s => s.StudentId));
        Assert.Equal(["m2", "m4"], filtered.Marks.Select(m => m.MarkId));
        Assert.Equal(2, filtered.Lessons.Count);
        Assert.Equal(2, filtered.Homework.Count);
    }

    [Fact]
    public void Apply_CombinesBothFiltersAndAcceptsCsvWithSpaces()
    {
        var filtered = JournalFilter.Apply(SamplePage(), "lesson-1, lesson-2", " student-1 ");

        Assert.Equal(["student-1"], filtered.Students.Select(s => s.StudentId));
        Assert.Equal(["m1", "m3"], filtered.Marks.Select(m => m.MarkId));
        Assert.Equal(2, filtered.Lessons.Count);
    }

    [Fact]
    public void Apply_UnknownIdsProduceEmptyResultWithoutThrowing()
    {
        var filtered = JournalFilter.Apply(SamplePage(), "missing", null);

        Assert.Empty(filtered.Lessons);
        Assert.Empty(filtered.Marks);
        Assert.Empty(filtered.Homework);
    }
}
