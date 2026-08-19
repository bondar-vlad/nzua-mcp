namespace NzuaMcp.Nzua;

public static class NzuaPrivacy
{
    public static bool IncludesPersonalNames =>
        string.Equals(
            Environment.GetEnvironmentVariable("NZUA_PRIVACY_MODE"),
            "full",
            StringComparison.OrdinalIgnoreCase);

    public static string StudentLabel(Student student) => StudentLabel(student, IncludesPersonalNames);

    public static string StudentLabel(Student student, bool includePersonalNames) =>
        includePersonalNames ? EscapeTable(student.Name) : $"Учень {student.Index + 1}";

    public static string PersonLabel(string name, string redactedLabel) =>
        PersonLabel(name, redactedLabel, IncludesPersonalNames);

    public static string PersonLabel(string name, string redactedLabel, bool includePersonalNames) =>
        includePersonalNames ? EscapeTable(name) : redactedLabel;

    public static string TeacherLabel(string name, int index) => TeacherLabel(name, index, IncludesPersonalNames);

    public static string TeacherLabel(string name, int index, bool includePersonalNames) =>
        includePersonalNames ? EscapeTable(name) : $"Вчитель {index + 1}";

    public static string Notice => IncludesPersonalNames
        ? "⚠️ Режим full: відповідь містить персональні дані учнів."
        : "🔒 ПІБ приховано. Для локально схваленого середовища: NZUA_PRIVACY_MODE=full.";

    private static string EscapeTable(string value) => value.Replace("|", "\\|");
}
