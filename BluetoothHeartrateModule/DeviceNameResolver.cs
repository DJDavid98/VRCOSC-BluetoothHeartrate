using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    internal class DeviceNameResolver
    {
        private readonly BluetoothHeartrateModule module;
        private readonly AsyncHelper ah;

        public DeviceNameResolver(BluetoothHeartrateModule module, AsyncHelper ah)
        {
            this.module = module;
            this.ah = ah;
        }

        internal async Task<string> GetDeviceNameAsync(BluetoothLEAdvertisement advertisement, ulong bluetoothAddress)
        {
            module.LogDebug("Getting device name");
            var deviceName = string.Empty;
            try
            {
                var advertisementDeviceName = advertisement.LocalName;
                if (advertisementDeviceName != string.Empty)
                {
                    module.LogDebug($"Device name picked from advertisement: {advertisementDeviceName}");
                    return advertisementDeviceName;
                }

                module.LogDebug("Advertisement missing device name");
                module.LogDebug($"Getting device object with address {Converter.FormatAsMac(bluetoothAddress)}");
                using var device = await ah.WaitAsync(BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress), AsyncTask.GetDeviceFromAddress);
                DeviceInformation? deviceInfo = null;
                if (device != null)
                {
                    module.LogDebug($"Creating DeviceInfo class with device ID {device.DeviceId}");
                    // Get the device name using the DeviceInformation class
                    deviceInfo = await ah.WaitAsync(DeviceInformation.CreateFromIdAsync(device.DeviceId), AsyncTask.GetDeviceInfo);
                    if (deviceInfo != null)
                    {
                        deviceName = deviceInfo.Name;
                    }
                    if (deviceName == string.Empty)
                    {
                        module.LogDebug("Could not read device name from DeviceInfo class");
                        module.LogDebug("Attempting to find GenericAccess service");
                        var services = await ah.WaitAsync(device.GetGattServicesForUuidAsync(GattServiceUuids.GenericAccess), AsyncTask.GetGenericAccessService);
                        if (services != null)
                        {
                            if (services.Services.Count > 0)
                            {
                                var firstService = services.Services[0];
                                module.LogDebug("Attempting to read GapDeviceName characteristic from GenericAccess service");
                                var characteristics = await ah.WaitAsync(firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.GapDeviceName), AsyncTask.GetDeviceNameCharacteristic);
                                if (characteristics != null && characteristics.Characteristics.Count > 0)
                                {
                                    var characteristic = characteristics.Characteristics[0];
                                    module.LogDebug("Attempting to read device name from GapDeviceName characteristic");
                                    var value = await ah.WaitAsync(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), AsyncTask.ReadHeartRateValue);
                                    if (value != null)
                                    {
                                        deviceName = DataReader.FromBuffer(value.Value).ReadString(value.Value.Length);
                                        module.LogDebug($"Read device name from GapDeviceName characteristic ({deviceName})");
                                    }
                                    else
                                    {
                                        module.LogDebug($"Could not read device name from GapDeviceName characteristic");
                                    }
                                }
                            }
                            module.LogDebug($"Cleaning up all found services");
                            foreach (var service in services.Services)
                                service.Dispose();
                        }
                    }
                    else
                    {
                        module.LogDebug($"Device name received from DeviceInfo class ({deviceName})");
                    }
                }
                else
                {
                    module.LogDebug($"Could not get device object from address {Converter.FormatAsMac(bluetoothAddress)}");
                }
            }
            catch (Exception ex)
            {
                module.LogDebug($"Could not get device name for address {Converter.FormatAsMac(bluetoothAddress)}: {ex.Message}\n{ex.StackTrace}");
            }
            return GetDeviceNameOrFallback(deviceName);
        }

        private string GetDeviceNameOrFallback(string? deviceName)
        {
            if (deviceName == string.Empty || deviceName == null)
            {
                module.LogDebug($"Device name not found, using fallback value");
                return "Unknown";
            }
            return deviceName;
        }
    }
}
