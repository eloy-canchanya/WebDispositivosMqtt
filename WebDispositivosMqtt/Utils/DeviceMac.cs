namespace WebDispositivosMqtt.Utils;

public static class DeviceMac
{
    /// <summary>
    /// Validates that a MAC is exactly 12 uppercase hex characters with no separators.
    /// </summary>
    public static bool IsValid(string? mac) =>
        mac is { Length: 12 } && mac.All(char.IsAsciiHexDigitUpper);
}
