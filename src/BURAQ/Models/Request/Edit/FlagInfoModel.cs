namespace BURAQ.Models.Request.Edit;

/// <summary>
/// Flag information (Edit)
/// </summary>
public class FlagInfoModel
{
    /// <summary>
    /// Flag Id
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Non-reversible, keyed fingerprint of the flag
    /// </summary>
    public string Fingerprint { get; set; } = FlagFingerprint.Empty;

    /// <summary>
    /// Attachment corresponding to the Flag
    /// </summary>
    public Attachment? Attachment { get; set; }

    /// <summary>
    /// Issued variants are immutable and cannot be deleted through normal administration.
    /// </summary>
    public bool IsAssigned { get; set; }

    internal static FlagInfoModel FromFlagContext(FlagContext context, byte[]? fingerprintKey) =>
        new()
        {
            Id = context.Id,
            Fingerprint = FlagFingerprint.Compute(context.Flag, fingerprintKey),
            Attachment = context.Attachment,
            IsAssigned = context.IsOccupied || context.VariantAssignment is not null
        };
}
