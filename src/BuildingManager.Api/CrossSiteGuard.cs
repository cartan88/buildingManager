namespace BuildingManager.Api;

/// <summary>
/// Rejects state-changing /api requests that lack our custom header. Other websites open in the
/// user's browser can send simple cross-origin POSTs to localhost, but they can't add a custom header
/// without a CORS preflight, and this API never approves one.
/// </summary>
public static class CrossSiteGuard
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "BuildingManager";

    public static IApplicationBuilder UseCrossSiteGuard(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var req = ctx.Request;
        var changesState = !HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method) && !HttpMethods.IsOptions(req.Method);
        if (changesState && req.Path.StartsWithSegments("/api") && req.Headers[HeaderName] != HeaderValue)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next();
    });
}
