using Amazon.S3;
using Microsoft.Extensions.Options;
using VideoAnalyzer.Api;
using VideoAnalyzer.Core.Jobs;
using VideoAnalyzer.Api.Uploads;

var builder = WebApplication.CreateBuilder(args);

// Runs as a normal Kestrel app locally; inside Lambda it is fed API Gateway HTTP API (v2) events.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new JobStore(
    sp.GetRequiredService<IAmazonS3>(),
    sp.GetRequiredService<IOptions<StorageOptions>>().Value.MediaBucket));
builder.Services.AddSingleton<UploadService>();
builder.Services.AddProblemDetails();

// In AWS the UI and API share one CloudFront origin, so CORS is only needed when the
// Blazor dev server (localhost:5100) calls a locally running API.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5100").AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
    app.UseCors();

app.MapVideoAnalyzerApi();

app.Run();
