namespace Hamster.Activity;

/// <summary>Etat affiche. Priorite croissante : Celebrating > WaitingUser > Error > Working > Idle.</summary>
public enum PetState { Idle, Working, Error, WaitingUser, Celebrating }

public enum ActivityKind
{
    PromptSubmitted,
    ToolStarted,
    ToolFinished,
    AskedUser,
    TurnEnded,
    ApiError,
    NeedsUser,
    SubagentActivity,
    SubagentEnded,
    SessionActivity,
}

/// <summary>
/// Un fait lu dans un transcript ou un hook.
/// AgentId non null : l'evenement vient du fil d'un sous-agent (ToolStarted et ToolFinished compris).
/// Detail selon le type :
/// ToolStarted, AskedUser, ToolFinished : id du tool_use, pour apparier debut et fin.
/// SubagentActivity : libelle du sous-agent (description du meta.json, sinon agentType).
/// TurnEnded, SubagentEnded : null pour une fin normale, sinon la raison
/// ("interrupted", "api_error", "synthetic", "refusal", ou le statut d'une task-notification).
/// NeedsUser : notification_type. ApiError : type d'erreur quand il est connu.
/// </summary>
public sealed record ActivityEvent(
    DateTimeOffset Time,
    ActivityKind Kind,
    string SessionId,
    string? AgentId,
    string? Cwd,
    string? ToolName,
    bool IsError,
    string? Detail);

/// <summary>D'ou vient une ligne de transcript : session, et sous-agent le cas echeant.</summary>
public sealed record TranscriptSource(string SessionId, string? AgentId, string? AgentDescription);

public sealed class ActivityOptions
{
    /// <summary>Session Working sans le moindre evenement depuis ce delai : consideree inactive.</summary>
    public TimeSpan WorkingTimeout { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>Attente de l'utilisateur au-dela de ce delai : abandonnee.</summary>
    public TimeSpan WaitingTimeout { get; set; } = TimeSpan.FromHours(1);
    /// <summary>Sous-agent muet depuis ce delai : il disparait.</summary>
    public TimeSpan SubagentTimeout { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>Session oubliee ce delai apres son dernier evenement.</summary>
    public TimeSpan ForgetAfter { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan CelebrateDuration { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan ErrorDuration { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Un tour sans outil ne se fete que s'il a dure au moins ce temps.</summary>
    public TimeSpan MinTurnForCelebration { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Un mini hamster : sous-agent ("subagent") ou autre session active ("session").</summary>
public sealed record MiniInfo(string Id, string Kind, string Label, string ColorKey, PetState State, DateTimeOffset Since);

public sealed record ActivitySnapshot(
    PetState State,
    string? MainSessionId,
    string? MainLabel,
    string? Tool,
    bool AnySessionActive,
    IReadOnlyList<MiniInfo> Minis,
    int MinisOverflow);
