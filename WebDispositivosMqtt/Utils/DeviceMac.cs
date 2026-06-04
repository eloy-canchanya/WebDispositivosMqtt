namespace WebDispositivosMqtt.Utils;

public static class DeviceMac
{
    // 12 lowercase hex chars, no separators: e.g. a4b1c2d3e4f5
    public static bool IsValid(string? mac) =>
        mac is { Length: 12 } && mac.All(char.IsAsciiHexDigitLower);
}
