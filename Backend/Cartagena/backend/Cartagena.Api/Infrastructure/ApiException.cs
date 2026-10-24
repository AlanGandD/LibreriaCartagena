namespace Cartagena.Api.Infrastructure;

public sealed class ApiException(string message, int status = 400) : Exception(message)
{
    public int StatusCode { get; } = status;
}
