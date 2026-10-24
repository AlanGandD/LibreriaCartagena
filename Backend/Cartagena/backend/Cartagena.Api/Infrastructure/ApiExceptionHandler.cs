using Microsoft.AspNetCore.Diagnostics;
using MySqlConnector;

namespace Cartagena.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error, CancellationToken ct)
    {
        int code;
        string message;
        if (error is ApiException api)
        {
            code = api.StatusCode;
            message = api.Message;
        }
        else if (error is MySqlException sql && sql.Number is 1062 or 1451 or 1452)
        {
            code = 409;
            message = "Conflicto con datos existentes o relaciones de la base de datos";
        }
        else if (error is MySqlException)
        {
            code = 503;
            message = "Error al consultar MySQL; revisa el esquema y los logs";
        }
        else if (error is FormatException or OverflowException)
        {
            code = 400;
            message = "Formato numérico inválido";
        }
        else
        {
            code = 500;
            message = "Error interno";
        }
        if (code >= 500) logger.LogError(error, "Error al procesar {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = code;
        await context.Response.WriteAsJsonAsync(new { error = message }, cancellationToken: ct);
        return true;
    }
}
