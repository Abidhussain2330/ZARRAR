/*
 * BURAQ — THE ARZENS CTF Platform
 *
 * Copyright © 2025-present THE ARZENS
 *
 * BURAQ is a product of THE ARZENS.
 *
 * This source code is licensed under the Custom THE ARZENS License found in LICENSE.txt
 * in the root directory of this source tree.
 *
 * Compatibility identifiers required to open existing data are retained internally and are not
 * exposed as product branding.
 */

global using BURAQ.Models.Data;
global using BURAQ.Utils;
global using static BURAQ.Server;
global using AppDbContext = BURAQ.Models.AppDbContext;
global using TaskStatus = BURAQ.Utils.TaskStatus;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using BURAQ.Extensions.Startup;
using BURAQ.Models;
using Serilog;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Log.Logger = LogHelper.GetInitLogger();

Banner();

var builder = WebApplication.CreateBuilder(args);

await PathHelper.EnsureDirsAsync(builder.Environment);

builder.ConfigureWebHost();
builder.ConfigureDatabase();
builder.ConfigureStorage();
builder.ConfigureCacheAndSignalR();
builder.ConfigureIdentity();
builder.ConfigureTelemetry();

builder.AddServiceConfigurations();
builder.AddCustomServices();
builder.AddWebServices();
builder.AddDevelopmentServices();

var app = builder.Build();

Log.Logger = app.GetLogger();

await app.RunPrelaunchWorkAsync();

app.UseMiddlewares();

await app.RunServerAsync();

namespace BURAQ
{
    public class Program
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DesignTimeAppDbContextFactory))]
        static Program()
        {
            using var stream = typeof(Program).Assembly
                .GetManifestResourceStream("BURAQ.Resources.favicon.webp")!;
            DefaultFavicon = new byte[stream.Length];

            stream.ReadExactly(DefaultFavicon);
            DefaultFaviconHash = Convert.ToHexStringLower(SHA256.HashData(DefaultFavicon));
        }

        internal static byte[] DefaultFavicon { get; }
        internal static string DefaultFaviconHash { get; }
    }
}
