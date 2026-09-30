using FluentAssertions;
using Moq;
using ReservaApp.API.DTOs;
using ReservaApp.API.Models;
using ReservaApp.API.Repositories;
using ReservaApp.API.Services;
using Xunit;

namespace ReservaApp.Tests.Unit;

/// <summary>
/// Tests unitarios para ReservaService.
/// Patrón: AAA (Arrange · Act · Assert)
/// Las dependencias (repositorios) se mockean con Moq — así probamos
/// SOLO la lógica del servicio, aislada de base de datos.
/// </summary>
public class ReservaServiceTests
{
    // ── Mocks de dependencias ──────────────────────────────────────────────────
    private readonly Mock<IReservaRepository>  _repoReservaMock  = new();
    private readonly Mock<IClienteRepository>  _repoClienteMock  = new();
    private readonly Mock<IServicioRepository> _repoServicioMock = new();

    // Sistema bajo prueba (SUT)
    private readonly IReservaService _sut;

    public ReservaServiceTests()
    {
        _sut = new ReservaService(
            _repoReservaMock.Object,
            _repoClienteMock.Object,
            _repoServicioMock.Object);
    }

    // ── Helpers (datos de prueba reutilizables) ──────────────────────────────
    private static Cliente ClienteEjemplo(int id = 1) => new()
    {
        Id = id, Nombre = "Juan", Apellido = "Pérez",
        Email = "juan@test.com", Telefono = "70012345"
    };

    private static Servicio ServicioEjemplo(int id = 1) => new()
    {
        Id = id, Nombre = "Corte de cabello",
        Descripcion = "Corte clásico", DuracionMin = 30, Precio = 50m
    };

    private static Reserva ReservaEjemplo(int id = 1, DateTime? fecha = null) => new()
    {
        Id = id,
        ClienteId = 1,
        ServicioId = 1,
        FechaHora = fecha ?? DateTime.UtcNow.AddDays(1),
        Estado = EstadoReserva.Pendiente,
        Cliente = ClienteEjemplo(),
        Servicio = ServicioEjemplo()
    };

    // ─────────────────────────────────────────────────────────────────────────
    // GetByIdAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReservaExistente_RetornaDto()
    {
        // Arrange
        var reserva = ReservaEjemplo(id: 42);
        _repoReservaMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(reserva);

        // Act
        var result = await _sut.GetByIdAsync(42);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.ClienteNombre.Should().Be("Juan Pérez");
        result.ServicioNombre.Should().Be("Corte de cabello");
        result.Estado.Should().Be("Pendiente");
    }

    [Fact]
    public async Task GetByIdAsync_ReservaInexistente_LanzaKeyNotFoundException()
    {
        // Arrange
        _repoReservaMock.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Reserva?)null);

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
    public async Task CreateAsync_DatosValidos_CreaReservaYRetornaDto()
    {
        // Arrange
        var fechaFutura = DateTime.UtcNow.AddDays(2);
        var dto = new ReservaRequest(ClienteId: 1, ServicioId: 1, FechaHora: fechaFutura, Notas: "Sin notas");

        _repoClienteMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ClienteEjemplo());
        _repoServicioMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ServicioEjemplo());
        _repoReservaMock.Setup(r => r.HayConflictoHorarioAsync(1, fechaFutura, null)).ReturnsAsync(false);
        _repoReservaMock.Setup(r => r.CreateAsync(It.IsAny<Reserva>()))
            .ReturnsAsync((Reserva r) => { r.Id = 10; return r; });

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.Id.Should().Be(10);
        result.FechaHora.Should().Be(fechaFutura);
        _repoReservaMock.Verify(r => r.CreateAsync(It.IsAny<Reserva>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_FechaPasada_LanzaInvalidOperationException()
    {
        // Arrange — fecha en el pasado
        var dto = new ReservaRequest(1, 1, DateTime.UtcNow.AddDays(-1), null);
        _repoClienteMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ClienteEjemplo());
        _repoServicioMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ServicioEjemplo());

        // Act
        Func<Task> act = () => _sut.CreateAsync(dto);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*fecha pasada*");
    }

    [Fact]
    public async Task CreateAsync_ConflictoHorario_LanzaInvalidOperationException()
    {
        // Arrange
        var fecha = DateTime.UtcNow.AddDays(1);
        var dto = new ReservaRequest(1, 1, fecha, null);

        _repoClienteMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ClienteEjemplo());
        _repoServicioMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ServicioEjemplo());
        _repoReservaMock.Setup(r => r.HayConflictoHorarioAsync(1, fecha, null)).ReturnsAsync(true); // <-- conflicto

        // Act
        Func<Task> act = () => _sut.CreateAsync(dto);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*horario*");
    }

    [Fact]
    public async Task CreateAsync_ClienteInexistente_LanzaKeyNotFoundException()
    {
        // Arrange
        var dto = new ReservaRequest(99, 1, DateTime.UtcNow.AddDays(1), null);
        _repoClienteMock.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Cliente?)null);

        // Act
        Func<Task> act = () => _sut.CreateAsync(dto);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Cliente*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CambiarEstadoAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Confirmada", EstadoReserva.Confirmada)]
    [InlineData("Cancelada",  EstadoReserva.Cancelada)]
    [InlineData("Completada", EstadoReserva.Completada)]
    public async Task CambiarEstadoAsync_EstadoValido_ActualizaEstado(
        string estadoStr, EstadoReserva estadoEsperado)
    {
        // Arrange
        var reserva = ReservaEjemplo();
        _repoReservaMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(reserva);
        _repoReservaMock.Setup(r => r.UpdateAsync(It.IsAny<Reserva>()))
            .ReturnsAsync((Reserva r) => r);

        // Act
        var result = await _sut.CambiarEstadoAsync(1, estadoStr);

        // Assert
        result.Estado.Should().Be(estadoEsperado.ToString());
        _repoReservaMock.Verify(r => r.UpdateAsync(It.Is<Reserva>(x => x.Estado == estadoEsperado)), Times.Once);
    }

    [Fact]
    public async Task CambiarEstadoAsync_EstadoInvalido_LanzaArgumentException()
    {
        // Arrange
        var reserva = ReservaEjemplo();
        _repoReservaMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(reserva);

        // Act
        Func<Task> act = () => _sut.CambiarEstadoAsync(1, "EstadoQueNoExiste");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*EstadoQueNoExiste*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UpdateAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ReservaCancelada_LanzaInvalidOperationException()
    {
        // Arrange — reserva ya cancelada, no se puede modificar
        var reserva = ReservaEjemplo();
        reserva.Estado = EstadoReserva.Cancelada;
        _repoReservaMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(reserva);

        var dto = new ReservaRequest(1, 1, DateTime.UtcNow.AddDays(1), null);

        // Act
        Func<Task> act = () => _sut.UpdateAsync(1, dto);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cancelada*");
    }
}
