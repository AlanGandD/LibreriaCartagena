using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Authorize]
public sealed class ClientesController(DbFactory db) : ApiControllerBase(db)
{
    [HttpGet("api/domicilios")]
    public async Task<IActionResult> Domicilios()
    {
        await using var cn = await Db.OpenAsync();
        var rows = await cn.QueryAsync("SELECT * FROM domicilio WHERE id_usuario=@uid", new { uid = UserId });
        return Ok(new { data = rows });
    }

    [HttpPost("api/domicilios")]
    public async Task<IActionResult> CrearDomicilio([FromBody] DireccionDto d)
    {
        await using var cn = await Db.OpenAsync();
        await cn.ExecuteAsync("""
          INSERT INTO domicilio (id_usuario,direccion,calle,numero,colonia,ciudad,estado,codigo_postal,telefono)
          VALUES (@uid,@direccion,@Calle,@Numero,@Colonia,@Ciudad,@Estado,@Codigo_Postal,@Telefono)
          """, new { uid = UserId, direccion = $"{d.Calle} {d.Numero}, {d.Colonia}", d.Calle,d.Numero,d.Colonia,d.Ciudad,d.Estado,d.Codigo_Postal,d.Telefono });
        return StatusCode(201, new { id_domicilio = await cn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()") });
    }

    [HttpGet("api/carrito")]
    public async Task<IActionResult> ObtenerCarrito()
    {
        await using var cn = await Db.OpenAsync();
        var cid = await cn.QueryFirstOrDefaultAsync<int?>("SELECT id_carrito FROM carrito WHERE id_usuario=@uid", new { uid=UserId });
        if (cid is null) return Ok(new { items = Array.Empty<object>(), cantidad_total = 0, total = "0.00" });
        var rows = (await cn.QueryAsync<CartItem>("""
          SELECT i.id_libro,i.cantidad,l.titulo,l.precio,l.descuento_porcentaje FROM item_carrito i
          JOIN libro l ON l.id_libro=i.id_libro WHERE i.id_carrito=@cid
          """, new { cid })).ToList();
        var total = rows.Sum(r => Price(r.precio, r.descuento_porcentaje) * r.cantidad);
        return Ok(new { items = rows, cantidad_total = rows.Sum(r => r.cantidad), total = total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) });
    }

    [HttpPut("api/carrito/{id}")]
    public async Task<IActionResult> ActualizarCarrito(string id, [FromBody] CantidadDto dto)
    {
        Ensure(dto.Cantidad is >= 0 and <= 1000, "cantidad debe ser entero de 0 a 1000");
        await using var cn = await Db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        var cid = await cn.QueryFirstOrDefaultAsync<int?>("SELECT id_carrito FROM carrito WHERE id_usuario=@uid FOR UPDATE", new { uid = UserId }, tx);
        if (cid is null)
        {
            await cn.ExecuteAsync("INSERT INTO carrito(id_usuario,total) VALUES (@uid,0)", new { uid=UserId }, tx);
            cid = (int)await cn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()", transaction: tx);
        }
        var stock = await cn.QueryFirstOrDefaultAsync<int?>("SELECT stock FROM libro WHERE id_libro=@id", new { id }, tx);
        Ensure(stock is not null, "Libro no encontrado", 404);
        Ensure(dto.Cantidad <= stock, "Inventario insuficiente", 409);
        var iid = await cn.QueryFirstOrDefaultAsync<int?>("SELECT id_item FROM item_carrito WHERE id_carrito=@cid AND id_libro=@id", new { cid, id }, tx);
        if (dto.Cantidad == 0)
            await cn.ExecuteAsync("DELETE FROM item_carrito WHERE id_carrito=@cid AND id_libro=@id", new { cid,id }, tx);
        else if (iid is not null)
            await cn.ExecuteAsync("UPDATE item_carrito SET cantidad=@qty WHERE id_item=@iid", new { qty=dto.Cantidad,iid }, tx);
        else
            await cn.ExecuteAsync("INSERT INTO item_carrito(id_carrito,id_libro,cantidad) VALUES (@cid,@id,@qty)", new { cid,id,qty=dto.Cantidad }, tx);
        var items = await cn.QueryAsync<CartItem>("""
          SELECT i.cantidad,l.precio,l.descuento_porcentaje FROM item_carrito i
          JOIN libro l ON l.id_libro=i.id_libro WHERE i.id_carrito=@cid
          """, new { cid }, tx);
        var total = items.Sum(i => Price(i.precio,i.descuento_porcentaje) * i.cantidad);
        await cn.ExecuteAsync("UPDATE carrito SET total=@total WHERE id_carrito=@cid", new { total,cid }, tx);
        await tx.CommitAsync();
        return Ok(new { ok = true, total = total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) });
    }

    public sealed class CartItem
    {
        public string id_libro { get; set; } = "";
        public int cantidad { get; set; }
        public string titulo { get; set; } = "";
        public decimal precio { get; set; }
        public decimal descuento_porcentaje { get; set; }
    }
}
