using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ReservaApp.API.Data;
using ReservaApp.API.DTOs;
using ReservaApp.API.Models;
using ReservaApp.API.Services;
using Xunit;

namespace ReservaApp.Tests.Unit;

/// <summary>
/// Tests unitarios para AuthService.
/// AuthService accede directamente a AppDbContext (no via repositorio),
/// por eso usamos InMemory EF Core en lugar de Moq puro.
/// Cada test recibe una BD en memoria nueva e independiente.
/// </summary>
public class AuthServiceTests : IDisposable
{
    // ── Base de datos en memoria ───────────────────────────────────────────────
    private readonly AppDbContext _db;

    // ── Configuración JWT de prueba ────────────────────────────────────────────
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]              = "clave-super-secreta-para-tests-de-jwt-32chars!",
                ["Jwt:Issuer"]           = "ReservaApp.Test",
                ["Jwt:Audience"]         = "ReservaApp.Test",
                ["Jwt:ExpiresInMinutes"] = "60"
            })
            .Build();

    public AuthServiceTests()
    {
        // Cada instancia de test usa una BD en memoria distinta (Guid garantiza aislamiento)
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts);
    }

    public void Dispose() => _db.Dispose();

    // ── Helper: inserta un usuario con password hasheada ─────────────────────
    private async Task<Usuario> SeedUsuarioAsync(
        string email = "admin@test.com",
        string password = "Admin123!",
        string rol = "Admin")
    {
        var usuario = new Usuario
        {
            Nombre       = "Admin Test",
            Email        = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol          = rol
        };
        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync();
        return usuario;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LoginAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_CredencialesCorrectas_RetornaLoginResponse()
    {
        // Arrange
        await SeedUsuarioAsync(email: "admin@test.com", password: "Admin123!");
        var sut = new AuthService(_db, BuildConfig());
        var request = new LoginRequest("admin@test.com", "Admin123!");

        // Act
        var result = await sut.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.Nombre.Should().Be("Admin Test");
        result.Rol.Should().Be("Admin");
        result.Expiracion.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_PasswordIncorrecta_RetornaNull()
    {
        // Arrange
        await SeedUsuarioAsync(email: "admin@test.com", password: "Admin123!");
        var sut = new AuthService(_db, BuildConfig());
        var request = new LoginRequest("admin@test.com", "WrongPassword!");

        // Act
        var result = await sut.LoginAsync(request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_EmailInexistente_RetornaNull()
    {
        // Arrange — BD vacía, ningún usuario registrado
        var sut = new AuthService(_db, BuildConfig());
        var request = new LoginRequest("noexiste@test.com", "cualquier");

        // Act
        var result = await sut.LoginAsync(request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_CredencialesCorrectas_TokenContieneClaimsEsperados()
    {
        // Arrange
        await SeedUsuarioAsync(email: "recep@test.com", password: "Recep123!", rol: "Recepcionista");
        var sut = new AuthService(_db, BuildConfig());
        var request = new LoginRequest("recep@test.com", "Recep123!");

        // Act
        var result = await sut.LoginAsync(request);

        // Assert — decodificamos el JWT para verificar los claims
        result.Should().NotBeNull();
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result!.Token);

        jwt.Claims.Should().Contain(c =>
            c.Type == System.Security.Claims.ClaimTypes.Email &&
            c.Value == "recep@test.com");

        jwt.Claims.Should().Contain(c =>
            c.Type == System.Security.Claims.ClaimTypes.Role &&
            c.Value == "Recepcionista");
    }
}
