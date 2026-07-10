using System.Net.Http.Headers;

namespace Charta.Signing;

/// <summary>
/// A source of RFC 3161 timestamps. Given a DER-encoded <c>TimeStampReq</c>, it returns the TSA's
/// DER-encoded <c>TimeStampResp</c>. The default implementation posts to an HTTP TSA; supply your own
/// to route through an offline TSA, a queue, or a captured response.
/// </summary>
public interface ITimestampAuthority
{
    /// <summary>Sends a timestamp request to the authority and returns its raw response.</summary>
    byte[] RequestTimestamp(ReadOnlyMemory<byte> request);
}

/// <summary>Factory methods for <see cref="ITimestampAuthority"/>.</summary>
public static class TimestampAuthorities
{
    /// <summary>
    /// A timestamp authority reached over HTTP (RFC 3161 §3.4): posts the request as
    /// <c>application/timestamp-query</c> and expects an <c>application/timestamp-reply</c>.
    /// </summary>
    public static ITimestampAuthority Http(Uri url, string? username = null, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new HttpTimestampAuthority(url, username, password);
    }
}

internal sealed class HttpTimestampAuthority : ITimestampAuthority
{
    private static readonly HttpClient Client = new();
    private readonly Uri _url;
    private readonly string? _username;
    private readonly string? _password;

    public HttpTimestampAuthority(Uri url, string? username, string? password)
    {
        _url = url;
        _username = username;
        _password = password;
    }

    public byte[] RequestTimestamp(ReadOnlyMemory<byte> request)
    {
        using var content = new ByteArrayContent(request.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");

        using var message = new HttpRequestMessage(HttpMethod.Post, _url) { Content = content };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/timestamp-reply"));
        if (_username is not null && _password is not null)
        {
            var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_username}:{_password}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var response = Client.Send(message);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
