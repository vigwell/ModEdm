using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using ModEdmZipAnalyzer;


var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ModEdmZipAnalyzerService";
});
LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
builder.Services.AddHostedService<Worker>();
builder.Logging.AddConfiguration(
    builder.Configuration.GetSection("Logging"));
var host = builder.Build();
host.Run();

