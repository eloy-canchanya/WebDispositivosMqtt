namespace WebDispositivosMqtt.Services.Dynsec;

public enum DynsecClientStatus { Enabled, Disabled, NotFound, Error }

public record DynsecClientInfo(
    DynsecClientStatus Status,
    string[] Roles,
    string? ErrorMessage = null);

public interface IDynsecService
{
    Task<bool> EnsureDeviceAsync(string macAddress, string plainPassword);
    Task<bool> SetDevicePasswordAsync(string macAddress, string plainPassword);
    Task DisableDeviceAsync(string macAddress);
    Task EnableDeviceAsync(string macAddress);
    Task<DynsecClientInfo> GetClientStatusAsync(string macAddress);
}
