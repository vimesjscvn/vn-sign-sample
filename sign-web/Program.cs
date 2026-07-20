using Serilog;
using VMSign.Web.Services;

#if USE_SDK_SOURCE
using SignSDK;
using Signature.Merchant.MySign.Extensions;
using Signature.Merchant.SmartCA.Extensions;
using Vimes.SignSDK.Merchants.USB.Extensions;
using Vimes.SignSDK.Merchants.Self.Extensions;
using Vimes.SignSDK.Merchants.BCY.Extensions;
using Vimes.SignSDK.Merchants.SIM.Extensions;
using Vimes.SignSDK.Merchants.InTrust.Extensions;
using Vimes.SignSDK.Merchants.CMC.Extensions;
#else
using Vimes.SignSDK;
using Vimes.SignSDK.Merchants.MySign;
using Vimes.SignSDK.Merchants.SmartCA;
using Vimes.SignSDK.Merchants.USB;
using Vimes.SignSDK.Merchants.Self;
using Vimes.SignSDK.Merchants.BCY;
using Vimes.SignSDK.Merchants.SIM;
using Vimes.SignSDK.Merchants.InTrust;
using Vimes.SignSDK.Merchants.CMC;
#endif

var builder = WebApplication.CreateBuilder(args);

// Serilog: console + file always. Seq sink added only when Seq:Enable=true in config.
// No database/Postgres sink here — file+Seq only, per project preference.
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console()
        .WriteTo.File("logs/sign-web-.txt", rollingInterval: RollingInterval.Day);

    var seqUrl = ctx.Configuration["Seq:ServerUrl"];
    if (ctx.Configuration.GetValue<bool>("Seq:Enable") && !string.IsNullOrWhiteSpace(seqUrl))
    {
        cfg.WriteTo.Seq(seqUrl, apiKey: ctx.Configuration["Seq:ApiKey"]);
    }
});

// MVC
builder.Services.AddControllersWithViews();

// SignalR for real-time logs
builder.Services.AddSignalR();

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// === SignSDK (same as sign-app) ===
builder.Services.AddSignSDK(builder.Configuration);
builder.Services.AddSignSDKMySign();
builder.Services.AddSignSDKSmartCA();
builder.Services.AddSignSDKUSB();
builder.Services.AddSignSDKSelf();
builder.Services.AddSignSDKBCY();
builder.Services.AddSignSDKSIM();
builder.Services.AddSignSDKInTrust();
builder.Services.AddSignSDKCMC();

// AddSignSDK() internally calls AddControllersWithSnakeCaseJson(), which forces
// ALL MVC JSON responses in this app to snake_case (for the SDK's own API contract).
// This sample's browser JS reads camelCase (data.filePath, data.isLoggedIn, f.isSigned, ...),
// so without this override the client gets `undefined` for every property — e.g. the PDF
// preview never renders because this.filePath is undefined. Re-assert camelCase for this
// app's own controllers (the only controllers mapped here; the SDK's APIs are separate apps).
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ContractResolver =
            new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
    });

// Application services
builder.Services.AddScoped<WebSigningService>();
builder.Services.AddScoped<FileUploadService>();
builder.Services.AddScoped<GeminiLayoutService>();
builder.Services.AddScoped<LocalVisionLayoutService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<VMSign.Web.Hubs.LogHub>("/hubs/logs");

app.Run();
