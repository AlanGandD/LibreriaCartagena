using Cartagena.Api.Data;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Authorize]
public sealed class PedidosController(DbFactory db) : ApiControllerBase(db)
{
    [HttpGet("api/pedidos")]
    public async Task<IActionResult> MisPedidos()
    {
        await using var cn=await Db.OpenAsync();
        var rows=await cn.QueryAsync("""
          SELECT v.id_venta AS id_pedido,v.fecha,v.total,v.estado,v.estado_pago,
            e.estado AS estado_envio FROM venta v LEFT JOIN envio e ON e.id_venta=v.id_venta
          WHERE v.id_usuario=@uid ORDER BY v.fecha DESC LIMIT 200
          """,new { uid=UserId });
        return Ok(new { data=rows });
    }

    [HttpGet("api/pedidos/{id:int}")]
    public async Task<IActionResult> Consultar(int id)
    {
        await using var cn=await Db.OpenAsync();
        var header=await cn.QueryFirstOrDefaultAsync<SaleOwner>("SELECT id_usuario FROM venta WHERE id_venta=@id",new { id });
        Ensure(header is not null,"Pedido no encontrado",404);
        RequireOwnerOrStaff(header!.id_usuario);
        var sale=await cn.QueryFirstAsync("SELECT * FROM venta WHERE id_venta=@id",new { id });
        var items=await cn.QueryAsync("""
          SELECT d.id_libro,l.titulo,d.cantidad,d.precio_unitario FROM detalle_venta d
          JOIN libro l ON l.id_libro=d.id_libro WHERE d.id_venta=@id
          """,new { id });
        var envio=await cn.QueryFirstOrDefaultAsync("SELECT * FROM envio WHERE id_venta=@id",new { id });
        return Ok(new { pedido=sale,items,envio });
    }

    [HttpGet("api/admin/pedidos")]
    [Authorize(Policy="Admin")]
    public async Task<IActionResult> Todos()
    {
        await using var cn=await Db.OpenAsync();
        var rows=await cn.QueryAsync("""
          SELECT v.id_venta AS id_pedido,v.id_usuario,u.nombre,v.fecha,v.total,v.estado,v.estado_pago,
            e.estado AS estado_envio FROM venta v JOIN usuario u ON u.id_usuario=v.id_usuario
          LEFT JOIN envio e ON e.id_venta=v.id_venta ORDER BY v.fecha DESC LIMIT 200
          """);
        return Ok(new { data=rows });
    }

    [HttpGet("api/admin/resumen")]
    [Authorize(Policy="Admin")]
    public async Task<IActionResult> Resumen()
    {
        await using var cn=await Db.OpenAsync();
        var ventas=await cn.QueryFirstAsync("""
          SELECT COUNT(*) AS total_pedidos,
            COALESCE(SUM(CASE WHEN estado!='Cancelado' THEN total ELSE 0 END),0) AS ventas_net,
            COALESCE(SUM(CASE WHEN estado='Pendiente' THEN 1 ELSE 0 END),0) AS pendientes
          FROM venta
          """);
        var inventario=await cn.QueryFirstAsync("""
          SELECT COUNT(*) AS titulos,COALESCE(SUM(stock),0) AS unidades,
            COALESCE(SUM(CASE WHEN stock<3 THEN 1 ELSE 0 END),0) AS bajo_stock FROM libro
          """);
        return Ok(new { ventas,inventario });
    }

    [HttpGet("internal/staff-check")]
    [Authorize(Policy="Staff")]
    public IActionResult VerificarPersonal() => NoContent();

    public sealed class SaleOwner { public int id_usuario {get;set;} }
}
