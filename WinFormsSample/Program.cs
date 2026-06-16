using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Vimes.SignSDK;
using Vimes.SignSDK.Merchants.MySign;
using Vimes.SignSDK.Merchants.SmartCA;
using Vimes.SignSDK.Merchants.USB;
using Vimes.SignSDK.Merchants.Self;
using Vimes.SignSDK.Merchants.BCY;
using Vimes.SignSDK.Merchants.SIM;
using Vimes.SignSDK.Merchants.InTrust;
using Vimes.SignSDK.Merchants.CMC;
using System.IO;
using System.Reflection;

namespace WinFormsSample;

static class Program
{
    public static IHost? Host { get; private set; }

    // Assembly names whose version mismatches should be silently redirected
    // to whatever version is already loaded (handles stale local nuget packs).
    private static readonly string[] _redirectedAssemblies =
    [
        "Vimes.SignSDK.Abstractions",
        "Vimes.SignSDK.Merchant.Base",
        "Vimes.Core",
        "Vimes.Signature.Domain",
    ];

    [STAThread]
    static void Main()
    {
        // .NET Core has no XML binding redirects; register a resolver that
        // redirects any version of our own SDK assemblies to the loaded version.
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name);
            if (Array.Exists(_redirectedAssemblies,
                    n => string.Equals(n, requested.Name, StringComparison.OrdinalIgnoreCase)))
            {
                // Return the already-loaded version if available
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(
                        a.GetName().Name, requested.Name,
                        StringComparison.OrdinalIgnoreCase));
                return loaded;
            }
            return null;
        };

        ApplicationConfiguration.Initialize();

        var builder = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        var configuration = builder.Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/winformsample-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddConfiguration(configuration);
            })
            .ConfigureServices((context, services) =>
            {
                // Add SignSDK
                services.AddSignSDK(context.Configuration);
                
                // Register All Merchants
                services.AddSignSDKMySign();
                services.AddSignSDKSmartCA();
                services.AddSignSDKUSB();
                services.AddSignSDKSelf();
                services.AddSignSDKBCY();
                services.AddSignSDKSIM();
                services.AddSignSDKInTrust();
                services.AddSignSDKCMC();

                // Add Forms
                services.AddTransient<MainForm>();
                
                // Add SignEnvironment for Desktop app
                services.AddSingleton<Core.Common.Abstractions.ISignEnvironment>(new DesktopSignEnvironment());
            })
            .UseSerilog()
            .Build();

        using var scope = Host.Services.CreateScope();
        var mainForm = scope.ServiceProvider.GetRequiredService<MainForm>();
        Application.Run(mainForm);
    }
}

public class DesktopSignEnvironment : Core.Common.Abstractions.ISignEnvironment
{
    public string WebRootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public string EnvironmentName { get; set; } = "Development";
}
