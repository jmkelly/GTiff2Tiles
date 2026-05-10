using Microsoft.AspNetCore.Http;

namespace GTiff2Tiles.Server.Services;

public static class Htmx
{
    public static bool IsHtmxRequest(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Equals(request.Headers["HX-Request"], "true", StringComparison.OrdinalIgnoreCase);
    }

    public static void Trigger(HttpResponse response, string eventName)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        response.Headers["HX-Trigger"] = eventName;
    }
}
