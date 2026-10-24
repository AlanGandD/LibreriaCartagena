using System.Security.Cryptography;
using Cartagena.Api.Data;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Authorize]
[Route("api/libros/{id}/pdf")]
public sealed class DigitalController(DbFactory db,IConfiguration config) : ApiControllerBase(db)
{
    private const int MaxPdf=10*1024*1024;
    private string PdfDirectory => config["LIBROS_PDF_DIR"] ?? "/data/pdfs";

    [HttpPost]
    [Authorize(Policy="Admin")]
    [RequestSizeLimit(MaxPdf+1024*1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Subir(string id,IFormFile archivo)
    {
        Ensure(archivo is not null && archivo.Length > 4 && archivo.Length<=MaxPdf &&
            archivo.FileName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase),"Adjunta archivo PDF hasta 10 MB");
        Directory.CreateDirectory(PdfDirectory);
        var newName=Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()+".pdf";
        var path=Path.Combine(PdfDirectory,newName);
        await using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write))
            await archivo!.CopyToAsync(target);
        try
        {
            await using(var check=System.IO.File.OpenRead(path))
            {
                var bytes=new byte[5];
                if(await check.ReadAsync(bytes)!=5 || System.Text.Encoding.ASCII.GetString(bytes)!="%PDF-")
                    throw new ApiException("Archivo PDF inválido");
            }
            await using var cn=await Db.OpenAsync();
            await using var tx=await cn.BeginTransactionAsync();
            var old=await cn.QueryFirstOrDefaultAsync<PdfPath>("SELECT pdf_ruta FROM libro WHERE id_libro=@id FOR UPDATE",new {id},tx);
            Ensure(old is not null,"Libro no encontrado",404);
            await cn.ExecuteAsync("UPDATE libro SET pdf_ruta=@file,formato='Digital' WHERE id_libro=@id",new {file=newName,id},tx);
            await tx.CommitAsync();
            if (!string.IsNullOrWhiteSpace(old!.pdf_ruta))
            {
                var previous=Path.Combine(PdfDirectory,Path.GetFileName(old.pdf_ruta));
                if (Path.GetFullPath(previous).StartsWith(Path.GetFullPath(PdfDirectory)+Path.DirectorySeparatorChar,StringComparison.Ordinal) &&
                    !string.Equals(previous,path,StringComparison.Ordinal))
                {
                    try { System.IO.File.Delete(previous); }
                    catch (IOException) { /* El PDF nuevo ya está asociado a la BD; limpieza diferida. */ }
                    catch (UnauthorizedAccessException) { /* No interrumpir una transacción ya confirmada. */ }
                }
            }
            return StatusCode(201,new {ok=true,id_libro=id,formato="Digital"});
        }
        catch { System.IO.File.Delete(path); throw; }
    }

    [HttpGet]
    public async Task<IActionResult> Descargar(string id)
    {
        await using var cn=await Db.OpenAsync();
        var book=await cn.QueryFirstOrDefaultAsync<PdfPath>("SELECT pdf_ruta FROM libro WHERE id_libro=@id",new {id});
        Ensure(book is not null,"Libro no encontrado",404);
        Ensure(!string.IsNullOrWhiteSpace(book!.pdf_ruta),"Este libro no tiene PDF",404);
        if (!Admin)
        {
            var purchased=await cn.ExecuteScalarAsync<int>("""
              SELECT COUNT(*) FROM detalle_venta d JOIN venta v ON v.id_venta=d.id_venta
              WHERE v.id_usuario=@uid AND d.id_libro=@id AND v.estado!='Cancelado'
                AND v.estado_pago IN ('AprobadoSandbox','ConfirmadoManual')
              """,new {uid=UserId,id});
            Ensure(purchased>0,"Debes comprar el libro para descargarlo",403);
        }
        var full=Path.GetFullPath(Path.Combine(PdfDirectory,Path.GetFileName(book.pdf_ruta!)));
        Ensure(full.StartsWith(Path.GetFullPath(PdfDirectory)+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Ruta PDF inválida",403);
        Ensure(System.IO.File.Exists(full),"PDF no disponible en almacenamiento",503);
        Response.Headers["Cache-Control"]="no-store";
        Response.Headers["X-Content-Type-Options"]="nosniff";
        return PhysicalFile(full,"application/pdf",$"libro-{id[..Math.Min(id.Length,40)]}.pdf");
    }
    public sealed class PdfPath {public string? pdf_ruta {get;set;} }
}
