using Rotativa.AspNetCore;
using Serilog;
using Serilog.Events;
using VF_CR_Management_System.Business.Authentication;
using VF_CR_Management_System.Business.ChangeRequestHandler;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Business.DivisionHandler;
using VF_CR_Management_System.Business.ModuleHandler;
using VF_CR_Management_System.Business.UserHandler;
using VF_CR_Management_System.Business.VendorHandler;
using VF_CR_Management_System.Data.Context;
using VF_CR_Management_System.Presentation.Filters;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// SessionCheckAttribute is registered as a GLOBAL filter here instead of
// being applied per-controller with [SessionCheck] — this guarantees every
// controller is session-gated automatically, including any new controller
// added later that forgets to decorate itself with the attribute.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<SessionCheckAttribute>();
});

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // Silence the framework's own request/routing/action noise (Route matched,
    // Executing endpoint, Request starting/finished, etc.) so the file only
    // shows your app's own "Action: ..." lines plus real framework warnings/errors.
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: "Logs/log-.txt",
        rollingInterval: RollingInterval.Day,   // new file each day
        retainedFileCountLimit: 30,              // keep 30 days
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} | {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Register application services
builder.Services.AddScoped<ADAuthentication>();
builder.Services.AddScoped<DapperContext>();
builder.Services.AddScoped<_ConnectionService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IChangeRequestService, ChangeRequestService>();
builder.Services.AddScoped<IModuleService, ModuleService>();
builder.Services.AddScoped<IDivisionService, DivisionService>();
builder.Services.AddScoped<IVendorService, VendorService>();

// Add session services
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

var app = builder.Build();

RotativaConfiguration.Setup(app.Environment.WebRootPath, "Rotativa");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Enable session before authorization
app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=User}/{action=Login}/{id?}");

try
{
    Log.Information("Starting up VF_CR_Management_System");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed");
}
finally
{
    Log.CloseAndFlush();
}