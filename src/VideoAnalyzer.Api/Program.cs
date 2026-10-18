using Amazon.S3;
using VideoAnalyzer.Api;
using VideoAnalyzer.Api.Jobs;
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
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<UploadService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapVideoAnalyzerApi();

app.Run();
