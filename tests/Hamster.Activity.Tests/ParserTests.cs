using static Hamster.Activity.Tests.Jsonl;

namespace Hamster.Activity.Tests;

static class ParserTests
{
    static readonly TranscriptSource Main = new(Session, null, null);
    static readonly TranscriptSource Sub = new(Session, "a0123456789abcdef", "Explorer le code");

    static IReadOnlyList<ActivityEvent> P(string line, TranscriptSource? source = null) => TranscriptParser.ParseLine(line, source ?? Main);

    static void Kinds(IReadOnlyList<ActivityEvent> events, params ActivityKind[] expected) =>
        T.Eq(string.Join(",", expected), string.Join(",", events.Select(e => e.Kind)), "types d'evenements");

    public static void Run()
    {
        T.Suite("TranscriptParser");

        T.Case("ligne invalide, vide ou non objet : liste vide sans exception", () =>
        {
            foreach (var bad in new[] { "", "   ", "{", "pas du json", "[1,2]", "42", "null", "{\"type\":\"user\"}", "{\"type\":\"assistant\",\"timestamp\":\"hier\"}", "\u0000\u0001" })
                T.Eq(0, P(bad).Count, "evenements pour <" + bad + ">");
            T.Eq(0, TranscriptParser.ParseLine((string)null!, Main).Count, "ligne null");
            T.Eq(0, TranscriptParser.ParseLine(Prompt(0), null!).Count, "source null");
        });

        T.Case("types sans rapport avec l'activite ignores", () =>
        {
            foreach (var type in new[] { "attachment", "last-prompt", "ai-title", "queue-operation", "mode", "file-history-snapshot" })
                T.Eq(0, P(Other(type, 1)).Count, type);
        });

        T.Case("prompt tape (chaine) => PromptSubmitted avec session, cwd et heure", () =>
        {
            var e = P(Prompt(5)).Single();
            T.Eq(ActivityKind.PromptSubmitted, e.Kind, "type");
            T.Eq(Session, e.SessionId, "session");
            T.Eq(Cwd, e.Cwd, "cwd");
            T.Eq(T0.AddSeconds(5), e.Time, "heure");
            T.Eq<string?>(null, e.AgentId, "agent");
        });

        T.Case("prompt en blocs text ou image => PromptSubmitted", () =>
        {
            Kinds(P(UserBlocks(1, new[] { Text("bonjour") })), ActivityKind.PromptSubmitted);
            Kinds(P(UserBlocks(1, new object[] { new { type = "image", source = new { } }, Text("vois") })), ActivityKind.PromptSubmitted);
        });

        T.Case("message isMeta => SessionActivity, pas un prompt", () =>
        {
            Kinds(P(UserString(1, "contexte injecte", meta: true)), ActivityKind.SessionActivity);
            Kinds(P(UserBlocks(1, new[] { Text("skill") }, meta: true)), ActivityKind.SessionActivity);
        });

        T.Case("commandes locales, mode bash et resume de compact ignores", () =>
        {
            foreach (var s in new[] { "<command-name>/model</command-name>", "<local-command-stdout>ok</local-command-stdout>", "<command-message>x</command-message>", "<bash-input>ls</bash-input>" })
                T.Eq(0, P(UserString(1, s)).Count, s);
            T.Eq(0, P(UserBlocks(1, new[] { Text("resume") }, compact: true)).Count, "compact");
        });

        T.Case("tool_use => ToolStarted avec nom et id", () =>
        {
            var e = P(Assistant(2, "tool_use", ToolUse("Bash", "toolu_1"))).Single();
            T.Eq(ActivityKind.ToolStarted, e.Kind, "type");
            T.Eq("Bash", e.ToolName, "outil");
            T.Eq("toolu_1", e.Detail, "id");
        });

        T.Case("AskUserQuestion et ExitPlanMode => AskedUser", () =>
        {
            Kinds(P(Assistant(2, "tool_use", ToolUse("AskUserQuestion", "q1"))), ActivityKind.AskedUser);
            Kinds(P(Assistant(2, "tool_use", ToolUse("ExitPlanMode", "q2"))), ActivityKind.AskedUser);
        });

        T.Case("tool_result => ToolFinished, erreur comprise", () =>
        {
            var ok = P(Result(3, "toolu_1")).Single();
            T.Eq(ActivityKind.ToolFinished, ok.Kind, "type");
            T.Eq("toolu_1", ok.Detail, "id");
            T.Eq(false, ok.IsError, "erreur");
            T.Eq(true, P(Result(3, "toolu_2", error: true)).Single().IsError, "erreur signalee");
        });

        T.Case("plusieurs tool_result dans une ligne => un ToolFinished chacun", () =>
            Kinds(P(UserBlocks(3, new[] { ToolResult("a"), ToolResult("b", true) })), ActivityKind.ToolFinished, ActivityKind.ToolFinished));

        T.Case("thinking ou texte en cours de tour => SessionActivity", () =>
        {
            Kinds(P(Assistant(2, "tool_use", Thinking())), ActivityKind.SessionActivity);
            Kinds(P(Assistant(2, null, Text("..."))), ActivityKind.SessionActivity);
        });

        T.Case("end_turn => TurnEnded (Detail null)", () =>
        {
            var e = P(Assistant(9, "end_turn", Text("fini"))).Single();
            T.Eq(ActivityKind.TurnEnded, e.Kind, "type");
            T.Eq<string?>(null, e.Detail, "detail");
            Kinds(P(Assistant(9, "end_turn", Thinking())), ActivityKind.TurnEnded);
        });

        T.Case("end_turn avec un tool_use : l'outil l'emporte, pas de fin", () =>
            Kinds(P(Assistant(9, "end_turn", ToolUse("Bash", "t9"))), ActivityKind.ToolStarted));

        T.Case("interruption => TurnEnded interrupted, l'outil coupe n'est pas une erreur", () =>
        {
            var e = P(UserBlocks(4, new[] { ToolResult("t1", true), Text("[Request interrupted by user for tool use]") }));
            Kinds(e, ActivityKind.ToolFinished, ActivityKind.TurnEnded);
            T.Eq(false, e[0].IsError, "erreur masquee");
            T.Eq("interrupted", e[1].Detail, "raison");
            Kinds(P(UserString(4, "[Request interrupted by user]")), ActivityKind.TurnEnded);
        });

        T.Case("system api_error => ApiError ; autres sous-types ignores", () =>
        {
            var e = P(SystemLine(5, "api_error")).Single();
            T.Eq(ActivityKind.ApiError, e.Kind, "type");
            T.Eq(true, e.IsError, "erreur");
            foreach (var st in new[] { "stop_hook_summary", "compact_boundary", "local_command", "informational" })
                T.Eq(0, P(SystemLine(5, st)).Count, st);
        });

        T.Case("erreur d'API definitive => ApiError puis TurnEnded api_error", () =>
        {
            var e = P(ApiErrorMessage(6));
            Kinds(e, ActivityKind.ApiError, ActivityKind.TurnEnded);
            T.Eq("rate_limit", e[0].Detail, "type d'erreur");
            T.Eq("api_error", e[1].Detail, "raison");
        });

        T.Case("message fabrique (<synthetic>) => TurnEnded synthetic", () =>
        {
            var e = P(Synthetic(6)).Single();
            T.Eq(ActivityKind.TurnEnded, e.Kind, "type");
            T.Eq("synthetic", e.Detail, "raison");
        });

        T.Case("sous-agent : chaque ligne porte SubagentActivity et son libelle", () =>
        {
            var e = P(UserString(1, "mission", agentId: Sub.AgentId), Sub);
            Kinds(e, ActivityKind.SubagentActivity);
            T.Eq("Explorer le code", e[0].Detail, "libelle");
            T.Eq(Sub.AgentId, e[0].AgentId, "agent");
            T.Eq(Session, e[0].SessionId, "session parente");
            Kinds(P(AssistantAs(Sub.AgentId, 2, null, Thinking()), Sub), ActivityKind.SubagentActivity);
        });

        T.Case("sous-agent : tool_use et tool_result gardent l'agentId", () =>
        {
            var start = P(AssistantAs(Sub.AgentId, 2, null, ToolUse("Read", "r1")), Sub);
            Kinds(start, ActivityKind.SubagentActivity, ActivityKind.ToolStarted);
            T.Eq(Sub.AgentId, start[1].AgentId, "agent sur ToolStarted");
            var end = P(Result(3, "r1", agentId: Sub.AgentId), Sub);
            Kinds(end, ActivityKind.SubagentActivity, ActivityKind.ToolFinished);
        });

        T.Case("sous-agent : AskUserQuestion reste un outil", () =>
            Kinds(P(AssistantAs(Sub.AgentId, 2, "tool_use", ToolUse("AskUserQuestion", "q")), Sub), ActivityKind.SubagentActivity, ActivityKind.ToolStarted));

        T.Case("sous-agent : end_turn => SubagentEnded", () =>
            Kinds(P(AssistantAs(Sub.AgentId, 9, "end_turn", Text("rapport")), Sub), ActivityKind.SubagentActivity, ActivityKind.SubagentEnded));

        T.Case("sous-agent : resultat final (toolEndsTurn) => SubagentEnded, sauf en erreur", () =>
        {
            Kinds(P(Result(9, "so", agentId: Sub.AgentId, toolEndsTurn: true), Sub),
                ActivityKind.SubagentActivity, ActivityKind.ToolFinished, ActivityKind.SubagentEnded);
            Kinds(P(Result(9, "so", error: true, agentId: Sub.AgentId, toolEndsTurn: true), Sub),
                ActivityKind.SubagentActivity, ActivityKind.ToolFinished);
        });

        T.Case("sous-agent : interruption => SubagentEnded", () =>
            Kinds(P(UserBlocks(9, new[] { Text("[Request interrupted by user]") }, agentId: Sub.AgentId), Sub),
                ActivityKind.SubagentActivity, ActivityKind.SubagentEnded));

        T.Case("task-notification d'un agent de fond => SubagentEnded(agentId) puis PromptSubmitted", () =>
        {
            var e = P(TaskNotification(20, "a0123456789abcdef", "completed"));
            Kinds(e, ActivityKind.SubagentEnded, ActivityKind.PromptSubmitted);
            T.Eq("a0123456789abcdef", e[0].AgentId, "agent vise");
            T.Eq("completed", e[0].Detail, "statut");
            T.Eq<string?>(null, e[1].AgentId, "la reprise est sur le fil principal");
            // notification sans statut (moniteur) : juste une reprise
            Kinds(P(UserString(20, "<task-notification><task-id>b1</task-id><event>x</event></task-notification>")), ActivityKind.PromptSubmitted);
        });

        T.Case("ligne en octets UTF-8 : meme resultat, validite signalee", () =>
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(Assistant(2, "tool_use", ToolUse("Edit", "e1")));
            var e = TranscriptParser.ParseLine(bytes, Main, out bool valid);
            T.Eq(true, valid, "valide");
            T.Eq("Edit", e.Single().ToolName, "outil");
            TranscriptParser.ParseLine(System.Text.Encoding.UTF8.GetBytes("{casse"), Main, out valid);
            T.Eq(false, valid, "invalide");
        });

        T.Case("champs inattendus ou mal types : ignores sans exception", () =>
        {
            T.Eq(0, P("{\"type\":\"assistant\",\"timestamp\":\"2026-09-26T12:00:00Z\",\"message\":\"texte\"}").Count, "message chaine");
            T.Eq(0, P("{\"type\":\"user\",\"timestamp\":\"2026-09-26T12:00:00Z\",\"message\":{\"content\":42}}").Count, "content nombre");
            Kinds(P("{\"type\":\"assistant\",\"timestamp\":\"2026-09-26T12:00:00Z\",\"message\":{\"content\":[1,{\"type\":\"tool_use\"},{\"type\":\"tool_use\",\"name\":\"Bash\"}]}}"), ActivityKind.ToolStarted);
        });
    }
}
