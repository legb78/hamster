using System.Text.Json;

namespace Hamster.Activity.Tests;

/// <summary>
/// Lignes de transcript synthetiques, a la forme observee (champs inventes, aucun contenu reel).
/// </summary>
static class Jsonl
{
    public const string Session = "11111111-2222-3333-4444-555555555555";
    public const string Cwd = @"C:\work\demo-projet";
    public static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    public static string Ts(double seconds) => T0.AddSeconds(seconds).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    static string Line(object o) => JsonSerializer.Serialize(o);

    static Dictionary<string, object?> Base(string type, double at, string? cwd = null, string? agentId = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["parentUuid"] = null,
            ["isSidechain"] = agentId != null,
            ["type"] = type,
            ["sessionId"] = Session,
            ["timestamp"] = Ts(at),
            ["cwd"] = cwd ?? Cwd,
            ["gitBranch"] = "main",
            ["entrypoint"] = "claude-vscode",
            ["uuid"] = Guid.NewGuid().ToString(),
        };
        if (agentId != null) d["agentId"] = agentId;
        return d;
    }

    public static object Text(string text) => new { type = "text", text };
    public static object Thinking() => new { type = "thinking", thinking = "..." };
    public static object ToolUse(string name, string id) => new { type = "tool_use", id, name, input = new { } };
    public static object ToolResult(string id, bool error = false) => new { type = "tool_result", tool_use_id = id, content = "sortie", is_error = error };

    public static string Assistant(double at, string? stop, params object[] blocks) => AssistantAs(null, at, stop, blocks);

    public static string AssistantAs(string? agentId, double at, string? stop, params object[] blocks)
    {
        var d = Base("assistant", at, agentId: agentId);
        d["message"] = new { id = "msg_1", type = "message", role = "assistant", model = "claude-test", content = blocks, stop_reason = stop, stop_sequence = (string?)null };
        return Line(d);
    }

    public static string ApiErrorMessage(double at)
    {
        var d = Base("assistant", at);
        d["isApiErrorMessage"] = true;
        d["error"] = "rate_limit";
        d["message"] = new { id = "msg_2", role = "assistant", model = "<synthetic>", content = new[] { Text("limite atteinte") }, stop_reason = "stop_sequence" };
        return Line(d);
    }

    public static string Synthetic(double at)
    {
        var d = Base("assistant", at);
        d["message"] = new { id = "msg_3", role = "assistant", model = "<synthetic>", content = new[] { Text("rien") }, stop_reason = "stop_sequence" };
        return Line(d);
    }

    public static string Prompt(double at, string text = "fais ceci", string? cwd = null) => UserString(at, text, cwd: cwd);

    public static string UserString(double at, string text, bool meta = false, string? cwd = null, string? agentId = null)
    {
        var d = Base("user", at, cwd, agentId);
        if (meta) d["isMeta"] = true;
        d["message"] = new { role = "user", content = text };
        return Line(d);
    }

    public static string UserBlocks(double at, object[] blocks, string? agentId = null, bool toolEndsTurn = false, bool meta = false, bool compact = false)
    {
        var d = Base("user", at, agentId: agentId);
        if (toolEndsTurn) d["toolEndsTurn"] = true;
        if (meta) d["isMeta"] = true;
        if (compact) d["isCompactSummary"] = true;
        d["message"] = new { role = "user", content = blocks };
        return Line(d);
    }

    public static string Result(double at, string id, bool error = false, string? agentId = null, bool toolEndsTurn = false) =>
        UserBlocks(at, new[] { ToolResult(id, error) }, agentId, toolEndsTurn);

    public static string SystemLine(double at, string subtype)
    {
        var d = Base("system", at);
        d["subtype"] = subtype;
        d["level"] = "error";
        return Line(d);
    }

    public static string TaskNotification(double at, string taskId, string status) =>
        UserString(at, $"<task-notification>\n<task-id>{taskId}</task-id>\n<tool-use-id>toolu_x</tool-use-id>\n<status>{status}</status>\n<summary>fini</summary>\n</task-notification>");

    public static string Other(string type, double at)
    {
        var d = Base(type, at);
        return Line(d);
    }
}
