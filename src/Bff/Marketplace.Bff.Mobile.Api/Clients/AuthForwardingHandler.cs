using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace Marketplace.Bff.Mobile.Api.Clients;

/// <summary>
/// Gelen isteğin Authorization (Bearer) header'ını downstream servis çağrılarına taşır.
/// Böylece kullanıcının kimliği ve tenant claim'i tüm zincir boyunca korunur (BFF ayrı bir
/// servis hesabı kullanmaz; müşterinin kendi token'ıyla çağırır).
/// </summary>
public sealed class AuthForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;

    public AuthForwardingHandler(IHttpContextAccessor accessor) => _accessor = accessor;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var incoming = _accessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();
        if (!string.IsNullOrWhiteSpace(incoming) &&
            AuthenticationHeaderValue.TryParse(incoming, out var parsed))
        {
            request.Headers.Authorization = parsed;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
