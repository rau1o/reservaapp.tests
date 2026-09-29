using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ReservaApp.API.Data;
using ReservaApp.API.Models;
using System.Net;
using System.Net.Http.Json;

namespace ReservaApp.Tests.Integration;

/// <summary>
/// Tests de integración para AuthController.
/// Validan el flujo completo de autenticación: POST /api/auth/login
/// </summary>
public class AuthControllerTests : IClassFixture<ReservaAppFactory>
{
    private readonly ReservaAppFactory _factory;

    public AuthControllerTests(ReservaAppFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedUsuarioAsync(string email, string password, string rol = "Admin")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Usuarios.Any(u => u.Email == email))
        {
            db.Usuarios.Add(new Usuario
            {
                Nombre       = "Test User",
                Email        = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Rol          = rol
            });
            await db.SaveChangesAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/login — credenciales correctas → 200 con token
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_CredencialesCorrectas_Retorna200ConToken()
    {
        // Arrange
        var email = $"auth{Guid.NewGuid()}@test.com";
        await SeedUsuarioAsync(email, "Password123!");
        var client = _factory.CreateClient();
        var payload = new { email, password = "Password123!" };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseBody>();
        body!.token.Should().NotBeNullOrWhiteSpace();
        body.rol.Should().Be("Admin");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/login — password incorrecta → 401
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_PasswordIncorrecta_Retorna401()
    {
        // Arrange
        var email = $"auth{Guid.NewGuid()}@test.com";
        await SeedUsuarioAsync(email, "Password123!");
        var client = _factory.CreateClient();
        var payload = new { email, password = "WrongPassword!" };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/login — usuario no existe → 401
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_UsuarioInexistente_Retorna401()
    {
        // Arrange
        var client = _factory.CreateClient();
        var payload = new { email = "noexiste@test.com", password = "cualquiera" };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // DTO para deserializar la respuesta de login
    private record LoginResponseBody(string token, string nombre, string rol, DateTime expiracion);
}
