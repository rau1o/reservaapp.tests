using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ReservaApp.API.Data;
using ReservaApp.API.DTOs;
using ReservaApp.API.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ReservaApp.Tests.Integration;

/// <summary>
/// Tests de integración para ReservasController.
/// Usan WebApplicationFactory para levantar la API completa en memoria,
/// reemplazando SQL Server con InMemory EF Core.
/// Validan el flujo HTTP completo: routing → middleware → servicio → repositorio.
/// </summary>
public class ReservasControllerTests : IClassFixture<ReservaAppFactory>
{
    private readonly ReservaAppFactory _factory;

    public ReservasControllerTests(ReservaAppFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Crea un HttpClient autenticado con token admin.</summary>
    private async Task<HttpClient> ClienteAutenticadoAsync()
    {
        var client = _factory.CreateClient();
        var token = await _factory.ObtenerTokenAdminAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Inserta datos de prueba en la BD InMemory.</summary>
    private async Task<(Cliente cliente, Servicio servicio)> SeedDatosBasicosAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "User",
            Email = $"test{Guid.NewGuid()}@test.com", Telefono = "70000001"
        };
        var servicio = new Servicio
        {
            Nombre = "Corte Test", Descripcion = "Desc",
            DuracionMin = 30, Precio = 50m
        };
        db.Clientes.Add(cliente);
        db.Servicios.Add(servicio);
        await db.SaveChangesAsync();
        return (cliente, servicio);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/reservas  — sin autenticación → 401
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_SinToken_Retorna401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/reservas");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/reservas  — con token válido → 200
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ConToken_Retorna200()
    {
        // Arrange
        var client = await ClienteAutenticadoAsync();

        // Act
        var response = await client.GetAsync("/api/reservas");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/reservas  — datos válidos → 201 Created
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_DatosValidos_Retorna201ConDto()
    {
        // Arrange
        var client = await ClienteAutenticadoAsync();
        var (cliente, servicio) = await SeedDatosBasicosAsync();

        var dto = new ReservaRequest(
            ClienteId: cliente.Id,
            ServicioId: servicio.Id,
            FechaHora: DateTime.UtcNow.AddDays(3),
            Notas: "Integración test");

        // Act
        var response = await client.PostAsJsonAsync("/api/reservas", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<ReservaResponse>();
        body.Should().NotBeNull();
        body!.ClienteId.Should().Be(cliente.Id);
        body.ServicioId.Should().Be(servicio.Id);
        body.Estado.Should().Be("Pendiente");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/reservas  — fecha pasada → 409 Conflict (por GlobalExceptionMiddleware)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_FechaPasada_Retorna409()
    {
        // Arrange
        var client = await ClienteAutenticadoAsync();
        var (cliente, servicio) = await SeedDatosBasicosAsync();

        var dto = new ReservaRequest(
            ClienteId: cliente.Id,
            ServicioId: servicio.Id,
            FechaHora: DateTime.UtcNow.AddDays(-1),  // fecha pasada
            Notas: null);

        // Act
        var response = await client.PostAsJsonAsync("/api/reservas", dto);

        // Assert — GlobalExceptionMiddleware mapea InvalidOperationException → 409
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/reservas/{id}  — id inexistente → 404 Not Found
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_IdInexistente_Retorna404()
    {
        // Arrange
        var client = await ClienteAutenticadoAsync();

        // Act
        var response = await client.GetAsync("/api/reservas/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PATCH /api/reservas/{id}/estado  — flujo completo crear → cambiar estado
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CambiarEstado_ReservaExistente_Retorna200ConNuevoEstado()
    {
        // Arrange — creamos la reserva primero
        var client = await ClienteAutenticadoAsync();
        var (cliente, servicio) = await SeedDatosBasicosAsync();

        var createDto = new ReservaRequest(cliente.Id, servicio.Id,
            DateTime.UtcNow.AddDays(5), null);
        var createResp = await client.PostAsJsonAsync("/api/reservas", createDto);
        var creada = await createResp.Content.ReadFromJsonAsync<ReservaResponse>();

        // Act
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/reservas/{creada!.Id}/estado",
            new { estado = "Confirmada" });

        // Assert
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var actualizada = await patchResp.Content.ReadFromJsonAsync<ReservaResponse>();
        actualizada!.Estado.Should().Be("Confirmada");
    }
}
