using System.Text.Json.Serialization;
using Microsoft.Extensions.Localization;

namespace ZARRAR.Utils;

/// <summary>
/// User role enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Role>))]
public enum Role : byte
{
    /// <summary>
    /// Banned user role
    /// </summary>
    Banned = 0,

    /// <summary>
    /// Regular user role
    /// </summary>
    User = 1,

    /// <summary>
    /// Monitor role, can query submission logs
    /// </summary>
    Monitor = 2,

    /// <summary>
    /// Admin role, can view system logs
    /// </summary>
    Admin = 3
}

/// <summary>
/// Login response status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegisterStatus>))]
public enum RegisterStatus : byte
{
    /// <summary>
    /// Registered successfully and logged in
    /// </summary>
    LoggedIn = 0,

    /// <summary>
    /// Waiting for admin confirmation
    /// </summary>
    AdminConfirmationRequired = 1,

    /// <summary>
    /// Waiting for email confirmation
    /// </summary>
    EmailConfirmationRequired = 2
}

/// <summary>
/// Task execution status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskStatus>))]
public enum TaskStatus : sbyte
{
    /// <summary>
    /// System is unhealthy
    /// </summary>
    Unhealthy = -3,

    /// <summary>
    /// System is in a degraded state
    /// </summary>
    Degraded = -2,

    /// <summary>
    /// Task is in progress
    /// </summary>
    Pending = -1,

    /// <summary>
    /// Task completed successfully
    /// </summary>
    Success = 0,

    /// <summary>
    /// Task execution failed
    /// </summary>
    Failed = 1,

    /// <summary>
    /// Task encountered a duplicate error
    /// </summary>
    Duplicate = 2,

    /// <summary>
    /// Task processing was denied
    /// </summary>
    Denied = 3,

    /// <summary>
    /// Task request not found
    /// </summary>
    NotFound = 4,

    /// <summary>
    /// Task thread is about to exit
    /// </summary>
    Exit = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<FileType>))]
public enum FileType : byte
{
    /// <summary>
    /// No attachment
    /// Normally, Attachment will not be this value, if there is no attachment, Attachment will be null
    /// </summary>
    None = 0,

    /// <summary>
    /// Local file
    /// </summary>
    Local = 1,

    /// <summary>
    /// Remote file
    /// </summary>
    Remote = 2
}

/// <summary>
/// Container status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContainerStatus>))]
public enum ContainerStatus : byte
{
    /// <summary>
    /// Starting
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Running
    /// </summary>
    Running = 1,

    /// <summary>
    /// Destroyed
    /// </summary>
    Destroyed = 2
}

/// <summary>
/// Game announcement type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NoticeType>))]
public enum NoticeType : byte
{
    /// <summary>
    /// Regular announcement
    /// </summary>
    Normal = 0,

    /// <summary>
    /// First blood announcement
    /// </summary>
    FirstBlood = 1,

    /// <summary>
    /// Second blood announcement
    /// </summary>
    SecondBlood = 2,

    /// <summary>
    /// Third blood announcement
    /// </summary>
    ThirdBlood = 3,

    /// <summary>
    /// New hint released
    /// </summary>
    NewHint = 4,

    /// <summary>
    /// New challenge released
    /// </summary>
    NewChallenge = 5
}

/// <summary>
/// Game event type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EventType>))]
public enum EventType : byte
{
    /// <summary>
    /// Regular information
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Container start information
    /// </summary>
    ContainerStart = 1,

    /// <summary>
    /// Container destroy information
    /// </summary>
    ContainerDestroy = 2,

    /// <summary>
    /// Flag submission information
    /// </summary>
    FlagSubmit = 3,

    /// <summary>
    /// Cheating information
    /// </summary>
    CheatDetected = 4
}

/// <summary>
/// Submission type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubmissionType>))]
public enum SubmissionType : byte
{
    /// <summary>
    /// Not solved
    /// </summary>
    Unaccepted = 0,

    /// <summary>
    /// First blood
    /// </summary>
    FirstBlood = 1,

    /// <summary>
    /// Second blood
    /// </summary>
    SecondBlood = 2,

    /// <summary>
    /// Third blood
    /// </summary>
    ThirdBlood = 3,

    /// <summary>
    /// Solved
    /// </summary>
    Normal = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ParticipationStatus>))]
public enum ParticipationStatus : byte
{
    /// <summary>
    /// Registered
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Accepted
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Rejected
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// Suspended
    /// </summary>
    Suspended = 3,

    /// <summary>
    /// Not submitted
    /// </summary>
    Unsubmitted = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ChallengeType>))]
