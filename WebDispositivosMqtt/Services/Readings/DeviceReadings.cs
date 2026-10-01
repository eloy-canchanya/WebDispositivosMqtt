using System.Text.RegularExpressions;

namespace WebDispositivosMqtt.Services.Readings;

// Lecturas que mide un equipo, con el nombre de su subtipo en tel/{subtipo}.
// El equipo las anuncia en devices/{mac}/info y quedan en Devices.Readings
// como lista separada por comas. Ver contratos/mqtt-topics.md.
public static partial class DeviceReadings
{
    public const string Level = "level";
    public const string Battery = "battery";

    // Equipo que nunca anunció (firmware anterior a devices/{mac}/info): se
    // asume que mide todo, como hacía el clorador antes de "bat off".
    public static readonly IReadOnlyList<string> Default = [Level, Battery];

    public const int MaxStoredLength = 100; // Devices.Readings VARCHAR(100)

    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$")]
    private static partial Regex NameRegex();

    public static bool IsValidName(string? name) => name is not null && NameRegex().IsMatch(name);

    public static IReadOnlyList<string> Parse(string? stored)
        => stored is null
            ? Default
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Format(IEnumerable<string> readings) => string.Join(',', readings.Distinct());
}
