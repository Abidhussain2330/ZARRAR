namespace ZARRAR.Models;

public static class Limits
{
    /// <summary>
    /// Flag 最大长度
    /// </summary>
    public const int MaxFlagLength = 127;

    /// <summary>
    /// Flag 模板最大长度, 为替换操作预留空间
    /// </summary>
    public const int MaxFlagTemplateLength = 120;

    /// <summary>
    /// 队伍名称最大长度
    /// </summary>
    public const int MaxTeamNameLength = 40;

    /// <summary>
    /// 队伍签名最大长度（前端展示原因）
    /// </summary>
    public const int MaxTeamBioLength = 72;

    /// <summary>
    /// 个人数据存储最大长度（签名与真实姓名）
    /// </summary>
    public const int MaxUserDataLength = 128;

    /// <summary>
    /// 学工号最大长度
    /// </summary>
    public const int MaxStdNumberLength = 64;

    /// <summary>
    /// 用户名最小长度
    /// </summary>
    public const int MinUserNameLength = 3;

    /// <summary>
    /// 用户名最大长度
    /// </summary>
    public const int MaxUserNameLength = 40;

    /// <summary>
    /// 密码最小长度
    /// </summary>
    public const int MinPasswordLength = 10;

    /// <summary>
    /// Maximum accepted password length. Password hashing is intentionally expensive, so bounding
    /// attacker-controlled input prevents oversized login bodies from becoming a CPU/memory DoS.
    /// </summary>
    public const int MaxPasswordLength = 128;

    /// <summary>
    /// Maximum Base64 credential payload accepted on API-encrypted account endpoints. X25519 +
    /// AES-GCM adds 60 bytes before Base64 encoding, so this comfortably carries a valid
    /// <see cref="MaxPasswordLength"/> password while rejecting oversized request input early.
    /// </summary>
    public const int MaxEncryptedCredentialLength = 512;

    /// <summary>
    /// 文件哈希长度
    /// </summary>
    public const int FileHashLength = 64;

    /// <summary>
    /// Maximum length of a game's Base64-encoded token signature public key
    /// </summary>
    /// <remarks>
    /// The public key is stored as the raw 32-byte Ed25519 point, Base64-encoded — 44 characters.
    /// It is never sealed by <see cref="Utils.SecretProtector" />, so its size does not move with
    /// the envelope format and this bound is unchanged from the original schema.
    /// </remarks>
    public const int GamePublicKeyLength = 63;

    /// <summary>
    /// Maximum length of a game's Base64-encoded, AES-256-GCM protected token signature private key
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept separate from <see cref="GamePublicKeyLength" /> because the two no longer have the
    /// same requirement. The private key is sealed by <see cref="Utils.SecretProtector" /> before
    /// storage, and the F-017 remediation replaced the legacy raw XOR (which stored the 32
    /// plaintext bytes, 44 Base64 characters) with an authenticated envelope that is markedly
    /// larger: magic (4) + version (1) + nonce (12) + ciphertext (32) + GCM tag (16) = 65 bytes,
    /// which is 88 Base64 characters. The single shared 63-character bound the two properties used
    /// to share was therefore 25 characters too small, and every game insert failed on a deployment
    /// with <c>XorKey</c> configured.
    /// </para>
    /// <para>
    /// 255 covers that 88 with deliberate headroom: it admits a protected payload of up to 189
    /// bytes, i.e. a plaintext key of up to 156 bytes once the current 33 bytes of envelope
    /// overhead are removed. That leaves room for a longer key (Ed448 is 57 bytes), a larger nonce
    /// or tag, or a future envelope version, without another schema change. It stays an explicitly
    /// bounded column rather than <c>text</c> so an oversized value still fails closed at the
    /// database rather than being stored unchecked.
    /// </para>
    /// </remarks>
    public const int GameProtectedPrivateKeyLength = 255;

