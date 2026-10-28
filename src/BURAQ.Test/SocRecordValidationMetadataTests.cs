using System.ComponentModel.DataAnnotations;
using BURAQ.Models.Request.Admin;
using Xunit;

namespace BURAQ.Test;

public sealed class SocRecordValidationMetadataTests
{
    [Fact]
    public void RiskCaseUpdate_PlacesMaxLengthOnPrimaryConstructorParameter()
    {
        var type = typeof(SocRiskCaseUpdateModel);
        var parameter = type.GetConstructors().Single().GetParameters().Single(x => x.Name == "Notes");

        var attribute = Assert.Single(parameter.GetCustomAttributes(typeof(MaxLengthAttribute), false)
            .Cast<MaxLengthAttribute>());

        Assert.Equal(2000, attribute.Length);
        Assert.Empty(type.GetProperty(nameof(SocRiskCaseUpdateModel.Notes))!
            .GetCustomAttributes(typeof(MaxLengthAttribute), false));
    }

    [Fact]
    public void TeamInstanceLimitUpdate_PlacesRangeOnPrimaryConstructorParameter()
    {
        var type = typeof(SocTeamInstanceLimitUpdateModel);
        var parameter = type.GetConstructors().Single().GetParameters().Single(x => x.Name == "Limit");

        var attribute = Assert.Single(parameter.GetCustomAttributes(typeof(RangeAttribute), false)
            .Cast<RangeAttribute>());

        Assert.Equal(0, attribute.Minimum);
        Assert.Equal(1000, attribute.Maximum);
        Assert.Empty(type.GetProperty(nameof(SocTeamInstanceLimitUpdateModel.Limit))!
            .GetCustomAttributes(typeof(RangeAttribute), false));
    }
}
