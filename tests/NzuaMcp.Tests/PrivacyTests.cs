using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public class PrivacyTests
{
    private static readonly Student Student = new("student-42", "Ім'я | Учня", "/profile/42", 2);

    [Fact]
    public void StudentLabel_RedactsNameByDefaultPolicy()
    {
        Assert.Equal("Учень 3", NzuaPrivacy.StudentLabel(Student, includePersonalNames: false));
    }

    [Fact]
    public void StudentLabel_EscapesMarkdownWhenExplicitlyIncluded()
    {
        Assert.Equal("Ім'я \\| Учня", NzuaPrivacy.StudentLabel(Student, includePersonalNames: true));
    }

    [Fact]
    public void TeacherLabel_RedactsNameByDefaultPolicy()
    {
        Assert.Equal("Вчитель 1", NzuaPrivacy.TeacherLabel("Бондарь Владислав", 0, includePersonalNames: false));
        Assert.Equal("Вчитель 3", NzuaPrivacy.TeacherLabel("Клюєв Едуард", 2, includePersonalNames: false));
    }

    [Fact]
    public void TeacherLabel_EscapesMarkdownWhenExplicitlyIncluded()
    {
        Assert.Equal("Ім'я \\| Вчителя", NzuaPrivacy.TeacherLabel("Ім'я | Вчителя", 0, includePersonalNames: true));
    }
}