public enum ChallengeType : byte
{
    /// <summary>
    /// Static challenge
    /// All teams use the same attachment and flag
    /// </summary>
    StaticAttachment = 0b00,

    /// <summary>
    /// Static container challenge
    /// All teams use the same docker and flag
    /// </summary>
    StaticContainer = 0b01,

    /// <summary>
    /// Dynamic attachment challenge
    /// Randomly distribute attachments, implement flag specificity with attachments
    /// </summary>
    DynamicAttachment = 0b10,

    /// <summary>
    /// Dynamic container challenge
    /// Randomly distribute containers, dynamic flag passed in via environment variables
    /// </summary>
    DynamicContainer = 0b11
}

public static class ChallengeTypeExtensions
{
    extension(ChallengeType type)
    {
        /// <summary>
        /// Is it a static challenge
        /// </summary>
        public bool IsStatic() => ((byte)type & 0b10) == 0;

        /// <summary>
        /// Is it a dynamic challenge
        /// </summary>
        public bool IsDynamic() => ((byte)type & 0b10) != 0;

        /// <summary>
        /// Is it an attachment challenge
        /// </summary>
        public bool IsAttachment() => ((byte)type & 0b01) == 0;

        /// <summary>
        /// Is it a container challenge
        /// </summary>
        public bool IsContainer() => ((byte)type & 0b01) != 0;
    }
}

/// <summary>
/// Challenge category
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChallengeCategory>))]
public enum ChallengeCategory : byte
{
    Misc = 0,
    Crypto = 1,
    Pwn = 2,
    Web = 3,
    Reverse = 4,
    Blockchain = 5,
    Forensics = 6,
    Hardware = 7,
    Mobile = 8,
    PPC = 9,

    // ReSharper disable once InconsistentNaming
    AI = 10,
    Pentest = 11,

    // ReSharper disable once InconsistentNaming
    OSINT = 12
}

/// <summary>
/// Challenge difficulty
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Difficulty>))]
public enum Difficulty : byte
{
    Baby = 0,
    Trivial = 1,
    Easy = 2,
    Normal = 3,
    Medium = 4,
    Hard = 5,
    Expert = 6,
    Insane = 7
}

/// <summary>
/// Container network mode
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NetworkMode>))]
public enum NetworkMode : byte
{
    /// <summary>
    /// Open network
    /// </summary>
    /// <remarks>
    /// Allows the container to access external networks, including the internet.
    /// Suitable for challenges that require external connectivity.
    /// </remarks>
    Open = 0,

    /// <summary>
    /// Isolated network
    /// </summary>
    /// <remarks>
    /// Restricts the container from accessing any external networks.
    /// The container can only communicate within container internal network.
    /// </remarks>
    Isolated = 32,

    /// <summary>
    /// Custom network
    /// </summary>
    /// <remarks>
    /// <para>
    ///  For kubernetes provider<br/>
    ///  this mode will add a label to the pod: <c>zarrar.arzens.io/NetworkMode: custom</c>,
    ///  alongside the compatibility label written for older workloads
    ///  for backwards compatibility (see <c>ContainerLabels</c>).
    /// </para>
    /// <para>
    ///  For Docker provider<br/>
    ///  this mode will create the container in a user-defined docker network specified in the config.
    /// </para>
    /// </remarks>
    Custom = 255,
}

/// <summary>
/// Game participant permission
/// </summary>
[Flags]
[JsonConverter(typeof(JsonNumberEnumConverter<GamePermission>))]
public enum GamePermission
{
    /// <summary>
    /// Join the game
    /// </summary>
    /// <remarks>
    /// Division-level permission only. Controls whether users can join the game through this division.
    /// Without this permission, the division cannot accept new participants.
    /// </remarks>
    JoinGame = 1 << 0,

    /// <summary>
    /// Can be ranked on the overall scoreboard
    /// </summary>
    /// <remarks>
    /// Division-level permission only. Determines if teams in this division appear on the overall scoreboard rankings.
    /// Teams without this permission will only have division-specific rankings.
    /// Use case: Unofficial participants or guest teams that should not compete for overall prizes.
    /// </remarks>
    RankOverall = 1 << 1,

    /// <summary>
    /// Require review before acceptance
    /// </summary>
    /// <remarks>
    /// Division-level permission only. When enabled, participations require manual admin approval (Pending status).
    /// When disabled, participations are automatically accepted without review (Accepted status).
    /// Overrides game-level AcceptWithoutReview setting for this specific division.
    /// Use case: Require review for external registrations while auto-accepting internal teams.
    /// Note: Permission name is positive (require review) so that All permission defaults to the secure behavior.
    /// </remarks>
    RequireReview = 1 << 2,

