using System.Text.Json;
using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Cartagena.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Route("api/libros")]
public sealed class LibrosController(DbFactory db) : ApiControllerBase(db)
{
    private const string Fields = "id_libro,titulo,autor,precio,stock,stock_minimo,stock_maximo,descripcion,isbn,editorial,categoria,genero,formato,imagen_portada,descuento_porcentaje";

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Listar([FromQuery] string? categoria, [FromQuery] string? genero,
        [FromQuery] string? formato, [FromQuery] string? q)
    {
        var where = new List<string>();
        var args = new DynamicParameters();
        foreach (var (name, value) in new[] { ("categoria", categoria), ("genero", genero), ("formato", formato) })
            if (!string.IsNullOrWhiteSpace(value)) { where.Add($"`{name}`=@{name}"); args.Add(name, value); }
        if (!string.IsNullOrWhiteSpace(q))
        {
            where.Add("(titulo LIKE @busqueda OR autor LIKE @busqueda OR isbn LIKE @busqueda)");
            args.Add("busqueda", "%" + q[..Math.Min(q.Length, 100)] + "%");
        }
        var sql = $"SELECT {Fields} FROM libro " + (where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where)) + " ORDER BY titulo LIMIT 200";
        await using var cn = await Db.OpenAsync();
        var rows = await cn.QueryAsync(sql, args);
        return Ok(new { data = rows.Select(Decorate).ToList() });
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Detalle(string id)
    {
        await using var cn = await Db.OpenAsync();
        var row = await cn.QueryFirstOrDefaultAsync($"SELECT {Fields} FROM libro WHERE id_libro=@id", new { id });
        if (row is null) throw new ApiException("Libro no encontrado", 404);
        return Ok(new { data = Decorate(row) });
    }

    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Crear([FromBody] LibroCrearDto data)
    {
        Ensure(data.Stock_Maximo >= data.Stock_Minimo, "stock_maximo debe ser >= stock_minimo");
        await using var cn = await Db.OpenAsync();
        const string sql = """
          INSERT INTO libro (id_libro,titulo,autor,precio,stock,stock_minimo,stock_maximo,
            descripcion,isbn,editorial,categoria,genero,formato,imagen_portada,descuento_porcentaje)
          VALUES (@Id_Libro,@Titulo,@Autor,@Precio,@Stock,@Stock_Minimo,@Stock_Maximo,
            @Descripcion,@Isbn,@Editorial,@Categoria,@Genero,@Formato,@Imagen_Portada,@Descuento_Porcentaje)
          """;
        await cn.ExecuteAsync(sql, data);
        return StatusCode(201, new { id_libro = data.Id_Libro });
    }

    [HttpPatch("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Actualizar(string id, [FromBody] JsonElement data)
    {
        Ensure(data.ValueKind == JsonValueKind.Object, "Se requiere objeto JSON");
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "titulo","autor","precio","stock","stock_minimo","stock_maximo","descripcion",
            "isbn","editorial","categoria","genero","formato","imagen_portada","descuento_porcentaje"
        };
        var update = new List<string>();
        var p = new DynamicParameters();
        foreach (var prop in data.EnumerateObject())
        {
            var key = prop.Name.ToLowerInvariant();
            Ensure(fields.Contains(key), "Campo no admitido: " + key);
            object? val;
            if (key is "precio" or "descuento_porcentaje")
            {
                Ensure(prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var _), key + " debe ser numérico");
                val = prop.Value.GetDecimal();
                Ensure((decimal)val >= 0 && (key != "descuento_porcentaje" || (decimal)val <= 100), "Valor inválido: " + key);
            }
            else if (key is "stock" or "stock_minimo" or "stock_maximo")
            {
                Ensure(prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var _), key + " debe ser entero");
                val = prop.Value.GetInt32();
                Ensure((int)val >= 0, key + " no puede ser negativo");
            }
            else
            {
                Ensure(prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null, key + " debe ser texto");
                val = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
            }
            p.Add(key, val);
            update.Add($"`{key}`=@{key}");
        }
        Ensure(update.Count > 0, "No hay campos para cambiar");
        p.Add("id", id);
        await using var cn = await Db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        var current = await cn.QueryFirstOrDefaultAsync<StockActual>(
            "SELECT stock FROM libro WHERE id_libro=@id FOR UPDATE", new { id }, tx);
        Ensure(current is not null, "Libro no encontrado", 404);
        if (data.TryGetProperty("descuento_porcentaje", out var d) && d.ValueKind == JsonValueKind.Number && d.GetDecimal() > 0)
            Ensure(current!.stock > 10, "Descuento manual permitido cuando el stock supera 10", 409);
        var changed = await cn.ExecuteAsync("UPDATE libro SET " + string.Join(",", update) + " WHERE id_libro=@id", p, tx);
        await tx.CommitAsync();
        return Ok(new { ok = true, filas = changed });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Eliminar(string id)
    {
        await using var cn = await Db.OpenAsync();
        var count = await cn.ExecuteAsync("DELETE FROM libro WHERE id_libro=@id", new { id });
        Ensure(count > 0, "Libro no encontrado", 404);
        return Ok(new { ok = true });
    }

    public sealed class StockActual { public int stock { get; set; } }

    private static Dictionary<string, object?> Decorate(dynamic row)
    {
        var output = ((IDictionary<string, object>)row).ToDictionary(x => x.Key, x => (object?)x.Value);
        var stock = Convert.ToInt32(output["stock"]);
        var price = Convert.ToDecimal(output["precio"]);
        var discount = Convert.ToDecimal(output["descuento_porcentaje"]);
        output["stock_bajo"] = stock < 3;
        output["stock_alto"] = stock > 10;
        output["precio_oferta"] = Price(price, discount).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        return output;
    }
}
