using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Slogs.Data;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ =>
{
    var handler = new CookieCredentialsHandler
    {
        InnerHandler = new HttpClientHandler()
    };

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
});
builder.Services.AddScoped(serviceProvider => new SlogsApiClient(serviceProvider.GetRequiredService<HttpClient>()));
builder.Services.AddScoped<SlogsAuthState>();

await builder.Build().RunAsync();