    /// <summary>
    /// Can view challenge
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Controls access to challenge details, descriptions, and attachments.
    /// Without this permission, challenges will be hidden from the team.
    /// </remarks>
    ViewChallenge = 1 << 8,

    /// <summary>
    /// Can submit flags
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Allows teams to submit flag answers for challenges.
    /// Can be combined with ViewChallenge but without GetScore for practice/demo scenarios.
    /// </remarks>
    SubmitFlags = 1 << 9,

    /// <summary>
    /// Can be awarded points
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Determines if successful flag submissions award points to the team.
    /// Teams without this permission can submit flags but won't receive any score.
    /// Use case: Observer teams or late joiners who participate without competing.
    /// </remarks>
    GetScore = 1 << 10,

    /// <summary>
    /// Can earn blood bonuses
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Allows teams to receive first/second/third blood bonus points.
    /// Requires GetScore permission to be effective. Blood bonuses are typically 30%, 20%, and 10% extra points.
    /// Use case: Restrict blood bonuses to specific divisions while allowing others to score normally.
    /// </remarks>
    GetBlood = 1 << 11,

    /// <summary>
    /// Affects dynamic scoring calculation
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Controls whether this team's submissions count toward challenge accept count,
    /// which influences dynamic score calculation for all teams.
    /// Independent of GetScore - teams can score without affecting others' challenge values.
    /// Use case: Allow external participants to score without affecting internal competition's dynamic scoring.
    /// </remarks>
    AffectDynamicScore = 1 << 12,

    /// <summary>
    /// All permissions, including future permissions
    /// </summary>
    /// <remarks>
    /// Special value representing all current and future permissions.
    /// Use int.MaxValue to ensure compatibility with newly added permission flags.
    /// </remarks>
    All = int.MaxValue
}

/// <summary>
/// API token scope
/// </summary>
/// <remarks>
/// An API token is a non-interactive credential and is never a user, so it can never inherit a
/// <see cref="Role" />. Before this existed a token was accepted by the privilege filter ahead of
/// the role check, which made every token admin-equivalent (finding F-012). A token now carries an
/// explicit set of capabilities, and an endpoint that opts into token authentication must name the
/// capability it needs.
/// Each value below maps to endpoints that exist in this codebase; there is deliberately no scope
/// for API token management itself, so <c>ApiTokenController</c> stays interactive-admin only and a
/// token can never mint, re-scope or restore another token.
/// </remarks>
[Flags]
[JsonConverter(typeof(JsonNumberEnumConverter<TokenScope>))]
public enum TokenScope
{
    /// <summary>
    /// No capability
    /// </summary>
    /// <remarks>
    /// The default for a freshly constructed token and the value an endpoint carries when it does
    /// not accept token authentication at all. A token holding only this scope authenticates
    /// nothing.
    /// </remarks>
    None = 0,

    /// <summary>
    /// Read-only observation of a running competition
    /// </summary>
    /// <remarks>
    /// Covers the real-time monitor stream (<c>MonitorHub</c>, <c>/hub/monitor</c>) and the
    /// monitor-level game endpoints: <c>GET api/Game/{id}/Events</c>,
    /// <c>GET api/Game/{id}/Submissions</c>, <c>GET api/Game/{id}/CheatInfo</c>,
    /// <c>GET api/Game/{id}/ScoreboardSheet</c>, <c>GET api/Game/{id}/SubmissionSheet</c>,
    /// <c>GET api/Game/Games/{id}/Captures</c> and the <c>api/Game/Captures/...</c> traffic routes.
    /// This is the scope a scoreboard mirror or an external SIEM feed needs.
    /// </remarks>
    GameMonitor = 1 << 0,

    /// <summary>
    /// Authoring and administration of games and their content
    /// </summary>
    /// <remarks>
    /// Covers <c>EditController</c> in full (<c>api/Edit/Posts...</c>, <c>api/Edit/Games...</c>
    /// including challenges, flags, notices, divisions, attachments, <c>Scoreboard/Flush</c>,
    /// <c>Games/{id}/Export</c> and <c>Games/Import</c>), together with
    /// <c>GET api/Game/{id}/Participations</c>, <c>PUT api/Admin/Participation/{id}</c> and
    /// <c>GET api/Admin/Writeups/{id}</c>.
    /// </remarks>
    GameManage = 1 << 1,

    /// <summary>
    /// Administration of user accounts and teams
    /// </summary>
    /// <remarks>
    /// Covers the account and team routes of <c>AdminController</c>: <c>api/Admin/Users</c>,
    /// <c>api/Admin/Users/Search</c>, <c>api/Admin/Users/{userid}</c>,
    /// <c>api/Admin/Users/{userid}/Password</c>, <c>api/Admin/Teams</c>,
    /// <c>api/Admin/Teams/Search</c> and <c>api/Admin/Teams/{id}</c>.
    /// </remarks>
    UserManage = 1 << 2,

