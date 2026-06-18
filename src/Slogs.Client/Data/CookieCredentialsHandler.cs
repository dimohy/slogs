using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Slogs.Data;

public sealed class CookieCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        return base.SendAsync(request, cancellationToken);
    }
}
