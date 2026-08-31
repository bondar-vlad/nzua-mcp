using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using NzuaMcp.Nzua;

namespace NzuaMcp.Tests;

public class McpSurfaceTests
{
    [Fact]
    public void ExposedToolsHaveUniqueNamesAndDescriptions()
    {
        var methods = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => (Method: method, Tool: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(item => item.Tool is not null)
            .ToList();
        var names = methods.Select(item => item.Tool!.Name ?? item.Method.Name).ToList();

        Assert.Equal(9, methods.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(methods, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.Method.GetCustomAttribute<DescriptionAttribute>()?.Description)));

        string[] expected =
        [
            "nzua_session",
            "nzua_list_journals",
            "nzua_get_journal",
            "nzua_get_form",
            "nzua_set_marks",
            "nzua_add_lessons",
            "nzua_edit_lessons",
            "nzua_delete_lessons",
            "nzua_set_homework",
        ];
        Assert.Equal(expected.Order(), names.Order());
    }

    [Fact]
    public void RemovedToolsAreNoLongerExposed()
    {
        var names = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(tool => tool is not null)
            .Select(tool => tool!.Name)
            .ToList();

        string[] removed =
        [
            "nzua_admin_capabilities",
            "nzua_request_history", "nzua_undo_info", "nzua_history_info",
            "nzua_grade_summary", "nzua_topic_analysis", "nzua_theme_analysis",
            "nzua_attendance_stats", "nzua_student_detail", "nzua_lesson_marks",
            "nzua_gr_coverage", "nzua_gr_student_summary",
            "nzua_move_lesson", "nzua_get_students", "nzua_get_lessons",
            "nzua_get_lesson_form", "nzua_get_homework_form",
            "nzua_session_status", "nzua_manual_login", "nzua_logout", "nzua_change_semester",
        ];
        Assert.All(removed, name => Assert.DoesNotContain(name, names));
    }

    [Fact]
    public void PromptsAreExposedWithUniqueNamesAndDescriptions()
    {
        var methods = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => (Method: method, Prompt: method.GetCustomAttribute<McpServerPromptAttribute>()))
            .Where(item => item.Prompt is not null)
            .ToList();
        var names = methods.Select(item => item.Prompt!.Name ?? item.Method.Name).ToList();

        string[] expected =
        [
            "journal_audit",
            "semester_prep",
            "marks_compliance",
            "attendance_review",
            "lesson_plan_hygiene",
        ];
        Assert.Equal(expected.Order(), names.Order());
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(methods, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.Method.GetCustomAttribute<DescriptionAttribute>()?.Description)));
    }

    [Fact]
    public void PromptsAlwaysCarryTeacherDecisionSafeguard()
    {
        // Правило репозиторію: жодних автоматичних підсумкових оцінок — рішення за вчителем.
        var prompts = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<McpServerPromptAttribute>() is not null);

        foreach (var prompt in prompts)
        {
            // Єдиний обов'язковий параметр промптів — journalId; решта мають default-значення.
            var arguments = prompt.GetParameters()
                .Select(p => p.HasDefaultValue ? p.DefaultValue : "journal-1")
                .ToArray();
            var text = (string)prompt.Invoke(null, arguments)!;
            Assert.Contains("рішення ухвалює вчитель", text);
        }
    }

    [Fact]
    public void ResourcesAreExposedWithExpectedUris()
    {
        var methods = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => (Method: method, Resource: method.GetCustomAttribute<McpServerResourceAttribute>()))
            .Where(item => item.Resource is not null)
            .ToList();
        var uris = methods.Select(item => item.Resource!.UriTemplate).ToList();

        string[] expected =
        [
            "nzua://journals",
            "nzua://journal/{journalId}",
            "nzua://reference/special-marks",
            "nzua://reference/otsinyuvannya",
        ];
        Assert.Equal(expected.Order(), uris.Order());
        Assert.All(methods, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.Method.GetCustomAttribute<DescriptionAttribute>()?.Description)));
    }

    [Fact]
    public void StudentListResourceIsNotExposed()
    {
        // Приватність: окремого ресурсу зі списком учнів бути не повинно.
        var uris = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerResourceAttribute>()?.UriTemplate)
            .Where(uri => uri is not null)
            .ToList();

        Assert.DoesNotContain(uris, uri => uri!.Contains("student", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EverySurfaceItemHasHumanReadableTitle()
    {
        var methods = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .ToList();

        Assert.All(
            methods.Select(m => m.GetCustomAttribute<McpServerToolAttribute>()).Where(a => a is not null),
            tool => Assert.False(string.IsNullOrWhiteSpace(tool!.Title)));
        Assert.All(
            methods.Select(m => m.GetCustomAttribute<McpServerPromptAttribute>()).Where(a => a is not null),
            prompt => Assert.False(string.IsNullOrWhiteSpace(prompt!.Title)));
        Assert.All(
            methods.Select(m => m.GetCustomAttribute<McpServerResourceAttribute>()).Where(a => a is not null),
            resource => Assert.False(string.IsNullOrWhiteSpace(resource!.Title)));
    }

    [Fact]
    public void ReadToolsAreAnnotatedReadOnly()
    {
        var tools = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .ToDictionary(a => a!.Name!, a => a!);

        Assert.True(tools["nzua_get_journal"].ReadOnly);
        Assert.True(tools["nzua_get_form"].ReadOnly);
        // Write-інструменти не мають бути read-only.
        Assert.False(tools["nzua_set_marks"].ReadOnly);
        Assert.False(tools["nzua_delete_lessons"].ReadOnly);
    }

    [Fact]
    public void Completions_SuggestJournalIdsFromCacheAndStaticEnums()
    {
        List<JournalListItem> cached =
        [
            new("14147143", "Алгебра", "10-В"),
            new("14126039", "математика", "6--"),
        ];

        var journals = Mcp.NzuaCompletions.Resolve(new ModelContextProtocol.Protocol.CompleteRequestParams
        {
            Ref = new ModelContextProtocol.Protocol.PromptReference { Name = "semester_prep" },
            Argument = new ModelContextProtocol.Protocol.Argument { Name = "journalId", Value = "1414" },
        }, cached);
        Assert.Equal(["14147143"], journals.Completion.Values);

        var grading = Mcp.NzuaCompletions.Resolve(new ModelContextProtocol.Protocol.CompleteRequestParams
        {
            Ref = new ModelContextProtocol.Protocol.PromptReference { Name = "semester_prep" },
            Argument = new ModelContextProtocol.Protocol.Argument { Name = "gradingSystem", Value = "nus" },
        }, cached);
        Assert.Equal(["nus_5_9", "nus_1_4"], grading.Completion.Values);

        var unknown = Mcp.NzuaCompletions.Resolve(new ModelContextProtocol.Protocol.CompleteRequestParams
        {
            Ref = new ModelContextProtocol.Protocol.ResourceTemplateReference { Uri = "nzua://journal/{journalId}" },
            Argument = new ModelContextProtocol.Protocol.Argument { Name = "journalId", Value = "" },
        }, []);
        Assert.Empty(unknown.Completion.Values);
    }
}
