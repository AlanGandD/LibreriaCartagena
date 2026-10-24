using Cartagena.Api.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[ApiController]
public sealed class SistemaController(DbFactory db) : ControllerBase
{
    [HttpGet("api/core/health")]
    public async Task<IActionResult> Health()
    {
        try
        {
            await using var cn=await db.OpenAsync();
            var one=await cn.ExecuteScalarAsync<int>("SELECT 1");
            return Ok(new {ok=one==1,servicio="cartagena-api-csharp"});
        }
        catch { return StatusCode(503,new {ok=false,servicio="cartagena-api-csharp"}); }
    }
}
