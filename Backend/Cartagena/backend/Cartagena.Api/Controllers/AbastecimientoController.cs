using System.Text.RegularExpressions;
using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Cartagena.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Authorize(Policy="Admin")]
public sealed class AbastecimientoController(DbFactory db,PdfService pdf) : ApiControllerBase(db)
{
    [HttpGet("api/compras-proveedor")]
    public async Task<IActionResult> Ordenes()
    {
        await using var cn=await Db.OpenAsync();
        var rows=await cn.QueryAsync("""
          SELECT p.id_pedido,p.id_proveedor,pr.nombre,p.fecha,p.costo_total,p.estado,p.fecha_recepcion
          FROM pedido_compra p JOIN proveedor pr ON pr.id_proveedor=p.id_proveedor
          ORDER BY p.fecha DESC LIMIT 200
          """);
        return Ok(new {data=rows});
    }

    [HttpGet("api/compras-proveedor/{id:int}")]
    public async Task<IActionResult> Orden(int id)
    {
        await using var cn=await Db.OpenAsync();
        var order=await cn.QueryFirstOrDefaultAsync("SELECT * FROM pedido_compra WHERE id_pedido=@id",new {id});
        Ensure(order is not null,"Pedido de compra no encontrado",404);
        var items=await cn.QueryAsync("SELECT * FROM detalle_pedido_compra WHERE id_pedido=@id",new {id});
        return Ok(new {pedido=order,items});
    }

    [HttpPost("api/compras-proveedor")]
    public async Task<IActionResult> CrearOrden([FromBody] CompraProveedorDto d)
    {
        Ensure(d.Id_Proveedor>0 && d.Items is {Count:>0},"id_proveedor y lista de items obligatorios");
        Ensure(d.Items.Count<=100,"Máximo 100 renglones");
        Ensure(d.Items.All(i=>!string.IsNullOrWhiteSpace(i.Id_Libro) && i.Cantidad>0 && i.Costo_Unitario>=0),"Items inválidos");
        Ensure(d.Items.Select(i=>i.Id_Libro).Distinct().Count()==d.Items.Count,"No repitas libros en la misma orden");
        var total=d.Items.Sum(i=>i.Cantidad*i.Costo_Unitario);
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var supplier=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM proveedor WHERE id_proveedor=@id",new {id=d.Id_Proveedor},tx);
        Ensure(supplier==1,"Proveedor no encontrado",404);
        await cn.ExecuteAsync("INSERT INTO pedido_compra(id_proveedor,costo_total,estado) VALUES (@id,@total,'Pendiente')",new {id=d.Id_Proveedor,total},tx);
        var pid=await cn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()",transaction:tx);
        foreach(var it in d.Items)
            await cn.ExecuteAsync("""
              INSERT INTO detalle_pedido_compra(id_pedido,id_libro,cantidad,costo_unitario)
              VALUES (@pid,@Id_Libro,@Cantidad,@Costo_Unitario)
              """,new {pid,it.Id_Libro,it.Cantidad,it.Costo_Unitario},tx);
        await tx.CommitAsync();
        return StatusCode(201,new {id_pedido=pid,total=total.ToString("F2")});
    }

    [HttpPost("api/compras-proveedor/{id:int}/recibir")]
    public async Task<IActionResult> RecibirOrden(int id)
    {
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var order=await cn.QueryFirstOrDefaultAsync<EstadoCompra>("SELECT estado FROM pedido_compra WHERE id_pedido=@id FOR UPDATE",new {id},tx);
        Ensure(order is not null,"Compra no encontrada",404);
        Ensure(order!.estado!="Recibido","Pedido ya recibido",409);
        var items=await cn.QueryAsync<ItemCompra>("SELECT id_libro,cantidad FROM detalle_pedido_compra WHERE id_pedido=@id ORDER BY id_libro",new {id},tx);
        foreach(var it in items)
        {
            var n=await cn.ExecuteAsync("UPDATE libro SET stock=stock+@qty WHERE id_libro=@book",new {qty=it.cantidad,book=it.id_libro},tx);
            Ensure(n==1,"Libro no encontrado",404);
            await cn.ExecuteAsync("""
              INSERT INTO movimiento_inventario(id_libro,cantidad,tipo,referencia)
              VALUES (@book,@qty,'COMPRA',@ref)
              """,new {book=it.id_libro,qty=it.cantidad,@ref=id.ToString()},tx);
        }
        await cn.ExecuteAsync("UPDATE pedido_compra SET estado='Recibido',fecha_recepcion=UTC_TIMESTAMP() WHERE id_pedido=@id",new {id},tx);
        await tx.CommitAsync();
        return Ok(new {ok=true});
    }

