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
    string? Detail)
{
    /// <summary>
    /// Sous-agent au premier plan seulement (TranscriptSource.ForegroundToolUseId) : id du
    /// tool_use Agent du fil principal qui l'attend. Son tool_result sur le fil principal
    /// termine l'agent.
    /// </summary>
    public string? ForegroundToolUseId { get; init; }
}

/// <summary>D'ou vient une ligne de transcript : session, et sous-agent le cas echeant.</summary>
public sealed record TranscriptSource(string SessionId, string? AgentId, string? AgentDescription)
{
    /// <summary>
    /// toolUseId du meta.json, pour un sous-agent au premier plan : requestShape present et
    /// different de "background". Null sinon, et null si requestShape est absent : sur les
    /// transcripts mesures, ces agents-la recoivent presque tous leur tool_result des le
    /// lancement, comme ceux de fond.
    /// </summary>
    public string? ForegroundToolUseId { get; init; }
}

public sealed class ActivityOptions
{
    /// <summary>
    /// Session Working sans le moindre evenement depuis ce delai : consideree inactive. Vaut
    /// aussi pour un sous-agent muet qui n'a aucun outil en suspens.
    /// </summary>
    public TimeSpan WorkingTimeout { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>
    /// Question ou plan a valider (AskedUser) au-dela de ce delai : attente abandonnee. La
    /// question est dans le transcript, la reponse aussi : le delai peut etre long.
    /// </summary>
    public TimeSpan WaitingTimeout { get; set; } = TimeSpan.FromHours(1);
    /// <summary>
    /// Demande du hook (NeedsUser) au-dela de ce delai : attente abandonnee. Rien n'est ecrit
    /// entre une autorisation accordee et le tool_result, ni quand la session est tuee
    /// pendant l'attente : le delai est court.
    /// </summary>
    public TimeSpan NeedsUserTimeout { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>
    /// Une session en attente ne prend la place principale que si son attente a moins de ce
    /// delai, ou si elle a commence apres la derniere activite de la principale.
    /// </summary>
    public TimeSpan FreshWaitWindow { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary>
    /// Une session ne prend la place principale que si elle doit rester active au moins ce
    /// delai sans nouvel evenement : successeur d'une attente perimee, ou attente qui redevient
    /// la plus parlante. Sinon elle la rendrait aussitot, et la principale ferait un aller-retour.
    /// </summary>
    public TimeSpan SuccessorMargin { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>
    /// Sous-agent muet depuis ce delai alors qu'un de ses outils est en suspens : il disparait.
    /// Sans outil en suspens, WorkingTimeout suffit (Claude Code ferme en plein travail).
    /// </summary>
    public TimeSpan SubagentTimeout { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>Session oubliee ce delai apres son dernier evenement.</summary>
    public TimeSpan ForgetAfter { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan CelebrateDuration { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan ErrorDuration { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Un tour sans outil ne se fete que s'il a dure au moins ce temps.</summary>
    public TimeSpan MinTurnForCelebration { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Un mini hamster : sous-agent ("subagent") ou autre session active ("session").</summary>
public sealed record MiniInfo(string Id, string Kind, string Label, string ColorKey, PetState State, DateTimeOffset Since)
{
    /// <summary>
    /// Session seulement : debut de son attente en cours, meme quand la fete (Celebrating)
    /// passe devant ; null sans attente. Une attente garde ce debut tant qu'elle dure : la
    /// sonnerie s'en sert pour ne pas annoncer deux fois la meme.
    /// </summary>
    public DateTimeOffset? WaitSince { get; init; }
}

public sealed record ActivitySnapshot(
    PetState State,
    string? MainSessionId,
    string? MainLabel,
    string? Tool,
    bool AnySessionActive,
    IReadOnlyList<MiniInfo> Minis,
    int MinisOverflow)
{
    /// <summary>Debut de l'attente en cours de la principale, comme MiniInfo.WaitSince ; null sans attente.</summary>
    public DateTimeOffset? MainWaitSince { get; init; }
}