    /// <summary>
    /// 邀请 Token 长度
    /// </summary>
    public const int InviteTokenLength = 32;

    /// <summary>
    /// 最大 IP 长度
    /// </summary>
    public const int MaxIPLength = 40;

    /// <summary>
    /// 最大标题长度
    /// </summary>
    public const int MaxPostTitleLength = 50;

    /// <summary>
    /// 最大日志等级长度
    /// </summary>
    public const int MaxLogLevelLength = 15;

    /// <summary>
    /// 最大日志记录源长度
    /// </summary>
    public const int MaxLoggerLength = 250;

    /// <summary>
    /// 最大日志状态长度
    /// </summary>
    public const int MaxLogStatusLength = 10;

    /// <summary>
    /// 分组名称最大长度
    /// </summary>
    public const int MaxDivisionNameLength = 31;

    /// <summary>
    /// Maximum number of entries permitted in an imported game archive
    /// </summary>
    /// <remarks>
    /// Import validation covered only the compressed size and the declared content type, so a
    /// well-formed 512 MB archive could expand without bound and exhaust the server's disk
    /// (finding F-013). These four caps bound the extraction cost instead, and every one of them
    /// is far above what a genuine export produces.
    /// </remarks>
    public const int MaxImportEntryCount = 8192;

    /// <summary>
    /// Maximum uncompressed size of any single entry in an imported game archive (256 MB)
    /// </summary>
    public const long MaxImportEntrySize = 256L * 1024 * 1024;

    /// <summary>
    /// Maximum total uncompressed size of an imported game archive (2 GB)
    /// </summary>
    public const long MaxImportTotalSize = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum overall compression ratio permitted for an imported game archive
    /// </summary>
    public const int MaxImportCompressionRatio = 200;

    /// <summary>
    /// Maximum size of an uploaded writeup document (32 MB)
    /// </summary>
    /// <remarks>
    /// Administrative and writeup uploads accepted an unbounded multipart body
    /// (<c>long.MaxValue</c>), which let any authorised caller exhaust disk or memory
    /// (finding F-014).
    /// </remarks>
    public const long MaxWriteupSize = 32L * 1024 * 1024;

    /// <summary>
    /// Maximum size of an administrative asset upload (256 MB)
    /// </summary>
    public const long MaxAssetUploadSize = 256L * 1024 * 1024;

    /// <summary>
    /// Maximum lifetime of a newly issued API token, in days (90 days)
    /// </summary>
    /// <remarks>
    /// API tokens could be issued with no expiry at all, so a single leaked token stayed valid
    /// forever unless somebody noticed and revoked it (finding F-012). Creation now always writes a
    /// concrete <c>ExpiresAt</c> and refuses anything longer than this bound. 90 days is one
    /// rotation quarter: long enough for the scoreboard-sync and monitoring integrations that use
    /// these tokens to run unattended between maintenance windows, short enough that an
    /// unnoticed leak expires on its own.
    /// </remarks>
    public const int MaxApiTokenLifetimeDays = 90;

    /// <summary>
    /// Maximum number of entries the dashboard live solve feed will return in one response
    /// </summary>
    /// <remarks>
    /// The feed is ordered by submission time over the whole submission table, so an unbounded
    /// count would let a single caller ask the database for every accepted submission ever made.
    /// The cap is enforced both by the model binder and again inside the repository, so no code
    /// path can request more than this.
    /// </remarks>
    public const int MaxDashboardLiveSolveCount = 50;

    /// <summary>
    /// Maximum number of leaderboard entries the dashboard will return in one response
    /// </summary>
    public const int MaxDashboardTopParticipantCount = 50;

    /// <summary>
    /// Default number of entries returned by the dashboard live solve feed
    /// </summary>
    public const int DefaultDashboardLiveSolveCount = 20;

    /// <summary>
    /// Default number of leaderboard entries returned by the dashboard
    /// </summary>
    public const int DefaultDashboardTopParticipantCount = 10;
}
