using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

// Application Insights for the worker process. The connection string comes
// from the APPLICATIONINSIGHTS_CONNECTION_STRING app setting in Azure;
// locally (none set) the worker simply runs without telemetry export.
// (The 3.x SDK registers everything through this one call — the 2.x-era
// ConfigureFunctionsApplicationInsights() helper no longer exists.)
builder.Services.AddApplicationInsightsTelemetryWorkerService();

builder.Build().Run();
