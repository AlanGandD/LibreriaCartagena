using System.Net;
using System.Text;
using Cartagena.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cartagena.Tests;

public sealed class ApiTests
{
    [Fact]
    public void TicketGeneraPdfValido()
    {
        var bytes=new PdfService().Create("Ticket",["Venta #123", "Total $250"]);
        Assert.StartsWith("%PDF-1.4",Encoding.ASCII.GetString(bytes));
        Assert.Contains("%%EOF",Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData("/api/carrito")]
    [InlineData("/api/pedidos")]
    [InlineData("/api/paquetes")]
    [InlineData("/api/admin/resumen")]
    [InlineData("/api/ventas")]
    public async Task RutasProtegidasRechazanAnonimos(string path)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET",new string('x',64));
        Environment.SetEnvironmentVariable("JWT_ISSUER","microservicio-login");
        await using var server=new WebApplicationFactory<Program>();
        var response=await server.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
}
