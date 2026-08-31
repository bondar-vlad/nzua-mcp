using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;

namespace NzuaMcp.Mcp.Resources;

/// <summary>
/// MCP-ресурси. Динамічні (журнали) вимагають активної сесії, але НЕ ініціюють ручний вхід самі:
/// клієнти можуть читати ресурси автоматично, і несподіване вікно браузера тут недоречне.
/// Список учнів окремим ресурсом свідомо не публікується (приватність).
/// </summary>
[McpServerResourceType]
public class NzuaResources(Tools.JournalTools journalTools, NzuaClient client)
{
    private const string LoginRequired =
        "🔒 Немає активної сесії nz.ua. Викличте інструмент nzua_session(action:\"login\"), " +
        "пройдіть вхід у вікні браузера і прочитайте ресурс ще раз.";

    private static readonly IProgress<ProgressNotificationValue> NullProgress = new Progress<ProgressNotificationValue>();

    [McpServerResource(UriTemplate = "nzua://journals", Name = "journals", MimeType = "text/markdown"),
     Description("Список журналів вчителя: предмети, класи, journal_id, семестри.")]
    public async Task<string> Journals()
    {
        if (client.Session is null)
            return LoginRequired;
        return await journalTools.ListJournals();
    }

    [McpServerResource(UriTemplate = "nzua://journal/{journalId}", Name = "journal", MimeType = "text/markdown"),
     Description("Повний журнал за journal_id: учні (псевдоніми), уроки, оцінки, теми та ДЗ.")]
    public async Task<string> Journal(string journalId)
    {
        if (client.Session is null)
            return LoginRequired;
        return await journalTools.GetJournal(journalId, NullProgress);
    }

