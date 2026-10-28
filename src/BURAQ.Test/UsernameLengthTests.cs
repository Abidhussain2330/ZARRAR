using System.ComponentModel.DataAnnotations;
using BURAQ.Models;
using BURAQ.Models.Data;
using BURAQ.Models.Request.Account;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BURAQ.Test;

public sealed class UsernameLengthTests
{
    [Fact]
    public void Registration_accepts_a_40_character_username()
    {
        var model = new RegisterModel
        {
            UserName = new string('a', 40),
            Email = "captain@example.test",
            Password = "encrypted-credential"
        };

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.True(valid);
    }

    [Fact]
    public void Profile_update_accepts_a_40_character_username()
    {
        var model = new ProfileUpdateModel { UserName = new string('a', 40) };

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.True(valid);
    }

    [Fact]
    public void Registration_rejects_a_41_character_username()
    {
        var model = new RegisterModel
        {
            UserName = new string('a', 41),
            Email = "captain@example.test",
            Password = "encrypted-credential"
        };

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), [], true);

        Assert.False(valid);
    }

    [Fact]
    public void Database_model_allows_a_40_character_username()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=tracking-only;Username=tracking;Password=tracking")
            .Options;

        using var context = new AppDbContext(options);
        var userName = context.Model.FindEntityType(typeof(UserInfo))!.FindProperty(nameof(UserInfo.UserName));

        Assert.NotNull(userName);
        Assert.Equal(40, userName.GetMaxLength());
    }
}
