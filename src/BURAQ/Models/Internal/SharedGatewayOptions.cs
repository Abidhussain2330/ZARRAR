using System.ComponentModel.DataAnnotations;

namespace BURAQ.Models.Internal;

public sealed class SharedGatewayOptions
{
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>Dedicated secret supplied through environment/secret storage, never participant runtime env.</summary>
    public string SigningKey { get; set; } = string.Empty;

    [Range(30, 3600)]
    public int SessionMinutes { get; set; } = 30;

    [Range(15, 300)]
    public int TokenSeconds { get; set; } = 60;

    [Range(1024, 1073741824)]
    public long MaxRequestBodyBytes { get; set; } = 16 * 1024 * 1024;

    public bool AllowHttp { get; set; }
}
