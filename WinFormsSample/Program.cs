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
using Vimes.SignSDK.Merchants.Softdream;
using Vimes.SignSDK.Merchants.VNPT;
using Vimes.SignSDK.Merchants.SIM;
using Vimes.SignSDK.Merchants.InTrust;
using Vimes.SignSDK.Merchants.CMC;
using System.IO;

namespace WinFormsSample;

static class Program
{
    public static IHost? Host { get; private set; }

    [STAThread]
    static void Main()
    {
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
                services.AddSignSDKSoftdream();
                services.AddSignSDKVNPT();
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
