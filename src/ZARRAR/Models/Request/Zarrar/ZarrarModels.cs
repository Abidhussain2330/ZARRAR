namespace ZARRAR.Models.Request.Zarrar;

/// <summary>
/// Live match state of a ZARRAR 1v1 showdown game.
/// </summary>
/// <remarks>
/// Persisted as JSON in the <c>Configs</c> table under <c>ZARRAR_DUEL_{gameId}</c>, so the
/// showdown needs no database migration. One ZARRAR game is one match.
/// </remarks>
public class DuelStateModel
{
    /// <summary>Game ID</summary>
    public int GameId { get; set; }

    /// <summary>Whether this game runs as a 1v1 showdown</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the match is paused (flag submission is blocked for both players)</summary>
    public bool Paused { get; set; }

    /// <summary>Why the match is paused, shown on the stage and to both players</summary>
    public string? PauseReason { get; set; }

    /// <summary>When the current pause started</summary>
    public DateTimeOffset? PausedAtUtc { get; set; }

    /// <summary>Whether the admin ended the match</summary>
    public bool Ended { get; set; }

    /// <summary>Stage layout: split, p1, p2, board, or f1/f2 (one screen full size)</summary>
    public string Layout { get; set; } = "split";

    /// <summary>The challenge most recently released by the admin</summary>
    public int? CurrentChallengeId { get; set; }

    /// <summary>Challenges the admin withdrew with no winner (kept out of "release next")</summary>
    public List<int> Withdrawn { get; set; } = [];

    /// <summary>When the admin pressed "Start match" (null until then)</summary>
    public DateTimeOffset? StartedUtc { get; set; }

    /// <summary>
    /// Match clock: active seconds banked while it was running. The clock runs from the first
    /// release and stops while paused or ended, so per-challenge times exclude pauses.
    /// </summary>
    public double ClockSeconds { get; set; }

    /// <summary>When the match clock last started running; null while it is stopped</summary>
    public DateTimeOffset? ClockSinceUtc { get; set; }

    /// <summary>Match-clock reading at the moment each challenge was released</summary>
    public Dictionary<int, double> ReleasedAtClock { get; set; } = new();

    /// <summary>
    /// Tag-team: who is on the keyboard for each team (participation ID → user ID), as chosen by
    /// the monitor or admin. A team with no entry is played by whichever member is streaming.
    /// Only the player on the keyboard may stream and submit flags.
    /// </summary>
    public Dictionary<int, Guid> Operators { get; set; } = new();

    /// <summary>Last change</summary>
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Match-clock reading at <paramref name="now" />.</summary>
    internal double ClockAt(DateTimeOffset now) =>
        ClockSeconds + (ClockSinceUtc is { } since ? Math.Max(0, (now - since).TotalSeconds) : 0);

    /// <summary>Banks the running time and stops the match clock.</summary>
    internal void StopClock(DateTimeOffset now)
    {
        ClockSeconds = ClockAt(now);
        ClockSinceUtc = null;
    }

    /// <summary>Starts the match clock if it is stopped and the match is in play.</summary>
    internal void RunClock(DateTimeOffset now)
    {
        if (ClockSinceUtc is null && !Paused && !Ended)
            ClockSinceUtc = now;
    }

    /// <summary>Seconds a challenge has been live (pauses excluded), or null if never released.</summary>
    internal int? LiveSecondsOf(int challengeId, DateTimeOffset now) =>
        ReleasedAtClock.TryGetValue(challengeId, out var at) ? (int)Math.Max(0, ClockAt(now) - at) : null;

    internal DuelStateModel Clone()
    {
        var copy = (DuelStateModel)MemberwiseClone();
        copy.Withdrawn = [.. Withdrawn];
        copy.ReleasedAtClock = new Dictionary<int, double>(ReleasedAtClock);
        copy.Operators = new Dictionary<int, Guid>(Operators);
        return copy;
    }
}

/// <summary>
/// One moment on the stage: a flag, a release, a lost feed, a pause.
/// </summary>
public class StageEventModel
{
    /// <summary>
    /// accepted | denied | released | captured | withdrawn | feedLost | feedRestored | paused |
    /// resumed | ended | countdown | tagged
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public int GameId { get; set; }

    public int? ParticipationId { get; set; }

    public string? TeamName { get; set; }

    public string? UserName { get; set; }

    public int? ChallengeId { get; set; }

    public string? ChallengeTitle { get; set; }

    public int? Points { get; set; }

    public string? Message { get; set; }

    /// <summary>Seconds the challenge was live before this flag (pauses excluded)</summary>
    public int? Seconds { get; set; }

