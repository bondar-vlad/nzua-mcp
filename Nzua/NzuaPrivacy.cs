using System.Security.Cryptography;
using System.Text;

namespace NzuaMcp.Nzua;

/// <summary>
/// Деперсоналізація за замовчуванням. Псевдоніми детерміновані (HMAC від локального salt),
/// тож стабільні між викликами й сесіями, але НЕ позиційні: за «Учень 1» тривіально відновити
/// ПІБ через алфавітний порядок списку nz.ua, за «Учень-K3F7A» — ні (без локального salt-файлу).
/// </summary>
public static class NzuaPrivacy
{
    // Без 0/O/1/I/L — щоб код не читався двозначно.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 5;

    private static readonly Lazy<byte[]> LazySalt = new(LoadOrCreateSalt, isThreadSafe: true);
    private static byte[]? _saltOverride;

    /// <summary>Дозволяє тестам підставити фіксований salt без запису в %APPDATA%.</summary>
    internal static void SetSaltForTests(byte[]? salt) => _saltOverride = salt;

    public static string SaltFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "nzua-mcp",
        "privacy.salt");

    public static bool IncludesPersonalNames
    {
        get
        {
            var flag = Environment.GetEnvironmentVariable("NZUA_SHOW_REAL_NAMES");
            if (string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase) || flag == "1")
                return true;
            // Легасі-флаг попередніх версій.
            return string.Equals(
                Environment.GetEnvironmentVariable("NZUA_PRIVACY_MODE"),
                "full",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string StudentLabel(Student student) => StudentLabel(student, IncludesPersonalNames);

    public static string StudentLabel(Student student, bool includePersonalNames) =>
        includePersonalNames ? EscapeTable(student.Name) : $"Учень-{PseudonymCode("student", student.StudentId)}";

    public static string PersonLabel(string name, string redactedLabel) =>
        PersonLabel(name, redactedLabel, IncludesPersonalNames);

    public static string PersonLabel(string name, string redactedLabel, bool includePersonalNames) =>
        includePersonalNames ? EscapeTable(name) : redactedLabel;

    public static string TeacherLabel(string name, int index) => TeacherLabel(name, index, IncludesPersonalNames);

    public static string TeacherLabel(string name, int index, bool includePersonalNames) =>
        includePersonalNames ? EscapeTable(name) : $"Вчитель-{PseudonymCode("teacher", name)}";

    public static string Notice => IncludesPersonalNames
        ? "⚠️ Показуються реальні ПІБ (NZUA_SHOW_REAL_NAMES=true)."
        : "🔒 ПІБ замінено стабільними псевдонімами. Реальні імена: NZUA_SHOW_REAL_NAMES=true (лише в довіреному середовищі).";

    /// <summary>Стабільний код: однаковий вхід завжди дає однаковий псевдонім на цій машині.</summary>
    internal static string PseudonymCode(string kind, string key)
    {
        var salt = _saltOverride ?? LazySalt.Value;
        var hash = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes($"{kind}:{key}"));

        var chars = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
            chars[i] = CodeAlphabet[hash[i] % CodeAlphabet.Length];
        return new string(chars);
    }

    private static byte[] LoadOrCreateSalt()
    {
        try
        {
            if (File.Exists(SaltFilePath))
            {
                var existing = File.ReadAllBytes(SaltFilePath);
                if (existing.Length >= 16)
                    return existing;
            }

            var salt = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Path.GetDirectoryName(SaltFilePath)!);
            var temporary = SaltFilePath + ".tmp";
            File.WriteAllBytes(temporary, salt);
            File.Move(temporary, SaltFilePath, overwrite: true);
            return salt;
        }
        catch (Exception ex)
        {
            // Не валимо сервер через недоступний файл: fallback-сіль стабільна лише в межах процесу.
            Console.Error.WriteLine($"[nzua-privacy] Не вдалося зберегти salt ({ex.Message}) — псевдоніми будуть стабільні лише до перезапуску.");
            return RandomNumberGenerator.GetBytes(32);
        }
    }

    private static string EscapeTable(string value) => value.Replace("|", "\\|");
}
