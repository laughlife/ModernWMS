using ModernWMS.Core.Extentions;
using NLog;
using NLog.Web;

var logger = LogManager.Setup()
    .LoadConfigurationFromFile("nlog.config")
    .GetCurrentClassLogger();

try
{
    logger.Debug("--- run");

    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
    builder.Host.UseNLog();
    builder.Services.AddExtensionsService(builder.Configuration, builder.Environment);

    var app = builder.Build();
    app.UseExtensionsConfigure(app.Environment, app.Services, app.Configuration);
    app.Run();
}
catch (Exception exception)
{
    logger.Error(exception, "---- exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}

public partial class Program;
