using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace NzuaMcp.Mcp.Prompts;

/// <summary>
/// Готові робочі сценарії для вчителя. Нормативна база: наказ МОН № 1427 від 17.08.2026
/// (рекомендації щодо оцінювання результатів навчання учнів 5–9 класів НУШ) та наказ МОН № 1430
/// від 17.08.2026 (Типова освітня програма 5–9 класів). Для інших рівнів формулювання нейтральні.
/// </summary>
[McpServerPromptType]
public static class NzuaPrompts
{
    private const string GradingSystemDescription =
        "Система оцінювання класу: nus_5_9 (НУШ 5–9, 12-бальна, наказ МОН № 1427), " +
        "nus_1_4 (НУШ 1–4, рівнева П/С/Д/В), traditional (10–11(12) класи, 12-бальна), " +
        "custom (власна шкала закладу або зараховано/не зараховано)";

    private const string JournalIdDescription =
        "ID журналу (є автодоповнення після першого nzua_list_journals). Не знаєте — залиште порожнім: AI спершу покаже список і уточнить вибір.";

    private static string JournalLabel(string? journalId) =>
        string.IsNullOrWhiteSpace(journalId) ? "(обери на кроці 0)" : journalId;

    private static void AppendResolveStep(StringBuilder sb, string? journalId)
    {
        if (string.IsNullOrWhiteSpace(journalId))
            sb.AppendLine("0. journalId не вказано: виклич nzua_list_journals, покажи список журналів і уточни у вчителя, з яким працювати.");
    }

    private static string GradingRules(string gradingSystem) => gradingSystem switch
    {
        "nus_5_9" =>
            "Нормативні правила (наказ МОН № 1427 від 17.08.2026, НУШ 5–9):\n" +
            "- Оцінки числові 1–12; рівневі позначки П/С/Д/В у поточному журналі 5–9 класів НЕ використовуються.\n" +
            "- Індекси груп результатів (ГР1–ГР4) необов'язкові — лише як добровільний робочий інструмент вчителя.\n" +
            "- Формувальне оцінювання НЕ враховується у підсумкові оцінки; фіксується у журналі лише за бажанням вчителя.\n" +
            "- Вхідні діагностувальні роботи (вересень) — інструмент формувального оцінювання: їхні результати не впливають на поточні/підсумкові оцінки.\n" +
            "- Поточні оцінки не обов'язкові на кожному уроці чи за кожне завдання.\n" +
            "- Тематична оцінка: узагальнення поточних, АБО оцінка за тематичну роботу, АБО їх поєднання. Окрема робота після кожної теми не обов'язкова.\n" +
            "- Семестрова: ОДНА оцінка з предмета на основі тематичних (за їх відсутності — поточних); окрема семестрова контрольна — за рішенням вчителя, не обов'язкова.\n" +
            "- Оцінки конфіденційні: без публічних рейтингів і порівнянь. Поведінка, темп роботи чи старанність не знижують оцінку з предмета.",
        "nus_1_4" =>
            "Правила для НУШ 1–4: рівневе/вербальне оцінювання (П — початковий, С — середній, Д — достатній, В — високий) " +
            "згідно з чинними рекомендаціями МОН для початкової школи. Бали 1–12 не застосовуються. " +
            "Формувальне оцінювання — основний інструмент; підсумкове — за рівнями у свідоцтві досягнень.",
        "traditional" =>
            "Правила для 10–11(12) класів: 12-бальна шкала, тематичне → семестрове → річне оцінювання " +
            "згідно з чинними нормативами МОН для профільної середньої школи. Річна виводиться на основі семестрових.",
        _ =>
            "Заклад використовує власну шкалу або дихотомічну «зараховано/не зараховано». " +
            "Перед аналізом уточніть у вчителя правила переведення шкали закладу та зафіксовані в освітній програмі підходи.",
    };

    private const string SafetyFooter =
        "\n\nОбов'язкові правила роботи:\n" +
        "1. Спочатку прочитай журнал через nzua_get_journal — не роби висновків без даних.\n" +
        "2. Будь-які масові зміни — ОДНИМ викликом з entriesJson, після підтвердження вчителем.\n" +
        "3. Підсумкові (тематичні/семестрові/річні) оцінки НЕ виставляй автоматично: підготуй чернетку-пропозицію, рішення ухвалює вчитель.\n" +
        "4. Після будь-якого запису перевір результат повторним nzua_get_journal.\n" +
        "5. Учні в даних позначені стабільними псевдонімами — використовуй їх, не намагайся відновити реальні імена.";

