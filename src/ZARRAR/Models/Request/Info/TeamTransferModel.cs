using System.ComponentModel.DataAnnotations;

namespace ZARRAR.Models.Request.Info;

public class TeamTransferModel
{
    /// <summary>
    /// New captain ID
    /// </summary>
    [Required]
    public Guid NewCaptainId { get; set; }
}