    [McpServerResource(UriTemplate = "nzua://reference/special-marks", Name = "special-marks", MimeType = "text/markdown"),
     Description("Довідник спеціальних позначок журналу NZ.UA та їх значень.")]
    public static string SpecialMarksReference()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Спеціальні позначки журналу NZ.UA");
        sb.AppendLine();
        sb.AppendLine("| Позначка | Значення | Коли доречна |");
        sb.AppendLine("|----------|----------|--------------|");
        sb.AppendLine("| Н | Відсутній | Пропуск без відомої причини |");
        sb.AppendLine("| Н/А | Відсутній з поважної причини | Довідка/заява батьків |");
        sb.AppendLine("| хв | Хворів | Медична довідка |");
        sb.AppendLine("| зар | Зараховано | Дихотомічна шкала «зараховано/не зараховано» |");
        sb.AppendLine("| зв | Звільнений | Звільнення від занять (напр., фізкультура) |");
        sb.AppendLine("| вивч | Вивчено | Факт опанування без бальної оцінки |");
        sb.AppendLine("| П | Початковий рівень | Лише НУШ 1–4 (рівневе оцінювання) |");
        sb.AppendLine("| С | Середній рівень | Лише НУШ 1–4 |");
        sb.AppendLine("| Д | Достатній рівень | Лише НУШ 1–4 |");
        sb.AppendLine("| В | Високий рівень | Лише НУШ 1–4 |");
        sb.AppendLine("| Н/О | Не оцінено | Учень присутній, але не оцінювався |");
        sb.AppendLine("| заув | Зауваження | Дисциплінарна нотатка (НЕ впливає на оцінку) |");
        sb.AppendLine("| п/п | Пропуск/присутність | Службова позначка присутності |");
        sb.AppendLine("| √ | Виконано | Позначка виконання без балу |");
        sb.AppendLine("| к | Коментар | Комірка лише з коментарем |");
        sb.AppendLine("| Н/З | Не зараховано | Дихотомічна шкала |");
        sb.AppendLine("| delete | Видалити оцінку | Спеціальне значення nzua_set_marks для очищення комірки |");
        sb.AppendLine();
        sb.AppendLine("⚠️ НУШ 5–9 класи: поточні оцінки — числові 1–12; рівневі П/С/Д/В не застосовуються (наказ МОН № 1427 від 17.08.2026).");
        return sb.ToString();
    }

    [McpServerResource(UriTemplate = "nzua://reference/otsinyuvannya", Name = "otsinyuvannya", MimeType = "text/markdown"),
     Description("Конспект чинних правил оцінювання НУШ 5–9 (наказ МОН № 1427 від 17.08.2026) з посиланнями на офіційні матеріали.")]
    public static string GradingReference()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Оцінювання 5–9 класів НУШ — конспект (наказ МОН № 1427 від 17.08.2026)");
        sb.AppendLine();
        sb.AppendLine("## Що фіксується в журналі");
        sb.AppendLine("- Поточні оцінки: у балах 1–12, БЕЗ обов'язкових індексів груп результатів (ГР1–ГР4). Індекси — добровільний робочий інструмент.");
        sb.AppendLine("- Поточні оцінки НЕ обов'язкові на кожному уроці чи за кожне завдання.");
        sb.AppendLine("- Формувальне оцінювання: шкалу обирає вчитель (вербальна/рівнева/бальна); у журнал заноситься лише за бажанням; у підсумкові НЕ враховується.");
        sb.AppendLine("- Вхідні діагностувальні роботи (вересень): інструмент формувального оцінювання, результати не входять у поточне/підсумкове оцінювання.");
        sb.AppendLine();
        sb.AppendLine("## Підсумкове оцінювання");
        sb.AppendLine("- Тематична оцінка (за потреби): узагальнення поточних, АБО оцінка за тематичну роботу, АБО поєднання. Окрема робота після кожної теми не обов'язкова.");
        sb.AppendLine("- Семестрова: ОДНА оцінка з предмета/інтегрованого курсу — на основі тематичних (за відсутності — поточних). Окрема семестрова контрольна — за рішенням вчителя.");
        sb.AppendLine("- Не рекомендується визначати семестрову лише за однією підсумковою роботою, якщо є інша достатня інформація про поступ.");
        sb.AppendLine();
        sb.AppendLine("## Етика");
        sb.AppendLine("- Оцінки — конфіденційна інформація учня та батьків: без публічних рейтингів і озвучування перед класом.");
        sb.AppendLine("- Поведінка, темп роботи, старанність НЕ знижують оцінку з предмета.");
        sb.AppendLine("- Несамостійна робота / повністю згенерована ШІ без дозволу — вважається невиконаною і не оцінюється.");
        sb.AppendLine();
        sb.AppendLine("## Автономія");
        sb.AppendLine("- Учитель сам обирає інструменти, кількість і частотність оцінювань (академічна свобода).");
        sb.AppendLine("- Заклад може запровадити власну шкалу або «зараховано/не зараховано» для окремих предметів (з правилами переведення в 12-бальну).");
        sb.AppendLine();
        sb.AppendLine("## Офіційні матеріали");
        sb.AppendLine("- Наказ МОН № 1427 від 17.08.2026 — рекомендації щодо оцінювання 5–9 класів: https://mon.gov.ua/npa/pro-zatverdzhennia-rekomendatsii-shchodo-otsiniuvannia-rezultataiv-navchannia-uchniv-5-9-klasiv-zakladiv-zahalnoi-serednoi-osvity");
        sb.AppendLine("- Наказ МОН № 1430 від 17.08.2026 — Типова освітня програма 5–9 класів: https://mon.gov.ua/npa/pro-vnesennia-zmin-do-typovoi-osvitnoi-prohramy-dlia-5-9-klasiv-zakladiv-zahalnoi-serednoi-osvity-2");
        sb.AppendLine("- Посібники «Як оцінювати в НУШ?»: https://mon.gov.ua/osvita-2/zagalna-serednya-osvita/nova-ukrainska-shkola-2/otsinyuvannya/metodychnyi-posibnyk-iak-otsiniuvaty-v-nush");
        sb.AppendLine("- Каталог компетентнісних робіт для НУШ: https://nus-tasks.net/");
        sb.AppendLine("- «Інтерактивний поступ» (платформа «Освіта для життя»): https://educationforlife.mon.gov.ua/interaktyvnyi-postup/");
        sb.AppendLine();
        sb.AppendLine("_Конспект станом на вересень 2026. Першоджерело завжди — чинні накази МОН._");
        return sb.ToString();
    }
}
