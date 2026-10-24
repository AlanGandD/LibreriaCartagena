using System.Security.Claims;
using Cartagena.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Infrastructure;

[ApiController]
public abstract class ApiControllerBase(DbFactory db) : ControllerBase
{
    protected DbFactory Db { get; } = db;
    protected int UserId
    {
        get
        {
            var val = User.FindFirstValue("sub");
            if (!int.TryParse(val, out var uid) || uid <= 0) throw new ApiException("Token sin usuario válido", 401);
            return uid;
        }
    }
    protected string UserRole => User.FindFirstValue("rol")?.ToUpperInvariant() ?? "";
    protected bool Admin => UserRole == "ADMIN";
    protected void RequireOwnerOrStaff(int owner)
    {
        if (owner != UserId && UserRole is not ("ADMIN" or "REPARTIDOR")) throw new ApiException("Recurso de otro usuario", 403);
    }
    protected void RequireOwnerOrAdmin(int owner)
    {
        if (owner != UserId && !Admin) throw new ApiException("Prohibido", 403);
    }
    protected static void Ensure(bool valid, string message, int code = 400)
    {
        if (!valid) throw new ApiException(message, code);
    }
    protected static decimal Price(decimal price, decimal percent) =>
        Math.Round(price * (1 - percent / 100m), 2, MidpointRounding.AwayFromZero);
}
