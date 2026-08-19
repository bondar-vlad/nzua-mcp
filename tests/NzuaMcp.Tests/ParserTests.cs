using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public class ParserTests
{
    [Fact]
    public async Task ParseLessonForm_ReadsLiveNusOptionsAndCurrentValues()
    {
        const string html = """
            <html><body>
              <form>
                <input name="_csrf" value="test-csrf" />
                <select id="lesson_type_id" name="OsvitaScheduleReal[lesson_type_id]">
                  <optgroup label="МАТЕМАТИЧНА">
                    <option value="9001" selected>ГР1 Моделює ситуації</option>
                    <option value="9002">ГР2 Розв'язує задачі</option>
                  </optgroup>
                </select>
                <select id="osvitaschedulereal-nus_lesson_type_id" name="OsvitaScheduleReal[nus_lesson_type_id]">
                  <option value="501" selected>Поточна</option>
                  <option value="502">Підсумкова</option>
                  <option value="503">Семестрова</option>
                </select>
                <select id="osvitaschedulereal-buzzer_id" name="OsvitaScheduleReal[buzzer_id]">
                  <option value="bell-2" selected>2 урок</option>
                </select>
                <select id="osvitaschedulereal-room_id" name="OsvitaScheduleReal[room_id]">
                  <option value="room-7" selected>Кабінет 7</option>
                </select>
              </form>
            </body></html>
            """;

        var form = await NzuaParser.ParseLessonForm(html);

        Assert.True(form.IsNus);
        Assert.Equal("test-csrf", form.Csrf);
        Assert.Collection(
            form.NusIndices!,
            first =>
            {
                Assert.Equal("9001", first.Id);
                Assert.Equal("МАТЕМАТИЧНА", first.Group);
            },
            second => Assert.Equal("9002", second.Id));
        Assert.Equal(["501", "502", "503"], form.NusLessonTypes!.Select(option => option.Id));
        Assert.Equal("9001", form.CurrentValues!["OsvitaScheduleReal[lesson_type_id]"]);
        Assert.Equal("501", form.CurrentValues["OsvitaScheduleReal[nus_lesson_type_id]"]);
    }
}
