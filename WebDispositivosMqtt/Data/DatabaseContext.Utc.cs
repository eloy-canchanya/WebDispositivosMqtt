using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WebDispositivosMqtt.Data;

// Escrito a mano: EF Core Power Tools regenera DatabaseContext.cs, pero no este
// archivo. No borrarlo al regenerar.
//
// En la BD todo DATETIME2 está en UTC (contratos/README.md), pero SQL Server no
// guarda la zona y EF devuelve DateTime con Kind = Unspecified. System.Text.Json
// lo serializa sin la Z, y el navegador lo interpreta como hora local: queda
// corrido 5 horas sin ningún error. Acá se marca como UTC todo lo que se lee.
public partial class DatabaseContext
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    // Al escribir no se toca el valor: el código ya usa DateTime.UtcNow.
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
