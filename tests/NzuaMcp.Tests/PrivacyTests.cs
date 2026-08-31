using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public sealed class PrivacyTests : IDisposable
{
    private static readonly Student Student = new("student-42", "Ім'я | Учня", "/profile/42", 2);

    public PrivacyTests() => NzuaPrivacy.SetSaltForTests(new byte[32]);

    public void Dispose() => NzuaPrivacy.SetSaltForTests(null);

    [Fact]
    public void StudentLabel_UsesStablePseudonymByDefaultPolicy()
    {
        var first = NzuaPrivacy.StudentLabel(Student, includePersonalNames: false);
        var second = NzuaPrivacy.StudentLabel(Student, includePersonalNames: false);

        Assert.StartsWith("Учень-", first);
        Assert.Equal(first, second);
        Assert.DoesNotContain(Student.Name, first);
    }

    [Fact]
    public void StudentLabel_IsNotPositional()
    {
        // Позиційний «Учень 3» деанонімізується алфавітним порядком nz.ua — псевдонім не має залежати від Index.
        var sameIdDifferentIndex = Student with { Index = 7 };
        Assert.Equal(
            NzuaPrivacy.StudentLabel(Student, includePersonalNames: false),
            NzuaPrivacy.StudentLabel(sameIdDifferentIndex, includePersonalNames: false));
        Assert.DoesNotContain("Учень 3", NzuaPrivacy.StudentLabel(Student, includePersonalNames: false));
    }

    [Fact]
    public void StudentLabel_DiffersPerStudent()
    {
        var other = new Student("student-43", "Інший Учень", "/profile/43", 3);
        Assert.NotEqual(
            NzuaPrivacy.StudentLabel(Student, includePersonalNames: false),
            NzuaPrivacy.StudentLabel(other, includePersonalNames: false));
    }

    [Fact]
    public void StudentLabel_DependsOnSalt()
    {
        var withZeroSalt = NzuaPrivacy.StudentLabel(Student, includePersonalNames: false);
        NzuaPrivacy.SetSaltForTests([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
        var withOtherSalt = NzuaPrivacy.StudentLabel(Student, includePersonalNames: false);

        Assert.NotEqual(withZeroSalt, withOtherSalt);
    }

    [Fact]
    public void StudentLabel_EscapesMarkdownWhenExplicitlyIncluded()
    {
        Assert.Equal("Ім'я \\| Учня", NzuaPrivacy.StudentLabel(Student, includePersonalNames: true));
    }

    [Fact]
    public void TeacherLabel_UsesStablePseudonymKeyedByName()
    {
        var first = NzuaPrivacy.TeacherLabel("Тестовий Вчитель", 0, includePersonalNames: false);
        var sameNameOtherIndex = NzuaPrivacy.TeacherLabel("Тестовий Вчитель", 5, includePersonalNames: false);
        var otherName = NzuaPrivacy.TeacherLabel("Інший Вчитель", 0, includePersonalNames: false);

        Assert.StartsWith("Вчитель-", first);
        Assert.Equal(first, sameNameOtherIndex);
        Assert.NotEqual(first, otherName);
    }

    [Fact]
    public void TeacherLabel_EscapesMarkdownWhenExplicitlyIncluded()
    {
        Assert.Equal("Ім'я \\| Вчителя", NzuaPrivacy.TeacherLabel("Ім'я | Вчителя", 0, includePersonalNames: true));
    }

    [Fact]
    public void PseudonymCode_UsesUnambiguousAlphabet()
    {
        for (int i = 0; i < 200; i++)
        {
            var code = NzuaPrivacy.PseudonymCode("student", $"id-{i}");
            Assert.Equal(5, code.Length);
            Assert.All(code, c => Assert.Contains(c, "ABCDEFGHJKMNPQRSTUVWXYZ23456789"));
        }
    }
}
