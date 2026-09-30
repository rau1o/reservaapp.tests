using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ReservaApp.API.Data;
using ReservaApp.API.Models;
using System.Net.Http.Json;
using System.Text;

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
                ["Jwt:Key"] = "clave-super-secreta-para-tests-de-jwt-32chars!",
                ["Jwt:Issuer"] = "ReservaApp.Test",
                ["Jwt:Audience"] = "ReservaApp.Test",
                ["Jwt:ExpiresInMinutes"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            // EF Core 9/10 registra DOS descriptores por cada AddDbContext():
            //   1. DbContextOptions<AppDbContext>
            //   2. IDbContextOptionsConfiguration<AppDbContext>  ← nuevo en EF 9+
            // Si solo removemos el primero, el proveedor SqlServer queda activo
            // y choca con InMemory → "Only a single database provider can be registered".
            // Solución: eliminar TODOS los descriptores que usen AppDbContext
            // como argumento de tipo genérico.
            var descriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericArguments().FirstOrDefault() == typeof(AppDbContext)))
                .ToList();

            foreach (var d in descriptors)
                services.Remove(d);

            // IMPORTANTE: el Guid debe capturarse FUERA del lambda.
            // Si está dentro, EF Core lo evalúa cada vez que construye los options
            // (una vez por scope), dando un nombre de BD distinto en cada llamada.
            // El seed escribiría en "TestDb_aaa" y el HTTP request leería de "TestDb_bbb".
            var dbName = "TestDb_" + Guid.NewGuid();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(dbName));

            // Sobreescribir los parámetros de validación JWT directamente.
            // ConfigureAppConfiguration carga la clave de prueba, pero el middleware
            // JwtBearer ya capturó los TokenValidationParameters al iniciarse.
            // PostConfigure corre DESPUÉS de toda la configuración y garantiza
            // que el middleware use la clave correcta durante los tests.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes("clave-super-secreta-para-tests-de-jwt-32chars!")),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true
                };
            });
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
                Nombre = "Admin Test",
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Rol = "Admin"
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