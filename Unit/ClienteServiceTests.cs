using FluentAssertions;
using Moq;
using ReservaApp.API.DTOs;
using ReservaApp.API.Models;
using ReservaApp.API.Repositories;
using ReservaApp.API.Services;
using Xunit;

namespace ReservaApp.Tests.Unit;

/// <summary>
/// Tests unitarios para ClienteService.
/// Patrón: AAA (Arrange · Act · Assert)
/// IClienteRepository se mockea con Moq — lógica del servicio aislada de BD.
/// </summary>
public class ClienteServiceTests
{
    // ── Mock de dependencia ────────────────────────────────────────────────────
    private readonly Mock<IClienteRepository> _repoMock = new();

    // Sistema bajo prueba (SUT)
    private readonly IClienteService _sut;

    public ClienteServiceTests()
    {
        _sut = new ClienteService(_repoMock.Object);
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    private static Cliente ClienteEjemplo(int id = 1) => new()
    {
        Id = id,
        Nombre = "Ana",
        Apellido = "García",
        Telefono = "70099988",
        Email = "ana@test.com",
        Activo = true,
        CreadoEn = DateTime.UtcNow
    };

    // ─────────────────────────────────────────────────────────────────────────
    // GetByIdAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ClienteExistente_RetornaDto()
    {
        // Arrange
        var cliente = ClienteEjemplo(id: 5);
        _repoMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(cliente);

        // Act
        var result = await _sut.GetByIdAsync(5);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(5);
        result.Nombre.Should().Be("Ana");
        result.Apellido.Should().Be("García");
        result.Email.Should().Be("ana@test.com");
        result.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_ClienteInexistente_LanzaKeyNotFoundException()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Cliente?)null);

        // Act
        Func<Task> act = () => _sut.GetByIdAsync(99);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CreateAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DatosValidos_CreaClienteYRetornaDto()
    {
        // Arrange
        var dto = new ClienteRequest("Ana", "García", "70099988", "ana@test.com");
        _repoMock.Setup(r => r.EmailExistsAsync(dto.Email, null)).ReturnsAsync(false);
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<Cliente>()))
            .ReturnsAsync((Cliente c) => { c.Id = 7; return c; });

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.Id.Should().Be(7);
        result.Nombre.Should().Be("Ana");
        result.Email.Should().Be("ana@test.com");
        _repoMock.Verify(r => r.CreateAsync(It.IsAny<Cliente>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EmailDuplicado_LanzaInvalidOperationException()
    {
        // Arrange — email ya existe en BD
        var dto = new ClienteRequest("Ana", "García", "70099988", "ana@test.com");
        _repoMock.Setup(r => r.EmailExistsAsync(dto.Email, null)).ReturnsAsync(true);

        // Act
        Func<Task> act = () => _sut.CreateAsync(dto);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*email ya está registrado*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UpdateAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_DatosValidos_ActualizaYRetornaDto()
    {
        // Arrange
        var clienteExistente = ClienteEjemplo(id: 3);
        var dto = new ClienteRequest("Ana", "López", "70011122", "ana.lopez@test.com");

        _repoMock.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(clienteExistente);
        _repoMock.Setup(r => r.EmailExistsAsync(dto.Email, 3)).ReturnsAsync(false);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<Cliente>()))
            .ReturnsAsync((Cliente c) => c);

        // Act
        var result = await _sut.UpdateAsync(3, dto);

        // Assert
        result.Apellido.Should().Be("López");
        result.Email.Should().Be("ana.lopez@test.com");
        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<Cliente>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_EmailEnUsoPorOtroCliente_LanzaInvalidOperationException()
    {
        // Arrange — el email pertenece a un cliente distinto (excludeId = 3, pero existe con otro id)
        var clienteExistente = ClienteEjemplo(id: 3);
        var dto = new ClienteRequest("Ana", "García", "70099988", "otro@test.com");

        _repoMock.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(clienteExistente);
        _repoMock.Setup(r => r.EmailExistsAsync(dto.Email, 3)).ReturnsAsync(true); // <-- email tomado

        // Act
        Func<Task> act = () => _sut.UpdateAsync(3, dto);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*email ya está en uso*");
    }

    [Fact]
    public async Task UpdateAsync_ClienteInexistente_LanzaKeyNotFoundException()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync(55)).ReturnsAsync((Cliente?)null);
        var dto = new ClienteRequest("X", "Y", "70000000", "x@test.com");

        // Act
        Func<Task> act = () => _sut.UpdateAsync(55, dto);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*55*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeleteAsync  (soft delete)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ClienteExistente_LlamaRepoDelete()
    {
        // Arrange
        var cliente = ClienteEjemplo(id: 2);
        _repoMock.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(cliente);
        _repoMock.Setup(r => r.DeleteAsync(cliente)).Returns(Task.CompletedTask);

        // Act
        await _sut.DeleteAsync(2);

        // Assert — verifica que el repositorio recibió la llamada de borrado
        _repoMock.Verify(r => r.DeleteAsync(It.Is<Cliente>(c => c.Id == 2)), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ClienteInexistente_LanzaKeyNotFoundException()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync(77)).ReturnsAsync((Cliente?)null);

        // Act
        Func<Task> act = () => _sut.DeleteAsync(77);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*77*");
    }
}
