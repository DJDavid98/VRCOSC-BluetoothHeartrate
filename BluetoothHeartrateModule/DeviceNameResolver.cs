using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    internal class DeviceNameResolver
    {
        private readonly BluetoothHeartrateModule _module;
        private readonly AsyncHelper _ah;

        public DeviceNameResolver(BluetoothHeartrateModule module)
        {
            this._module = module;
            this._ah = module.Ah;
        }

        void LogDebug(string macAddress, string text)
        {
            _module.LogDebug($"[MAC:{macAddress}] {text}");
        }

        internal async Task<string> GetDeviceNameAsync(BluetoothLEAdvertisement advertisement, ulong bluetoothAddress)
        {

            var macAddress = Converter.FormatAsMac(bluetoothAddress);
            LogDebug(macAddress, "Getting device name");
            var deviceName = string.Empty;
            try
            {
                var advertisementDeviceName = advertisement.LocalName;
                if (advertisementDeviceName != string.Empty)
                {
                    LogDebug(macAddress, $"Device name picked from advertisement: {advertisementDeviceName}");
                    return advertisementDeviceName;
                }

                LogDebug(macAddress, "Advertisement missing device name, getting device object");
                using var device = await _ah.WaitAsync(BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress), AsyncTask.GetDeviceFromAddress);
                DeviceInformation? deviceInfo = null;
                if (device != null)
                {
                    LogDebug(macAddress, $"Creating DeviceInfo class with device ID {device.DeviceId}");
                    // Get the device name using the DeviceInformation class
                    deviceInfo = await _ah.WaitAsync(DeviceInformation.CreateFromIdAsync(device.DeviceId), AsyncTask.GetDeviceInfo);
                    if (deviceInfo != null)
                    {
                        deviceName = deviceInfo.Name;
                    }
                    if (deviceName == string.Empty)
                    {
                        LogDebug(macAddress, "Could not read device name from DeviceInfo class, attempting to find GenericAccess service");
                        var services = await _ah.WaitAsync(device.GetGattServicesForUuidAsync(GattServiceUuids.GenericAccess), AsyncTask.GetGenericAccessService);
                        if (services != null)
                        {
                            if (services.Services.Count > 0)
                            {
                                var firstService = services.Services[0];
                                LogDebug(macAddress, "Attempting to read GapDeviceName characteristic from GenericAccess service");
                                var characteristics = await _ah.WaitAsync(firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.GapDeviceName), AsyncTask.GetDeviceNameCharacteristic);
                                if (characteristics != null && characteristics.Characteristics.Count > 0)
                                {
                                    var characteristic = characteristics.Characteristics[0];
                                    LogDebug(macAddress, "Attempting to read device name from GapDeviceName characteristic");
                                    var value = await _ah.WaitAsync(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), AsyncTask.ReadHeartRateValue);
                                    if (value != null)
                                    {
                                        deviceName = DataReader.FromBuffer(value.Value).ReadString(value.Value.Length);
                                        LogDebug(macAddress, $"Read device name from GapDeviceName characteristic ({deviceName})");
                                    }
                                    else
                                    {
                                        LogDebug(macAddress, $"Could not read device name from GapDeviceName characteristic");
                                    }
                                }
                            }
                            LogDebug(macAddress, $"Cleaning up all found services");
                            foreach (var service in services.Services)
                                service.Dispose();
                        }
                    }
                    else
                    {
                        LogDebug(macAddress, $"Device name received from DeviceInfo class ({deviceName})");
                    }
                }
                else
                {
                    LogDebug(macAddress, $"Could not get device object from address {Converter.FormatAsMac(bluetoothAddress)}");
                }
            }
            catch (Exception ex)
            {
                LogDebug(macAddress, $"Could not get device name for address {Converter.FormatAsMac(bluetoothAddress)}: {ex.Message}\n{ex.StackTrace}");
            }
            return GetDeviceNameOrFallback(deviceName, macAddress);
        }

        private string GetDeviceNameOrFallback(string? deviceName, string macAddress)
        {
            if (deviceName == string.Empty || deviceName == null)
            {
                LogDebug(macAddress, $"Device name not found");
                return string.Empty;
            }
            return deviceName;
        }
    }
}