    [McpServerPrompt(Name = "journal_audit"), Description(
        "Аудит повноти журналу: уроки без тем/номерів КТП/ДЗ, пропуски дат, учні без оцінок. Готує план виправлень.")]
    public static string JournalAudit(
        [Description(JournalIdDescription)] string? journalId = null,
        [Description("Період аналізу, напр. 'вересень', '01.09–30.10' або 'семестр'. Порожньо = весь журнал.")] string? period = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Проведи аудит повноти журналу {JournalLabel(journalId)} у NZ.UA{(string.IsNullOrWhiteSpace(period) ? "" : $" за період: {period}")}.");
        sb.AppendLine();
        sb.AppendLine("Кроки:");
        AppendResolveStep(sb, journalId);
        sb.AppendLine("1. Заванта́ж журнал повністю: nzua_get_journal(journalId, include: \"students,lessons,marks,homework\").");
        sb.AppendLine("2. Знайди і зведи в таблиці:");
        sb.AppendLine("   - уроки БЕЗ теми (порожнє поле topic);");
        sb.AppendLine("   - уроки БЕЗ номера в календарному плані;");
        sb.AppendLine("   - уроки БЕЗ домашнього завдання (окрім контрольних/тематичних, де ДЗ може бути недоречним);");
        sb.AppendLine("   - учнів, які не мають жодної оцінки за період;");
        sb.AppendLine("   - підозрілі розриви в датах уроків (більше тижня без уроку в робочі тижні).");
        sb.AppendLine("3. Для кожної групи проблем запропонуй конкретний план виправлення одним batch-викликом:");
        sb.AppendLine("   теми/номери/ДЗ — nzua_set_homework з entriesJson; нові уроки — nzua_add_lessons з entriesJson.");
        sb.AppendLine("4. Покажи план вчителю і чекай явного підтвердження перед будь-яким записом.");
        sb.Append(SafetyFooter);
        return sb.ToString();
    }

