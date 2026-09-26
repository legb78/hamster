using System.Text.Json;

namespace Hamster.Activity;

/// <summary>
/// Disposition des transcripts sous ~/.claude/projects, observee (format non documente) :
///   slug\sessionId.jsonl                                   session principale
///   slug\sessionId\subagents\agent-id.jsonl                sous-agent
///   slug\sessionId\subagents\workflows\wf\agent-id.jsonl   agent de workflow
/// avec un agent-id.meta.json a cote de chaque agent (agentType, description, requestShape,
/// toolUseId...). Le journal.jsonl des workflows n'est pas un agent.
/// </summary>
public static class TranscriptPaths
{
    public static string DefaultProjectsRoot
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("HAMSTER_PROJECTS_ROOT");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        }
    }

    /// <summary>Null si le fichier n'est pas un transcript de session ou de sous-agent.</summary>
    public static TranscriptSource? Classify(string fullPath, string projectsRoot)
    {
        try
        {
            string full = Path.GetFullPath(fullPath);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectsRoot)) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!full.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return null;

            var parts = full[root.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string stem = Path.GetFileNameWithoutExtension(parts[^1]);

            if (parts.Length == 2)
                // seuls des GUID ont ete observes a ce niveau : on ne prend pas un .jsonl quelconque pour une session
                return Guid.TryParse(stem, out _) ? new TranscriptSource(stem, null, null) : null;

            if (parts.Length >= 4 && Guid.TryParse(parts[1], out _)
                && parts[2].Equals("subagents", StringComparison.OrdinalIgnoreCase)
                && stem.StartsWith("agent-", StringComparison.Ordinal) && stem.Length > "agent-".Length)
            {
                string agentId = stem["agent-".Length..];
                var meta = ReadAgentMeta(Path.Combine(Path.GetDirectoryName(full)!, stem + ".meta.json"));
                return new TranscriptSource(parts[1], agentId, meta.Label) { ForegroundToolUseId = meta.ForegroundToolUseId };
            }
            return null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Meta absent ou illisible : (null, null).
    /// Label : description, sinon agentType. ForegroundToolUseId : toolUseId, si requestShape
    /// est present et different de "background" (valeurs observees : "background",
    /// "foreground"). requestShape absent : null, car sur les transcripts mesures 9 de ces
    /// agents sur 11 ont recu leur tool_result des le lancement, comme un agent de fond.
    /// </summary>
    internal static (string? Label, string? ForegroundToolUseId) ReadAgentMeta(string metaPath)
    {
        try
        {
            if (!File.Exists(metaPath)) return (null, null);
            using var fs = new FileStream(metaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // un meta.json fait quelques centaines d'octets : au-dela, ce n'est pas le fichier attendu
            if (fs.Length > 64 * 1024) return (null, null);
            using var doc = JsonDocument.Parse(fs);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return (null, null);
            string? label = null;
            foreach (var name in new[] { "description", "agentType" })
                if (Str(r, name) is { } v && !string.IsNullOrWhiteSpace(v))
                {
                    label = v.Trim();
                    break;
                }
            string? shape = Str(r, "requestShape");
            string? toolUseId = shape != null && shape != "background" && Str(r, "toolUseId") is { Length: > 0 } id ? id : null;
            return (label, toolUseId);
        }
        catch (Exception) { return (null, null); }
    }

    static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
