using Microsoft.AspNetCore.Components;

namespace Slogs.Components.Helpers;

public static class PageStateKeyBuilder
{
    public static string FromNavigation(NavigationManager navigation)
    {
        var uri = new Uri(navigation.Uri);
        return string.IsNullOrWhiteSpace(uri.Query)
            ? uri.AbsolutePath
            : uri.PathAndQuery;
    }
}
