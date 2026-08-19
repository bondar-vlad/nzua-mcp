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
    public void NoPromptsAreExposed()
    {
        var methods = typeof(NzuaClient).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<McpServerPromptAttribute>() is not null)
            .ToList();

        Assert.Empty(methods);
    }
}
