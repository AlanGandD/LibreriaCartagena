using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Cartagena.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Route("api/cuenta")]
public sealed class CuentasController(DbFactory db,MailService mail) : ApiControllerBase(db)
{
    [HttpPost("verificacion/solicitar")]
    public async Task<IActionResult> SolicitarVerificacion([FromBody] CorreoDto d)
    {
        Ensure(!string.IsNullOrWhiteSpace(d.Correo),"Correo obligatorio");
        await using var cn=await Db.OpenAsync();
        var user=await cn.QueryFirstOrDefaultAsync<UserVerify>("SELECT id_usuario,correo_verificado FROM usuario WHERE correo=@correo",new {correo=d.Correo});
        if (user is not null && !user.correo_verificado)
        {
            var token=await IssueTokenAsync(cn,user.id_usuario,"verificacion",60);
            // Sin frontend: los correos incluyen el token para que se envíe por HTTP.
            await mail.SendAsync(d.Correo,"Verifica tu correo en Cartagena",
                "Token de verificación (válido 60 minutos): "+token+"\nEnvía POST /api/cuenta/verificacion/confirmar con {\"token\":\"...\"}.");
        }
        return Ok(new {mensaje="Si corresponde, enviaremos instrucciones"});
    }

    [HttpPost("verificacion/confirmar")]
    public async Task<IActionResult> ConfirmarVerificacion([FromBody] TokenDto d)
    {
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var uid=await ConsumeTokenAsync(cn,tx,d.Token,"verificacion");
        await cn.ExecuteAsync("UPDATE usuario SET correo_verificado=TRUE WHERE id_usuario=@uid",new {uid},tx);
        await tx.CommitAsync();
        return Ok(new {ok=true});
    }

    [HttpPost("recuperacion/solicitar")]
    public async Task<IActionResult> SolicitarRecuperacion([FromBody] CorreoDto d)
    {
        Ensure(!string.IsNullOrWhiteSpace(d.Correo),"Correo obligatorio");
        await using var cn=await Db.OpenAsync();
        var uid=await cn.QueryFirstOrDefaultAsync<int?>("SELECT id_usuario FROM usuario WHERE correo=@correo",new {correo=d.Correo});
        if (uid is not null)
        {
            var token=await IssueTokenAsync(cn,uid.Value,"recuperacion",20);
            await mail.SendAsync(d.Correo,"Recupera tu contraseña en Cartagena",
                "Token de recuperación (válido 20 minutos): "+token+"\nEnvía POST /api/cuenta/recuperacion/restablecer con token y nueva_contrasena.");
        }
        return Ok(new {mensaje="Si el correo existe, enviaremos instrucciones"});
    }

    [HttpPost("recuperacion/restablecer")]
    public async Task<IActionResult> Restablecer([FromBody] RestablecerDto d)
    {
        Ensure(d.Nueva_Contrasena is {Length:>=8 and <=200},"La contraseña debe tener de 8 a 200 caracteres");
        var hash=BCrypt.Net.BCrypt.HashPassword(d.Nueva_Contrasena);
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var uid=await ConsumeTokenAsync(cn,tx,d.Token,"recuperacion");
        await cn.ExecuteAsync("""
          UPDATE usuario SET contrasena_hash=@hash,intentos_fallidos=0,bloqueado_hasta=NULL
          WHERE id_usuario=@uid
          """,new {hash,uid},tx);
        await tx.CommitAsync();
        return Ok(new {ok=true});
    }

    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static async Task<string> IssueTokenAsync(MySqlConnector.MySqlConnection cn,int uid,string type,int minutes)
    {
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var tx=await cn.BeginTransactionAsync();
        await cn.ExecuteAsync("DELETE FROM token_cuenta WHERE id_usuario=@uid AND tipo=@type",new {uid,type},tx);
        await cn.ExecuteAsync("""
          INSERT INTO token_cuenta(id_usuario,tipo,hash_token,expira_en)
          VALUES (@uid,@type,@hash,DATE_ADD(UTC_TIMESTAMP(),INTERVAL @minutes MINUTE))
          """,new {uid,type,hash=Digest(token),minutes},tx);
        await tx.CommitAsync();
        return token;
    }

    private static async Task<int> ConsumeTokenAsync(MySqlConnector.MySqlConnection cn,MySqlConnector.MySqlTransaction tx,string token,string type)
    {
        Ensure(!string.IsNullOrWhiteSpace(token) && Regex.IsMatch(token,"^[0-9a-f]{64}$"),"Token inválido o expirado");
        var found=await cn.QueryFirstOrDefaultAsync<TokenRow>("""
          SELECT id,id_usuario FROM token_cuenta
          WHERE hash_token=@hash AND tipo=@type AND usado_en IS NULL AND expira_en>UTC_TIMESTAMP()
          FOR UPDATE
          """,new {hash=Digest(token),type},tx);
        Ensure(found is not null,"Token inválido o expirado");
        await cn.ExecuteAsync("UPDATE token_cuenta SET usado_en=UTC_TIMESTAMP() WHERE id=@id",new {id=found!.id},tx);
        return found.id_usuario;
    }

    public sealed class UserVerify {public int id_usuario {get;set;} public bool correo_verificado {get;set;} }
    public sealed class TokenRow {public long id {get;set;} public int id_usuario {get;set;} }
}
