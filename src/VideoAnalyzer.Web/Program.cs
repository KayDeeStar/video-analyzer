using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VideoAnalyzer.Web;
using VideoAnalyzer.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// In AWS the API is served from the same CloudFront domain (/api/*), so the default is the
// site's own address. Local development points ApiBaseUrl at the locally running API.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] is { Length: > 0 } configured
    ? configured
    : builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
