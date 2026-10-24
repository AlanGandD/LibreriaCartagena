using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Authorize]
public sealed class PaquetesController(DbFactory db) : ApiControllerBase(db)
{
    private static readonly string[] States=["Preparando","Listo","EntregadoAPaqueteria"];

    [HttpGet("api/paquetes")]
    [Authorize(Policy="Staff")]
    public async Task<IActionResult> Listar()
    {
        await using var cn=await Db.OpenAsync();
        var rows=await cn.QueryAsync("""
          SELECT p.*,v.estado AS estado_venta,e.estado AS estado_envio
          FROM paquete p JOIN venta v ON v.id_venta=p.id_venta
          LEFT JOIN envio e ON e.id_venta=p.id_venta ORDER BY p.id_paquete DESC LIMIT 200
          """);
        return Ok(new { data=rows });
    }

    [HttpGet("api/paquetes/{id:int}")]
    public async Task<IActionResult> Detalle(int id)
    {
        await using var cn=await Db.OpenAsync();
        var owner=await cn.QueryFirstOrDefaultAsync<PaqueteOwner>("""
          SELECT p.id_venta,v.id_usuario FROM paquete p
          JOIN venta v ON v.id_venta=p.id_venta WHERE p.id_paquete=@id
          """,new { id });
        Ensure(owner is not null,"Paquete no encontrado",404);
        RequireOwnerOrStaff(owner!.id_usuario);
        var row=await cn.QueryFirstAsync("SELECT * FROM paquete WHERE id_paquete=@id",new { id });
        return Ok(new { data=row });
    }

    [HttpPost("api/paquetes")]
    [Authorize(Policy="Admin")]
    public async Task<IActionResult> Crear([FromBody] PaqueteCrearDto d)
    {
        Ensure(d.Id_Venta>0,"id_venta inválido");
        foreach(var m in new[] {d.Peso_Kg,d.Largo_Cm,d.Ancho_Cm,d.Alto_Cm})
            Ensure(m>0 && m<=9999.99m,"Medidas deben ser positivas y menores a 9999.99");
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var sale=await cn.QueryFirstOrDefaultAsync<PayableSale>("""
          SELECT v.estado,v.estado_pago,t.nombre AS metodo FROM venta v
          JOIN tipo_pago t ON t.id_tipo_pago=v.id_tipo_pago
          WHERE v.id_venta=@id FOR UPDATE
          """,new { id=d.Id_Venta },tx);
        Ensure(sale is not null,"Venta no encontrada",404);
        Ensure(sale!.estado!="Cancelado","No se puede empaquetar una venta cancelada",409);
        Ensure(sale.estado_pago is "AprobadoSandbox" or "ConfirmadoManual" ||
            sale.metodo.Equals("efectivo contra entrega",StringComparison.OrdinalIgnoreCase),
            "Confirma el pago antes de empaquetar",409);
        await cn.ExecuteAsync("""
          INSERT INTO paquete(id_venta,peso_kg,largo_cm,ancho_cm,alto_cm,estado,id_usuario_empaqueto)
          VALUES (@Id_Venta,@Peso_Kg,@Largo_Cm,@Ancho_Cm,@Alto_Cm,'Preparando',@uid)
          """,new {d.Id_Venta,d.Peso_Kg,d.Largo_Cm,d.Ancho_Cm,d.Alto_Cm,uid=UserId},tx);
        var pid=await cn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()",transaction:tx);
        await cn.ExecuteAsync("UPDATE envio SET estado='En Preparación' WHERE id_venta=@id",new { id=d.Id_Venta },tx);
        await tx.CommitAsync();
        return StatusCode(201,new { id_paquete=pid,id_venta=d.Id_Venta,estado="Preparando" });
    }

    [HttpPatch("api/paquetes/{id:int}")]
    [Authorize(Policy="Staff")]
    public async Task<IActionResult> CambiarEstado(int id,[FromBody] EstadoDto d)
    {
        Ensure(States.Contains(d.Estado),"Estado de paquete no permitido");
        if (d.Numero_Guia is not null) Ensure(d.Numero_Guia.Trim().Length is >= 5 and <= 100,"numero_guia inválido");
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var current=await cn.QueryFirstOrDefaultAsync<PaqueteState>(
            "SELECT id_venta,estado FROM paquete WHERE id_paquete=@id FOR UPDATE",new { id },tx);
        Ensure(current is not null,"Paquete no encontrado",404);
        var before=Array.IndexOf(States,current!.estado);
        var next=Array.IndexOf(States,d.Estado);
        Ensure(before>=0 && next>=before && next<=before+1,"Transición de empaquetado no permitida",409);
        if (next==before) return Ok(new { ok=true,estado=d.Estado,repetido=true });
        if (d.Estado=="EntregadoAPaqueteria")
            Ensure(!string.IsNullOrWhiteSpace(d.Numero_Guia),"Se requiere número de guía");
        await cn.ExecuteAsync("UPDATE paquete SET estado=@estado,fecha_actualizacion=NOW() WHERE id_paquete=@id",
            new { estado=d.Estado,id },tx);
        if (d.Estado=="EntregadoAPaqueteria")
            await cn.ExecuteAsync("UPDATE envio SET estado='En Camino',numero_guia=@guia WHERE id_venta=@vid",
                new { guia=d.Numero_Guia,vid=current.id_venta },tx);
        await tx.CommitAsync();
        return Ok(new { ok=true,estado=d.Estado });
    }
    public sealed class PaqueteOwner {public int id_venta {get;set;} public int id_usuario {get;set;} }
    public sealed class PayableSale {public string estado {get;set;}=""; public string estado_pago {get;set;}=""; public string metodo {get;set;}=""; }
    public sealed class PaqueteState {public int id_venta {get;set;} public string estado {get;set;}=""; }
}
