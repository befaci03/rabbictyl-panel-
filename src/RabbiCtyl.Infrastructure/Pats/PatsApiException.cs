using System.Net;

namespace RabbiCtyl.Infrastructure.Pats;

public sealed class PatsApiException : Exception
{
    public PatsApiException(HttpStatusCode statusCode, string responseBody)
        : base($"pats returned {(int)statusCode} ({statusCode}): {ExtractMessage(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no response body";
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? body;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through and use the raw body.
        }

        return body.Length > 500 ? body[..500] : body;
    }
}
