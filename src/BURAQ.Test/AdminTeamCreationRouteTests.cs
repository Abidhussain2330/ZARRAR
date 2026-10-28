using BURAQ.Controllers;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BURAQ.Test;

public sealed class AdminTeamCreationRouteTests
{
    [Fact]
    public void Admin_teams_exposes_a_post_endpoint_for_team_creation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(AdminController).Assembly);

        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;

        var createAction = actions.OfType<ControllerActionDescriptor>().SingleOrDefault(action =>
            action.ControllerTypeInfo == typeof(AdminController) &&
            action.ActionConstraints?.OfType<HttpMethodActionConstraint>()
                .Any(constraint => constraint.HttpMethods.Contains("POST", StringComparer.OrdinalIgnoreCase)) == true &&
            string.Equals(action.AttributeRouteInfo?.Template, "api/Admin/Teams", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(createAction);
    }
}