    /// <summary>
    /// Platform configuration, diagnostics and stored assets
    /// </summary>
    /// <remarks>
    /// Covers <c>api/Admin/Config</c> and <c>api/Admin/Config/Logo</c>, <c>api/Admin/Logs</c>,
    /// <c>api/Admin/Instances</c> and <c>api/Admin/Instances/{id}</c>, <c>api/Admin/Files</c>,
    /// and the administrative asset routes <c>POST api/Assets</c> and
    /// <c>DELETE api/Assets/{hash}</c>.
    /// </remarks>
    SystemManage = 1 << 3,

    /// <summary>
    /// All scopes, including future scopes
    /// </summary>
    /// <remarks>
    /// Special value representing all current and future capabilities, mirroring
    /// <see cref="GamePermission.All" />. This is the value the
    /// <c>AddApiTokenScopeAndExpiry</c> migration assigns to tokens that predate scoping, so that
    /// they keep behaving exactly as they did before the upgrade until an operator re-scopes them.
    /// </remarks>
    All = int.MaxValue
}

public static class TokenScopeExtensions
{
    /// <summary>
    /// Every scope bit that is currently defined
    /// </summary>
    public const TokenScope Defined =
        TokenScope.GameMonitor | TokenScope.GameManage | TokenScope.UserManage | TokenScope.SystemManage;

    extension(TokenScope scope)
    {
        /// <summary>
        /// Whether the scope is usable as a grant: non-empty and free of undefined bits
        /// </summary>
        /// <remarks>
        /// <see cref="TokenScope.All" /> is accepted explicitly because it intentionally sets bits
        /// that are not defined yet.
        /// </remarks>
        public bool IsValidGrant() =>
            scope is TokenScope.All ||
            (scope is not TokenScope.None &&
             (scope & ~TokenScopeExtensions.Defined) is TokenScope.None);

        /// <summary>
        /// Whether this grant satisfies the capability an endpoint demands
        /// </summary>
        /// <param name="required">The scope the endpoint requires</param>
        /// <remarks>
        /// A required scope of <see cref="TokenScope.None" /> means the endpoint does not accept
        /// token authentication, so it is never satisfied.
        /// </remarks>
        public bool Covers(TokenScope required) =>
            required is not TokenScope.None && (scope & required) == required;
    }
}

/// <summary>
/// Judgement result
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnswerResult>))]
public enum AnswerResult : sbyte
{
    /// <summary>
    /// Flag submitted successfully
    /// </summary>
    FlagSubmitted = 0,

    /// <summary>
    /// Answer is correct
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Answer is wrong
    /// </summary>
    WrongAnswer = 2,

    /// <summary>
    /// Cheating detected
    /// </summary>
    CheatDetected = 3,

    /// <summary>
    /// Submitted challenge instance not found
    /// </summary>
    NotFound = -1
}

public static class AnswerResultExtensions
{
    extension(AnswerResult result)
    {
        public string ToShortString(IStringLocalizer<Program> localizer) =>
            result switch
            {
                AnswerResult.FlagSubmitted => localizer[nameof(Resources.Program.Submission_FlagSubmitted)],
                AnswerResult.Accepted => localizer[nameof(Resources.Program.Submission_Accepted)],
                AnswerResult.WrongAnswer => localizer[nameof(Resources.Program.Submission_WrongAnswer)],
                AnswerResult.CheatDetected => localizer[nameof(Resources.Program.Submission_CheatDetected)],
                AnswerResult.NotFound => localizer[nameof(Resources.Program.Submission_UnknownInstance)],
                _ => "??"
            };
    }
}

/// <summary>
/// Compression format
/// </summary>
public enum CompressionFormat
{
    None,
    GZip,
    Zstd
}

/// <summary>
/// System error codes, starting from 10000
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Game not started
    /// </summary>
    public const int GameNotStarted = 10001;

    /// <summary>
    /// Game ended
    /// </summary>
    public const int GameEnded = 10002;
}

/// <summary>
/// Motion preference for the participant dashboard
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MotionPreference>))]
public enum MotionPreference : byte
{
    /// <summary>
    /// Follow the platform setting, i.e. the browser's <c>prefers-reduced-motion</c> media query
    /// </summary>
    System = 0,

    /// <summary>
    /// Always play the full set of transitions, regardless of the platform setting
    /// </summary>
    Full = 1,

    /// <summary>
    /// Always use the reduced-motion presentation, regardless of the platform setting
    /// </summary>
    Reduced = 2
}
