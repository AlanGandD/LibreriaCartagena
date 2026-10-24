using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Authorize]
public sealed class LogisticaController(DbFactory db) : ApiControllerBase(db)
{
    private static readonly string[] Estados=["Procesando","En Preparación","En Camino","Entregado"];

    [HttpGet("api/envios")]
    [Authorize(Policy="Staff")]
    public async Task<IActionResult> Envios()
    {
        await using var cn=await Db.OpenAsync();
        return Ok(new {data=await cn.QueryAsync("SELECT * FROM envio ORDER BY id_envio DESC LIMIT 200")});
    }

    [HttpPatch("api/envios/{id:int}")]
    [Authorize(Policy="Staff")]
    public async Task<IActionResult> ActualizarEnvio(int id,[FromBody] EstadoDto d)
    {
        Ensure(!string.IsNullOrWhiteSpace(d.Estado) && Estados.Contains(d.Estado),"Estado de envío no permitido");
        if (d.Numero_Guia is not null) Ensure(d.Numero_Guia.Trim().Length is >= 5 and <= 100,"numero_guia inválido");
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var row=await cn.QueryFirstOrDefaultAsync<EnvioRow>("SELECT estado,numero_guia FROM envio WHERE id_envio=@id FOR UPDATE",new { id },tx);
        Ensure(row is not null,"Envío no encontrado",404);
        Ensure(row!.estado!="Cancelado","No se puede modificar un envío cancelado",409);
        var prev=Array.IndexOf(Estados,row.estado);
        var next=Array.IndexOf(Estados,d.Estado);
        Ensure(prev<0 || next>=prev,"No se puede retroceder el estado del envío",409);
        Ensure(row.estado!="Entregado" || d.Estado=="Entregado","Envío ya entregado",409);
        if (d.Estado is "En Camino" or "Entregado")
            Ensure(!string.IsNullOrWhiteSpace(d.Numero_Guia) || !string.IsNullOrWhiteSpace(row.numero_guia),"Se requiere guía de seguimiento",409);
        await cn.ExecuteAsync("""
          UPDATE envio SET estado=@estado,numero_guia=COALESCE(@guia,numero_guia),
          fecha_entrega=CASE WHEN @estado='Entregado' THEN COALESCE(fecha_entrega,UTC_TIMESTAMP()) ELSE fecha_entrega END
          WHERE id_envio=@id
          """,new { estado=d.Estado,guia=d.Numero_Guia,id },tx);
        await tx.CommitAsync();
        return Ok(new { ok=true,estado=d.Estado });
    }

    [HttpPost("api/garantias")]
    [Authorize(Policy="Comprador")]
    public async Task<IActionResult> SolicitarGarantia([FromBody] GarantiaDto d)
    {
        Ensure(d.Id_Venta>0 && !string.IsNullOrWhiteSpace(d.Id_Libro) && !string.IsNullOrWhiteSpace(d.Motivo),"Campos de garantía inválidos");
        await using var cn=await Db.OpenAsync();
        var sale=await cn.QueryFirstOrDefaultAsync<CompraEntrega>("""
          SELECT v.id_usuario,e.fecha_entrega FROM venta v
          JOIN detalle_venta dv ON dv.id_venta=v.id_venta AND dv.id_libro=@book
          LEFT JOIN envio e ON e.id_venta=v.id_venta WHERE v.id_venta=@id
          """,new { book=d.Id_Libro,id=d.Id_Venta });
        Ensure(sale is not null,"La compra no contiene ese libro",404);
        Ensure(sale!.id_usuario==UserId,"Prohibido",403);
        Ensure(sale.fecha_entrega is not null && sale.fecha_entrega.Value<=DateTime.UtcNow &&
            DateTime.UtcNow-sale.fecha_entrega.Value<=TimeSpan.FromDays(7),
            "La entrega no está confirmada o han transcurrido más de 7 días",409);
        await cn.ExecuteAsync("""
          INSERT INTO solicitud_garantia(id_venta,id_usuario,id_libro,motivo)
          VALUES (@Id_Venta,@uid,@Id_Libro,@Motivo)
          """,new { d.Id_Venta,uid=UserId,d.Id_Libro,d.Motivo });
        return StatusCode(201,new { id_solicitud=await cn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()") });
    }

    [HttpGet("api/garantias")]
    public async Task<IActionResult> Garantias()
    {
        await using var cn=await Db.OpenAsync();
        var rows=Admin
            ? await cn.QueryAsync("SELECT * FROM solicitud_garantia ORDER BY fecha DESC LIMIT 200")
            : await cn.QueryAsync("SELECT * FROM solicitud_garantia WHERE id_usuario=@uid ORDER BY fecha DESC LIMIT 200",new {uid=UserId});
        return Ok(new {data=rows});
    }

    public sealed class EnvioRow {public string estado {get;set;}="";public string? numero_guia {get;set;} }
    public sealed class CompraEntrega {public int id_usuario {get;set;}public DateTime? fecha_entrega {get;set;} }
}
