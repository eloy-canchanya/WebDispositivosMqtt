namespace WebDispositivosMqtt.Services.Provisioning;

public interface IDeviceProvisioningService
{
    /// <summary>
    /// Genera un password aleatorio, lo encripta y devuelve ambos.
    /// Guardar el valor encriptado en DB; el plain solo se usa para enviarlo al ESP32.
    /// </summary>
    string GenerateCredential(out string plainPassword);

    /// <summary>
    /// Desencripta el valor almacenado en DB y devuelve el password en claro.
    /// </summary>
    string GetPlainPassword(string protectedCredential);
}
