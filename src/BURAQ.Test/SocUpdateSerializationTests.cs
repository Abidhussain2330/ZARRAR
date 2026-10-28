using System.Text.Json;
using BURAQ.Models.Request.Admin;
using BURAQ.Utils;
using Xunit;

namespace BURAQ.Test;

public sealed class SocUpdateSerializationTests
{
    [Fact]
    public void LiveSocUpdateUsesSourceGeneratedMetadata()
    {
        var update = new SocUpdateModel(
            "event",
            DateTimeOffset.UnixEpoch,
            JsonSerializer.SerializeToElement(
                new SocUpdateDataModel(Guid.NewGuid(), "ChallengeUpdated", "Info", "PlatformSecurity",
                    "test", null, null, 2, 1, 6, null),
                AppJsonSerializerContext.Default.SocUpdateDataModel));

        var json = JsonSerializer.Serialize(update, AppJsonSerializerContext.Default.SocUpdateModel);

        Assert.Contains("ChallengeUpdated", json);
        Assert.DoesNotContain("System.NotSupportedException", json);
    }
}