    [HttpPost("api/inventario/merma")]
    public async Task<IActionResult> Merma([FromBody] MermaDto d)
    {
        Ensure(d.Cantidad>0 && !string.IsNullOrWhiteSpace(d.Id_Libro),"Libro/cantidad inválidos");
        Ensure(d.Referencia is {Length: >=5 and <=100},"Referencia de 5-100 caracteres");
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var changed=await cn.ExecuteAsync("UPDATE libro SET stock=stock-@qty WHERE id_libro=@id AND stock>=@qty",
            new {id=d.Id_Libro,qty=d.Cantidad},tx);
        Ensure(changed==1,"Inventario insuficiente",409);
        await cn.ExecuteAsync("""
          INSERT INTO movimiento_inventario(id_libro,cantidad,tipo,referencia)
          VALUES (@id,@qty,'MERMA',@ref)
          """,new {id=d.Id_Libro,qty=-d.Cantidad,@ref=d.Referencia},tx);
        await tx.CommitAsync();
        return Ok(new {ok=true});
    }

    [HttpPost("api/devoluciones-proveedor")]
    public async Task<IActionResult> DevolucionProveedor([FromBody] DevolucionProveedorDto d)
    {
        Ensure(d.Id_Proveedor>0 && d.Cantidad>0 && !string.IsNullOrWhiteSpace(d.Id_Libro) && !string.IsNullOrWhiteSpace(d.Motivo),"Campos inválidos");
        Ensure(d.Referencia is {Length:>=8 and <=100},"Referencia de 8-100 caracteres");
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var supplier=await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM proveedor WHERE id_proveedor=@id",new {id=d.Id_Proveedor},tx);
        Ensure(supplier==1,"Proveedor no encontrado",404);
        var changed=await cn.ExecuteAsync("UPDATE libro SET stock=stock-@qty WHERE id_libro=@id AND stock>=@qty",
            new {id=d.Id_Libro,qty=d.Cantidad},tx);
        Ensure(changed==1,"Stock insuficiente",409);
        await cn.ExecuteAsync("""
          INSERT INTO devolucion_proveedor(id_proveedor,id_libro,cantidad,motivo,referencia)
          VALUES (@Id_Proveedor,@Id_Libro,@Cantidad,@Motivo,@Referencia)
          """,d,tx);
        var did=await cn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()",transaction:tx);
        await cn.ExecuteAsync("""
          INSERT INTO movimiento_inventario(id_libro,cantidad,tipo,referencia)
          VALUES (@book,@qty,'DEVOLUCION_PROVEEDOR',@ref)
          """,new {book=d.Id_Libro,qty=-d.Cantidad,@ref=d.Referencia},tx);
        await tx.CommitAsync();
        return StatusCode(201,new {id_devolucion=did});
    }

    [HttpGet("api/compras-proveedor/reporte")]
    public async Task<IActionResult> ReporteCompras([FromQuery] string mes)
    {
        Ensure(!string.IsNullOrWhiteSpace(mes) && Regex.IsMatch(mes,@"^\d{4}-(0[1-9]|1[0-2])$"),"mes debe ser AAAA-MM");
        await using var cn=await Db.OpenAsync();
        var rows=(await cn.QueryAsync<CompraReporte>("""
          SELECT p.id_pedido,p.fecha,p.estado,pr.nombre,p.costo_total FROM pedido_compra p
          JOIN proveedor pr ON pr.id_proveedor=p.id_proveedor
          WHERE DATE_FORMAT(p.fecha,'%Y-%m')=@mes ORDER BY p.fecha DESC
          """,new {mes})).ToList();
        return Ok(new {mes,total=rows.Sum(r=>r.costo_total).ToString("F2"),compras=rows});
    }

    [HttpGet("api/inventario/reporte.pdf")]
    public async Task<IActionResult> InventarioPdf()
    {
        await using var cn=await Db.OpenAsync();
        var rows=await cn.QueryAsync<InventarioLinea>("""
          SELECT id_libro,titulo,precio,stock,stock_minimo,stock_maximo,descuento_porcentaje
          FROM libro ORDER BY titulo
          """);
        var lines=rows.Select(x=>$"{x.id_libro} | {x.titulo} | stock: {x.stock} | precio: {x.precio:F2} | descuento: {x.descuento_porcentaje}%");
        return File(pdf.Create("Reporte de inventario",lines),"application/pdf","inventario.pdf");
    }

    public sealed class EstadoCompra {public string estado {get;set;}="";}
    public sealed class ItemCompra {public string id_libro {get;set;}=""; public int cantidad {get;set;}}
    public sealed class CompraReporte {public int id_pedido {get;set;}public DateTime fecha {get;set;}public string estado {get;set;}="";public string nombre {get;set;}="";public decimal costo_total {get;set;}}
    public sealed class InventarioLinea {public string id_libro {get;set;}=""; public string titulo {get;set;}=""; public decimal precio {get;set;}public int stock {get;set;}public int stock_minimo {get;set;}public int stock_maximo {get;set;}public decimal descuento_porcentaje {get;set;}}
}
