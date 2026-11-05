using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http.Features;

namespace NeedleBase.Web.Hosting;

/// <summary>
/// When the Api is hosted in this process (<c>Api:InProcess</c>), Web → Api HttpClient calls run through the Api's
/// pipeline directly. That avoids a hairpin out to the public site URL and back (TLS, DNS, the host's proxy, and the
/// rate-limit "all visitors share one IP" problem). Registered only with InProcess; Development keeps the real network
/// call to the standalone Api.
/// </summary>
public sealed class InProcessApiHandler(EmbeddedApiHostedService api) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Api request has no URI.");
        if (!uri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("Api request URI must be absolute (HttpClient BaseAddress + relative path).");
        }

        var responseBody = new MemoryStream();
        MemoryStream? requestBody = null;
        try
        {
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpRequestLifetimeFeature>(new RequestLifetime { RequestAborted = cancellationToken });
            context.Connection.RemoteIpAddress = IPAddress.Loopback; // trusted by UseForwardedHeaders; real IP is in X-Forwarded-For
            context.Request.Method = request.Method.Method;
            context.Request.Scheme = "https";
            context.Request.IsHttps = true;
            context.Request.Host = uri.IsDefaultPort ? new HostString(uri.Host) : new HostString(uri.Host, uri.Port);
            context.Request.Path = PathString.FromUriComponent(uri);
            context.Request.QueryString = QueryString.FromUriComponent(uri);
            context.Response.Body = responseBody;

            foreach (var header in request.Headers)
            {
                context.Request.Headers[header.Key] = header.Value.ToArray();
            }

            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    context.Request.Headers[header.Key] = header.Value.ToArray();
                }

                // Buffer: Api handlers may need to read the body more than once (model binding), and HttpContent
                // streams are often not rewindable after a single read.
                requestBody = new MemoryStream();
                await request.Content.CopyToAsync(requestBody, cancellationToken);
                requestBody.Position = 0;
                context.Request.Body = requestBody;
                context.Request.ContentLength = requestBody.Length;
            }

            await api.HandleAsync(context);

            responseBody.Position = 0;
            var response = new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode)
            {
                Content = new StreamContent(responseBody),
                RequestMessage = request
            };
            // StreamContent now owns responseBody; do not dispose it in finally. If header copy fails, dispose the
            // response (which disposes the stream) so nothing leaks.
            responseBody = null!;
            try
            {
                foreach (var header in context.Response.Headers)
                {
                    // Content-* belongs on HttpContent; the rest on the response. TryAdd avoids rejecting restricted names.
                    if (!response.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value))
                    {
                        response.Content.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
                    }
                }

                // StreamContent defaults to application/octet-stream when no Content-Type was copied; clear that so
                // callers that only check MediaType see what the Api set (or null).
                if (!context.Response.Headers.ContainsKey("Content-Type"))
                {
                    response.Content.Headers.ContentType = null;
                }

                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        finally
        {
            if (requestBody is not null)
            {
                await requestBody.DisposeAsync();
            }

            if (responseBody is not null)
            {
                await responseBody.DisposeAsync();
            }
        }
    }

    private sealed class RequestLifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public void Abort() { }
    }
}
