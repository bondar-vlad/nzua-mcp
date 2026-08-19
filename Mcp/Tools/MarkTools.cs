using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;
using NzuaMcp.Nzua.Api;

namespace NzuaMcp.Mcp.Tools;

[McpServerToolType]
public class MarkTools(MarksApi marksApi)
{
    [McpServerTool(Name = "nzua_set_marks"), Description(
        "Виставляє оцінки. Для кількох оцінок ЗАВЖДИ ОДИН виклик з entriesJson: масив на 30 оцінок — це один виклик, а не 30. " +
        "В одному масиві можна змішувати різні уроки й різних учнів. " +
        "Один учень на одному уроці: scheduleId + studentId + grade/specialMark. " +
        "Кілька учнів: scheduleId + entriesJson [{studentId, mark?, grade?, specialMark?, comment?}]. " +
        "Кілька учнів на кількох уроках: entriesJson [{scheduleId, studentId, mark?, ...}] (scheduleId в кожному записі). " +
        "mark — універсальне поле: приймає число (10 або \"10\") або спеціальну позначку (\"Н\") — використовувати замість grade/specialMark. " +
        "grade: 1-12 (тільки числова). specialMark: Н, Н/А, хв, зар, зв, вивч, П, С, Д, В, Н/О, заув, п/п, √, к, Н/З, delete. " +
        "⚠️ НУШ 5–9 клас: числові оцінки 1–12 — НЕ використовувати П/С/Д/В. " +
        "Підсумкові й семестрові оцінки не виставляйте за середнім — рішення приймає педагог. " +
        "studentId може бути числом або рядком. " +
        "💡 Після масових змін перевірте результат через nzua_get_journal.")]
    public async Task<string> SetMarks(
        [Description("ID уроку (schedule_id). Якщо всі записи в entriesJson мають власний scheduleId — можна не вказувати.")] string? scheduleId = null,
        [Description("ID учня (для одного учня)")] string? studentId = null,
        [Description("Числова оцінка 1-12 (для одного учня)")] int? grade = null,
        [Description("Спеціальна позначка (для одного учня): Н, хв, П, С, Д, В, к (Коментар), delete тощо")] string? specialMark = null,
        [Description("Коментар (для одного учня)")] string? comment = null,
        [Description("JSON масив. Рекомендований формат: [{\"studentId\":123,\"mark\":10}] або [{\"studentId\":123,\"mark\":\"Н\"}]. studentId — число або рядок.")] string? entriesJson = null)
    {
        try
        {
            // Batch mode
            if (!string.IsNullOrEmpty(entriesJson))
            {
                var entries = JsonSerializer.Deserialize<List<BulkSetMarkEntry>>(entriesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new NzuaException("Невірний формат entries");

                var flat = new List<FlatMarkEntry>();
                foreach (var e in entries)
                {
                    var sid = e.ScheduleId ?? scheduleId;
                    if (string.IsNullOrEmpty(sid))
                        return "❌ Вкажіть scheduleId (глобально або в кожному записі entriesJson)";
                    var markId = MarkValueResolver.Resolve(e.Mark, e.Grade, e.SpecialMark);
                    flat.Add(new FlatMarkEntry(sid, e.StudentId, markId, e.Comment));
                }

                var results = await marksApi.BulkSetMarksFlat(flat);
                var ok = results.Count(r => r.Success);
                var fail = results.Where(r => !r.Success).ToList();

                var text = $"✅ Виставлено {ok}/{results.Count} оцінок\n";
                if (fail.Count > 0)
                {
                    text += "\n❌ Помилки:\n";
                    foreach (var f in fail)
                        text += $"  - {f.Id}: {f.Error}\n";
                }
                return text;
            }

            // Single mode
            if (string.IsNullOrEmpty(scheduleId))
                return "❌ Вкажіть scheduleId або entriesJson";
            if (string.IsNullOrEmpty(studentId))
                return "❌ Вкажіть studentId (один учень) або entriesJson (масив)";
            if (!grade.HasValue && string.IsNullOrEmpty(specialMark))
                return "❌ Вкажіть grade (1-12) або specialMark";

            var singleMarkId = MarkValueResolver.Resolve(grade: grade, specialMark: specialMark);

            await marksApi.SetMark(new SetMarkParams(scheduleId, studentId, singleMarkId, comment));
            var displayVal = MarkDisplay.Get(singleMarkId);
            var commentStr = !string.IsNullOrEmpty(comment) ? $" з коментарем \"{comment}\"" : "";
            return $"✅ Оцінку {displayVal} виставлено{commentStr}";
        }
        catch (Exception ex)
        {
            return $"❌ {ex.Message}";
        }
    }
}

public class BulkSetMarkEntry
{
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string StudentId { get; set; } = "";
    public int? Grade { get; set; }
    public string? SpecialMark { get; set; }
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string? Mark { get; set; }
    public string? Comment { get; set; }
    public string? ScheduleId { get; set; }
}

public class JsonStringOrNumberConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return reader.TokenType == JsonTokenType.Number
            ? reader.GetInt64().ToString()
            : reader.GetString();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
