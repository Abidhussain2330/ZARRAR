using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Serilog;

namespace BURAQ;

public class Server
{
    internal const int MetricPort = 3000;
    internal const int ServerPort = 8080;

    internal static readonly string[] SupportedCultures =
    [
        "en-US",
        "zh-CN",
        "zh-TW",
        "ja-JP",
        "id-ID",
        "ko-KR",
        "ru-RU",
        "de-DE",
        "fr-FR",
        "es-ES",
        "vi-VN"
    ];

    private static readonly string LanguageWarning =
        $"Warning: Current language {CultureInfo.CurrentCulture.DisplayName} is machine translated and may not be accurate.\n";

    internal static IStringLocalizer<Program> StaticLocalizer { get; } =
        new CulturedLocalizer<Program>(CultureInfo.CurrentCulture);

    internal static void Banner()
    {
        // BURAQ Banner ASCII Art
        // BURAQ core and maintained components: Custom THE ARZENS License
        // Refer to LICENSE.txt and THIRD_PARTY_NOTICES.md for licensing policy.
        const string banner =
            """
             ██████╗ ██╗   ██╗██████╗  █████╗  ██████╗
             ██╔══██╗██║   ██║██╔══██╗██╔══██╗██╔═══██╗
             ██████╔╝██║   ██║██████╔╝███████║██║   ██║
             ██╔══██╗██║   ██║██╔══██╗██╔══██║██║▄▄ ██║
             ██████╔╝╚██████╔╝██║  ██║██║  ██║╚██████╔╝
             ╚═════╝  ╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝ ╚══▀▀═╝
                            T H E   A R Z E N S
            """ + "\n";
        Console.WriteLine(banner);

        var versionStr = "";
        var version = typeof(Program).Assembly.GetName().Version;
        if (version is not null)
            versionStr = $"Version: {version.Major}.{version.Minor}.{version.Build}";

        var currentYear = DateTime.Now.Year;

        // ReSharper disable once LocalizableElement
        Console.WriteLine($"BURAQ © 2025-{currentYear} THE ARZENS {versionStr,36}");

        // Upstream attribution. BURAQ is a derivative work of BURAQ; Custom THE ARZENS License and the
        // Keep the THE ARZENS project license notice with the software.
        // ReSharper disable once LocalizableElement
        Console.WriteLine("BURAQ © 2026 THE ARZENS — Custom THE ARZENS License\n");

        // Show warning if a language is machine translated
        string[] machineTranslated = ["de-DE", "fr-FR", "es-ES"];
        if (machineTranslated.Contains(CultureInfo.CurrentCulture.Name))
            Console.WriteLine(LanguageWarning);
    }

    internal static void ExitWithFatalMessage(string msg)
    {
        Log.Logger.Fatal("{msg}", msg);
        Thread.Sleep(30000);
        Environment.Exit(1);
    }

    internal static IActionResult InvalidModelStateHandler(ActionContext context)
    {
        var localizer =
            context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Program>>();
        if (context.ModelState.ErrorCount <= 0)
            return RequestResponse.Result(
                localizer[nameof(Resources.Program.Model_ValidationFailed)]);

        var error = context.ModelState.Values.Where(v => v.Errors.Count > 0)
            .Select(v => v.Errors.FirstOrDefault()?.ErrorMessage).FirstOrDefault();

        return RequestResponse.Result(error is [_, ..]
            ? error
            : localizer[nameof(Resources.Program.Model_ValidationFailed)]);
    }
}
