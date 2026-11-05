namespace NeedleBase.Web.Hosting;

internal static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Last handler: Web → Api traffic stays in-process when the Api is hosted here. Registered after the auth/IP
    /// handlers so they still decorate the request (Bearer token, X-Forwarded-For, X-NeedleBase-Client).
    /// </summary>
    public static IHttpClientBuilder AddInProcessApiHandler(this IHttpClientBuilder builder, bool hostApi)
    {
        if (hostApi)
        {
            builder.AddHttpMessageHandler(sp =>
                new InProcessApiHandler(sp.GetRequiredService<EmbeddedApiHostedService>()));
        }

        return builder;
    }
}
