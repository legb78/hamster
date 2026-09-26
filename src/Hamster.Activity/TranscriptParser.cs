using System.Text.Json;

namespace Hamster.Activity;

/// <summary>
/// Traduit une ligne de transcript Claude Code en evenements. Le format est interne et non
/// documente : tout ce qui n'est pas reconnu donne une liste vide, jamais une exception.
/// Fonction pure : aucun etat, aucune horloge, le temps vient du champ timestamp.
/// </summary>
public static class TranscriptParser
{
    static readonly IReadOnlyList<ActivityEvent> None = Array.Empty<ActivityEvent>();

    // des tool_use imbriques peuvent depasser la profondeur par defaut de 64
    static readonly JsonDocumentOptions Options = new() { MaxDepth = 256 };

    /// <summary>
    /// Exceptions levees en interpretant un JSON valide : toujours avalees, mais comptees,
    /// pour que le test de relecture voie un cas de forme que le code ne prevoit pas.
    /// </summary>
    internal static long InterpretErrors;

    public static IReadOnlyList<ActivityEvent> ParseLine(string line, TranscriptSource source)
    {
        if (string.IsNullOrWhiteSpace(line) || source is null) return None;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line, Options); }
        catch (Exception) { return None; }
        using (doc) return Interpret(doc.RootElement, source);
    }

    /// <summary>Variante sur des octets UTF-8 : le watcher evite ainsi un aller-retour par string.</summary>
    internal static IReadOnlyList<ActivityEvent> ParseLine(ReadOnlyMemory<byte> utf8, TranscriptSource source) =>
        ParseLine(utf8, source, out _);

    /// <summary>valid : faux si la ligne n'est pas du JSON (sert au test de relecture).</summary>
    internal static IReadOnlyList<ActivityEvent> ParseLine(ReadOnlyMemory<byte> utf8, TranscriptSource source, out bool valid)
    {
        valid = false;
        if (utf8.IsEmpty || source is null) return None;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8, Options); }
        catch (Exception) { return None; }
        valid = true;
        using (doc) return Interpret(doc.RootElement, source);
    }

    static IReadOnlyList<ActivityEvent> Interpret(JsonElement root, TranscriptSource source)
    {
        try { return Parse(root, source); }
        catch (Exception)
        {
            Interlocked.Increment(ref InterpretErrors);
            return None;
        }
    }

    static IReadOnlyList<ActivityEvent> Parse(JsonElement root, TranscriptSource source)
    {
        if (root.ValueKind != JsonValueKind.Object) return None;
        // seuls ces trois types disent quelque chose de l'activite ; attachment, mode,
        // queue-operation, file-history-snapshot, ai-title, etc. sont ignores
        string? type = Str(root, "type");
        if (type is not ("assistant" or "user" or "system")) return None;
        if (!root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.String
            || !ts.TryGetDateTimeOffset(out var time)) return None;

        var line = new Line(time, source, Str(root, "cwd"));
        switch (type)
        {
            case "system": System(root, line); break;
            case "assistant": Assistant(root, line); break;
            default: User(root, line); break;
        }
        return line.Events.Count == 0 ? None : line.Events;
    }

    static void System(JsonElement root, Line line)
    {
        // api_error = nouvelle tentative apres une erreur d'API : Claude continue de travailler.
        // stop_hook_summary, compact_boundary, local_command, informational... : rien a en dire
        if (Str(root, "subtype") == "api_error") line.Add(ActivityKind.ApiError, isError: true, detail: "api_error");
    }

    static void Assistant(JsonElement root, Line line)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;

        // erreur d'API definitive (quota, auth, surcharge) : Claude Code l'ecrit comme une reponse
        // fabriquee, et le tour s'arrete la
        if (Bool(root, "isApiErrorMessage"))
        {
            line.Add(ActivityKind.ApiError, isError: true, detail: Str(root, "error") ?? "api_error");
            line.End("api_error");
            return;
        }
        // autres messages fabriques, comme "No response requested." apres une commande locale
        if (Str(msg, "model") == "<synthetic>")
        {
            line.End("synthetic");
            return;
        }

        var tools = new List<(string Name, string? Id)>();
        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var block in content.EnumerateArray())
                if (block.ValueKind == JsonValueKind.Object && Str(block, "type") == "tool_use" && Str(block, "name") is { } name)
                    tools.Add((name, Str(block, "id")));

        if (line.IsSubagent) line.Add(ActivityKind.SubagentActivity, detail: line.Source.AgentDescription);

        foreach (var (name, id) in tools)
        {
            // AskUserQuestion et ExitPlanMode attendent une reponse de l'utilisateur
            bool asks = !line.IsSubagent && name is "AskUserQuestion" or "ExitPlanMode";
            line.Add(asks ? ActivityKind.AskedUser : ActivityKind.ToolStarted, tool: name, detail: id);
        }

        // un message est ecrit bloc par bloc, une ligne par bloc, et chaque ligne porte le
        // stop_reason final (null sur les blocs intermediaires chez les sous-agents) : une
        // fin se voit donc souvent deux fois, le modele l'absorbe
        string? stop = Str(msg, "stop_reason");
        if (tools.Count == 0 && stop is "end_turn" or "stop_sequence")
            line.End(null);
        else if (tools.Count == 0 && stop == "refusal")
            line.End("refusal");
        else if (tools.Count == 0 && !line.IsSubagent)
            line.Add(ActivityKind.SessionActivity);
    }

    static void User(JsonElement root, Line line)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
        if (!msg.TryGetProperty("content", out var content)) return;
        // resume ecrit par /compact : ni une demande, ni du travail
        if (Bool(root, "isCompactSummary")) return;
        bool meta = Bool(root, "isMeta");

        if (content.ValueKind == JsonValueKind.String)
        {
            string text = content.GetString() ?? "";
            if (text.StartsWith("<task-notification>", StringComparison.Ordinal))
            {
                TaskNotification(text, line);
                return;
            }
            // commandes locales (/model, /clear...) et mode bash du CLI : Claude ne travaille pas
            if (text.StartsWith("<command-name>", StringComparison.Ordinal)
                || text.StartsWith("<command-message>", StringComparison.Ordinal)
                || text.StartsWith("<local-command-", StringComparison.Ordinal)
                || text.StartsWith("<bash-", StringComparison.Ordinal)) return;
            if (IsInterrupt(text)) { line.End("interrupted"); return; }
            if (line.IsSubagent) line.Add(ActivityKind.SubagentActivity, detail: line.Source.AgentDescription);
            else line.Add(meta ? ActivityKind.SessionActivity : ActivityKind.PromptSubmitted);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;

        bool interrupted = false, hasText = false, anyError = false;
        var results = new List<(string? Id, bool Error)>();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object) continue;
            switch (Str(block, "type"))
            {
                case "tool_result":
                    bool error = Bool(block, "is_error");
                    anyError |= error;
                    results.Add((Str(block, "tool_use_id"), error));
                    break;
                case "text":
                    if (IsInterrupt(Str(block, "text") ?? "")) interrupted = true; else hasText = true;
                    break;
                case "image":
                case "document":
                    hasText = true;
                    break;
            }
        }
        if (results.Count == 0 && !hasText && !interrupted) return;

        if (line.IsSubagent) line.Add(ActivityKind.SubagentActivity, detail: line.Source.AgentDescription);
        // l'outil coupe par Echap revient en erreur : c'est l'utilisateur, pas un echec
        foreach (var (id, error) in results)
            line.Add(ActivityKind.ToolFinished, isError: error && !interrupted, detail: id);

        if (interrupted) line.End("interrupted");
        // resultat d'outil qui clot le tour, comme StructuredOutput chez les agents de workflow.
        // Meme regle que Claude Code : pas de fin si un des resultats est en erreur
        else if (results.Count > 0 && !anyError && (Bool(root, "toolEndsTurn") || McpEndsTurn(root))) line.End(null);
        else if (results.Count == 0 && !line.IsSubagent)
            line.Add(meta ? ActivityKind.SessionActivity : ActivityKind.PromptSubmitted);
    }

    /// <summary>
    /// Fin d'une tache de fond. Pour un sous-agent lance en arriere-plan, task-id est son
    /// agentId : c'est souvent le seul signal de fin quand son transcript s'arrete sur un
    /// stop_reason null. Pour un Bash de fond, l'id ne correspond a aucun agent et le modele
    /// l'ignore. Dans tous les cas la session principale se remet au travail.
    /// </summary>
    static void TaskNotification(string text, Line line)
    {
        if (line.IsSubagent)
        {
            line.Add(ActivityKind.SubagentActivity, detail: line.Source.AgentDescription);
            return;
        }
        string? taskId = Tag(text, "task-id");
        string? status = Tag(text, "status");
        if (!string.IsNullOrEmpty(taskId) && status is "completed" or "failed" or "stopped" or "killed" or "cancelled")
            line.Events.Add(new ActivityEvent(line.Time, ActivityKind.SubagentEnded, line.Source.SessionId,
                taskId, line.Cwd, null, status == "failed", status));
        line.Add(ActivityKind.PromptSubmitted, detail: "task-notification");
    }

    static bool IsInterrupt(string text) => text.StartsWith("[Request interrupted", StringComparison.Ordinal);

    // mcpMeta._meta["claude/endTurn"] : l'autre facon dont Claude Code marque un resultat final
    static bool McpEndsTurn(JsonElement root) =>
        root.TryGetProperty("mcpMeta", out var m) && m.ValueKind == JsonValueKind.Object
        && m.TryGetProperty("_meta", out var inner) && inner.ValueKind == JsonValueKind.Object
        && Bool(inner, "claude/endTurn");

    static string? Tag(string text, string name)
    {
        string open = "<" + name + ">", close = "</" + name + ">";
        int start = text.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return null;
        start += open.Length;
        int end = text.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : text[start..end].Trim();
    }

    static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Bool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Accumule les evenements d'une ligne avec les champs communs.</summary>
    sealed class Line(DateTimeOffset time, TranscriptSource source, string? cwd)
    {
        public readonly List<ActivityEvent> Events = new(2);
        public DateTimeOffset Time => time;
        public TranscriptSource Source => source;
        public string? Cwd => cwd;
        public bool IsSubagent => source.AgentId != null;

        public void Add(ActivityKind kind, string? tool = null, bool isError = false, string? detail = null) =>
            Events.Add(new ActivityEvent(time, kind, source.SessionId, source.AgentId, cwd, tool, isError, detail)
            {
                ForegroundToolUseId = source.AgentId != null ? source.ForegroundToolUseId : null,
            });

        /// <summary>Fin de tour : pour un sous-agent, c'est sa fin tout court.</summary>
        public void End(string? reason) =>
            Add(IsSubagent ? ActivityKind.SubagentEnded : ActivityKind.TurnEnded, detail: reason);
    }
}
