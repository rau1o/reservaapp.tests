using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReservaApp.API.Data;
using ReservaApp.API.Models;

namespace ReservaApp.Tests.Integration;

/// <summary>
/// WebApplicationFactory personalizada para tests de integración.
/// Reemplaza SQL Server con una BD en memoria por cada test,
/// configura JWT de prueba, y ofrece helpers para seed y login.
/// </summary>
public class ReservaAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Sobrescribimos la configuración con valores de prueba
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]              = "clave-super-secreta-para-tests-de-jwt-32chars!",
                ["Jwt:Issuer"]           = "ReservaApp.Test",
                ["Jwt:Audience"]         = "ReservaApp.Test",
                ["Jwt:ExpiresInMinutes"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Removemos el DbContext de SQL Server registrado en Program.cs
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            // Registramos un DbContext con BD en memoria (único por factory)
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));
        });

        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Devuelve un scope del service provider para acceder a la BD de prueba.
    /// </summary>
    public AppDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>
    /// Inserta un usuario admin en la BD de prueba y devuelve el token JWT
    /// obtenido via POST /api/auth/login.
    /// </summary>
    public async Task<string> ObtenerTokenAdminAsync(HttpClient client,
        string email = "admin@reserva.test",
        string password = "Admin123!")
    {
        // Seedeamos el usuario directamente en la BD
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Usuarios.Any(u => u.Email == email))
        {
            db.Usuarios.Add(new Usuario
            {
                Nombre       = "Admin Test",
                Email        = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Rol          = "Admin"
            });
            await db.SaveChangesAsync();
        }

        // Hacemos login para obtener el token
        var loginPayload = new { email, password };
        var response = await client.PostAsJsonAsync("/api/auth/login", loginPayload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return body!.token;
    }

    // DTO anónimo para deserializar la respuesta de login
    private record LoginResponseDto(string token, string nombre, string rol, DateTime expiracion);
}