    public DateTimeOffset TimeUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A player in the showdown (a solo team's participation).
/// </summary>
public class DuelPlayerModel
{
    /// <summary>p1 (left, blue) or p2 (right, green)</summary>
    public string Slot { get; set; } = "p1";

    public int ParticipationId { get; set; }

    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public string[] Members { get; set; } = [];

    public int Score { get; set; }

    public int Rank { get; set; }

    public int SolvedCount { get; set; }

    /// <summary>Flags submitted in this match (right and wrong), for the stage's hit rate</summary>
    public int Attempts { get; set; }

    /// <summary>Whether the player's screen feed is currently live</summary>
    public bool Live { get; set; }

    /// <summary>Everyone on this team who joined the match, for the tag-in picker</summary>
    public DuelMemberModel[] Roster { get; set; } = [];

    /// <summary>The member the monitor tagged in (null: whoever streams plays)</summary>
    public Guid? OperatorId { get; set; }

    /// <summary>Who is streaming right now (null when the feed is down)</summary>
    public Guid? LiveUserId { get; set; }

    /// <summary>Name of the player on the keyboard: the tagged-in member, else whoever streams</summary>
    public string? OperatorName { get; set; }
}

/// <summary>
/// One member of a showdown team.
/// </summary>
public class DuelMemberModel
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string? Avatar { get; set; }
}

/// <summary>
/// A challenge as the showdown sees it.
/// </summary>
public class DuelChallengeModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Score { get; set; }

    /// <summary>queued | live | captured | withdrawn</summary>
    public string Status { get; set; } = "queued";

    public int? CapturedBy { get; set; }

    public string? CapturedByName { get; set; }

    public DateTimeOffset? CapturedAtUtc { get; set; }
}

/// <summary>
/// Everything the stage needs in one call.
/// </summary>
public class DuelSnapshotModel
{
    public int GameId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    public DuelStateModel Duel { get; set; } = new();

    public DuelPlayerModel[] Players { get; set; } = [];

    public DuelChallengeModel[] Challenges { get; set; } = [];

    /// <summary>Monitor stages currently open on this match (projector screens)</summary>
    public int StageScreens { get; set; }

    /// <summary>Pre-flight checklist; only filled in for admins</summary>
    public DuelReadinessModel[]? Readiness { get; set; }
}

/// <summary>
/// One item on the admin's pre-flight checklist.
/// </summary>
public class DuelReadinessModel
{
    /// <summary>Stable identifier, e.g. "players"</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>What is being checked, in plain words</summary>
    public string Label { get; set; } = string.Empty;

    public bool Ok { get; set; }

    /// <summary>Critical items block "Start match" and "Release" until fixed or overridden</summary>
    public bool Critical { get; set; }

    /// <summary>What is wrong and how to fix it (or what is fine)</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// A recorded screen feed.
/// </summary>
public class DuelRecordingModel
{
    public string Name { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }

    /// <summary>The player's participation, parsed from the file name when possible</summary>
    public int? ParticipationId { get; set; }
}

/// <summary>
/// A game the monitor can open.
/// </summary>
public class DuelGameModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    public bool Duel { get; set; }

    public bool Paused { get; set; }

    public bool Ended { get; set; }

    public int Players { get; set; }
}

/// <summary>
/// What a player needs to know about the showdown they are in.
/// </summary>
public class DuelPlayerStatusModel
{
    public bool Duel { get; set; }

    public bool Paused { get; set; }

    public string? PauseReason { get; set; }

    public bool Ended { get; set; }

    /// <summary>Whether the server currently receives this player's screen</summary>
    public bool Live { get; set; }

    /// <summary>Another team member is tagged in; this player may not stream or submit flags</summary>
    public bool Benched { get; set; }

    /// <summary>Who is on the keyboard for the caller's team, when known</summary>
    public string? OperatorName { get; set; }

    /// <summary>The caller's team</summary>
    public string? TeamName { get; set; }
}

/// <summary>
/// Admin request: turn showdown mode on or off.
/// </summary>
public class DuelEnableModel
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Admin request: change the stage layout.
/// </summary>
public class DuelLayoutModel
{
    public string Layout { get; set; } = "split";
}

/// <summary>
/// Monitor/admin request: put a team member on the keyboard (tag-in). A null user clears the
/// choice, so whichever member streams plays.
/// </summary>
public class DuelOperatorModel
{
    public int ParticipationId { get; set; }

    public Guid? UserId { get; set; }
}

/// <summary>
/// Admin request: announce a challenge release on the stage, or mark one withdrawn.
/// </summary>
public class DuelReleaseModel
{
    public int ChallengeId { get; set; }
}
