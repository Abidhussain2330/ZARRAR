using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using ZARRAR.Models;

namespace ZARRAR.Extensions.Startup;

internal static class IdentityExtension
{
    extension(WebApplicationBuilder builder)
    {
        public void ConfigureIdentity()
        {
            builder.Services.AddDataProtection().PersistKeysToDbContext<AppDbContext>();

            builder.Services.AddAuthentication(o =>
                {
                    o.DefaultScheme = IdentityConstants.ApplicationScheme;
                    o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
                })
                .AddIdentityCookies(options =>
                {
                    options.ApplicationCookie?.Configure(auth =>
                    {
                        auth.Cookie.Name = "ZARRAR_Token";
                        auth.SlidingExpiration = true;
                        auth.ExpireTimeSpan = TimeSpan.FromDays(7);

                        // Previously only the name and lifetime were set, leaving SameSite,
                        // HttpOnly and the secure policy to framework defaults that vary by
                        // browser and deployment (finding F-023). State them explicitly.
                        auth.Cookie.HttpOnly = true;
                        auth.Cookie.SameSite = SameSiteMode.Lax;
                        auth.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                            ? CookieSecurePolicy.SameAsRequest
                            : CookieSecurePolicy.Always;
                    });
                });

            builder.Services.AddIdentityCore<UserInfo>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequiredLength = Limits.MinPasswordLength;
                    options.Password.RequiredUniqueChars = 4;
                    options.SignIn.RequireConfirmedEmail = true;

                    // Allow all characters in username
                    options.User.AllowedUserNameCharacters = string.Empty;

                    // No lockout policy was configured at all, and the login path passed
                    // lockoutOnFailure:false, so credential stuffing was bounded only by the
                    // broad global rate limit (finding F-009). Thresholds are deliberately
                    // moderate: low enough to blunt guessing, high enough that an attacker
                    // cannot cheaply lock legitimate competitors out mid-event.
                    options.Lockout.AllowedForNewUsers = true;
                    options.Lockout.MaxFailedAccessAttempts = 10;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                })
                .AddSignInManager<SignInManager<UserInfo>>()
                .AddUserManager<UserManager<UserInfo>>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddErrorDescriber<TranslatedIdentityErrorDescriber>()
                .AddDefaultTokenProviders();

            builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
                o.TokenLifespan = TimeSpan.FromHours(3)
            );

            // Session cookies are re-checked against the account's security stamp every minute
            // (default 30). Banning a player, "sign out everywhere" or a password change from the
            // SOC therefore ends their sessions within a minute instead of half an hour.
            builder.Services.Configure<SecurityStampValidatorOptions>(o =>
                o.ValidationInterval = TimeSpan.FromMinutes(1));
        }
    }
}