    [McpServerPrompt(Name = "semester_prep"), Description(
        "Підготовка до виставлення семестрових: зведення тематичних/поточних оцінок по кожному учню + чернетка для рішення вчителя.")]
    public static string SemesterPrep(
        [Description(JournalIdDescription)] string? journalId = null,
        [Description(GradingSystemDescription)] string gradingSystem = "nus_5_9",
        [Description("Семестр: 1 або 2")] int semester = 1)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Підготуй зведення для виставлення оцінок за {semester}-й семестр у журналі {JournalLabel(journalId)}.");
        sb.AppendLine();
        sb.AppendLine(GradingRules(gradingSystem));
        sb.AppendLine();
        sb.AppendLine("Кроки:");
        AppendResolveStep(sb, journalId);
        sb.AppendLine("1. Заванта́ж журнал: nzua_get_journal(journalId). Тематичні/семестрові колонки можна виділити через lessonTypeId (див. опис інструмента).");
        sb.AppendLine("2. Для КОЖНОГО учня зведи таблицю: кількість поточних оцінок, тематичні оцінки, пропуски (Н/хв), спецпозначки (зв, вивч, Н/О...).");
        sb.AppendLine("3. Познач учнів, для яких даних замало для семестрової (немає тематичних і менш як 2–3 поточні) — їм потрібна увага вчителя в першу чергу.");
        sb.AppendLine("4. Як ДОВІДКУ (не як готову оцінку) порахуй середнє тематичних для кожного учня і діапазон коливань.");
        sb.AppendLine("5. Сформуй чернетку семестрових ЯК ПРОПОЗИЦІЮ в таблиці «учень → дані → пропозиція → поле для рішення вчителя».");
        sb.AppendLine("   НЕ виставляй семестрові оцінки жодним викликом інструментів, доки вчитель явно не затвердить кожну.");
        sb.AppendLine("6. Після затвердження — один batch-виклик nzua_set_marks на семестрову колонку (scheduleId семестрової колонки з журналу).");
        sb.Append(SafetyFooter);
        return sb.ToString();
    }

    [McpServerPrompt(Name = "marks_compliance"), Description(
        "Перевірка оцінок на відповідність системі оцінювання: шкала, доречність спецпозначок, вересневі діагностики.")]
    public static string MarksCompliance(
        [Description(JournalIdDescription)] string? journalId = null,
        [Description(GradingSystemDescription)] string gradingSystem = "nus_5_9",
        [Description("Номер класу (5–12), якщо відомий — для точніших порад")] int? classNumber = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Перевір оцінки журналу {JournalLabel(journalId)}{(classNumber.HasValue ? $" ({classNumber} клас)" : "")} на відповідність системі оцінювання.");
        sb.AppendLine();
        sb.AppendLine(GradingRules(gradingSystem));
        sb.AppendLine();
        sb.AppendLine("Чек-лист перевірки:");
        AppendResolveStep(sb, journalId);
        sb.AppendLine("1. Заванта́ж журнал: nzua_get_journal(journalId).");
        if (gradingSystem == "nus_5_9")
        {
            sb.AppendLine("2. Знайди рівневі позначки П/С/Д/В у поточних колонках — для 5–9 класів їх бути не повинно, лише 1–12.");
            sb.AppendLine("3. Перевір вересневі уроки з позначками «діагностувальна»: оцінки за них не мають входити в підсумкові.");
        }
        else
        {
            sb.AppendLine("2. Перевір, що всі оцінки відповідають шкалі системи (див. правила вище).");
            sb.AppendLine("3. Перевір узгодженість підсумкових колонок із поточними даними.");
        }
        sb.AppendLine("4. Перевір доречність спецпозначок (довідка — ресурс nzua://reference/special-marks): «зар» без числової оцінки там, де очікується бал; «Н/О» на уроках, де решта класу оцінена, тощо.");
        sb.AppendLine("5. Аномалії (різкі одиничні низькі бали, серії однакових оцінок) познач ЯК ПИТАННЯ вчителю — без оцінних суджень і без автоматичних виправлень.");
        sb.AppendLine("6. Підсумуй знахідки таблицею: «урок → учень (псевдонім) → що не так → пропозиція». Виправлення — лише після підтвердження, одним batch-викликом nzua_set_marks.");
        sb.Append(SafetyFooter);
        return sb.ToString();
    }

    [McpServerPrompt(Name = "attendance_review"), Description(
        "Аналіз відвідуваності: патерни Н/хв по учнях, датах і днях тижня, таблиця для класного керівника.")]
    public static string AttendanceReview(
        [Description(JournalIdDescription)] string? journalId = null,
        [Description("Період аналізу, напр. 'вересень' або '01.09–30.10'. Порожньо = весь журнал.")] string? period = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Проаналізуй відвідуваність у журналі {JournalLabel(journalId)}{(string.IsNullOrWhiteSpace(period) ? "" : $" за період: {period}")}.");
        sb.AppendLine();
        sb.AppendLine("Кроки:");
        AppendResolveStep(sb, journalId);
        sb.AppendLine("1. Заванта́ж журнал: nzua_get_journal(journalId, include: \"students,lessons,marks\").");
        sb.AppendLine("2. Виділи всі позначки відсутності: Н (відсутній), хв (хвороба), Н/А (відсутній з поважної причини).");
        sb.AppendLine("3. Зведи по кожному учню: кількість Н / хв / Н/А, частка пропущених уроків у %.");
        sb.AppendLine("4. Знайди патерни: пропуски в конкретні дні тижня, серії поспіль (3+ уроки), збіги пропусків із контрольними роботами.");
        sb.AppendLine("5. Сформуй підсумкову таблицю для класного керівника: «учень (псевдонім) → пропуски → патерн → рекомендація уваги (так/ні)».");
        sb.AppendLine("6. Пам'ятай: це аналітика для вчителя, а не підстава для санкцій. Пропуски не впливають на оцінки з предмета.");
        sb.Append(SafetyFooter);
        return sb.ToString();
    }

    [McpServerPrompt(Name = "lesson_plan_hygiene"), Description(
        "Гігієна календарного плану: послідовність номерів уроків у КТП, дублікати, дірки в розкладі.")]
    public static string LessonPlanHygiene(
        [Description(JournalIdDescription)] string? journalId = null,
        [Description("Планова кількість уроків на тиждень за навчальним планом (напр. 2)")] int? hoursPerWeek = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Перевір відповідність журналу {JournalLabel(journalId)} календарно-тематичному плану.");
        sb.AppendLine();
        sb.AppendLine("Кроки:");
        AppendResolveStep(sb, journalId);
        sb.AppendLine("1. Заванта́ж журнал: nzua_get_journal(journalId, include: \"lessons,homework\").");
        sb.AppendLine("2. Перевір номери уроків у КТП (lesson_number_in_plan): послідовність без пропусків, без дублікатів, без порожніх там, де в сусідніх уроках номери є.");
        if (hoursPerWeek.HasValue)
            sb.AppendLine($"3. Звір фактичну кількість уроків на тиждень із плановою ({hoursPerWeek}/тиждень): знайди тижні з недобором чи перебором.");
        else
            sb.AppendLine("3. Оціни рівномірність уроків по тижнях; якщо вчитель повідомить планову кількість годин на тиждень — звір із нею.");
        sb.AppendLine("4. Знайди уроки з однаковою датою і часом (дублікати) та уроки, що випадають з хронологічного порядку номерів КТП.");
        sb.AppendLine("5. Сформуй план виправлень: перенумерація — nzua_set_homework (entriesJson з lessonNumber); перенесення дат — nzua_edit_lessons (entriesJson з lessonDate); зайві уроки — nzua_delete_lessons (лише без оцінок!).");
        sb.AppendLine("6. Покажи план вчителю і чекай підтвердження перед записом.");
        sb.Append(SafetyFooter);
        return sb.ToString();
    }
}
