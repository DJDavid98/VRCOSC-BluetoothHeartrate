using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    internal class DeviceNameResolver
    {
        BluetoothHeartrateModule module;

        public DeviceNameResolver(BluetoothHeartrateModule module)
        {
            this.module = module;
        }

        internal async Task<string> GetDeviceNameAsync(BluetoothLEAdvertisement advertisement, ulong bluetoothAddress)
        {
            module.Log("Getting device name");
            var deviceName = string.Empty;
            //try
            //{
                var advertisementDeviceName = advertisement.LocalName;
                if (advertisementDeviceName != string.Empty)
                {
                    module.Log($"Device name picked from advertisement: {advertisementDeviceName}");
                    return advertisementDeviceName;
                }

                module.Log("Advertisement missing device name");
                module.Log($"Getting device object with address {Converter.FormatAsMac(bluetoothAddress)}");
                using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                module.Log($"Creating DeviceInfo class with device ID {device.DeviceId}");
                // Get the device name using the DeviceInformation class
                DeviceInformation deviceInfo = await DeviceInformation.CreateFromIdAsync(device.DeviceId);
                deviceName = deviceInfo.Name;
                if (deviceName == string.Empty)
                {
                    module.Log("Cound lot read device name from DeviceInfo class");
                    module.Log("Attempting to find GenericAccess service");
                    var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.GenericAccess);
                    if (services.Services.Count > 0)
                    {
                        var firstService = services.Services.First();
                        module.Log("Attempting to read GapDeviceName characteristic from GenericAccess service");
                        var characteristics = await firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.GapDeviceName);
                        if (characteristics.Characteristics.Count > 0)
                        {
                            var characteristic = characteristics.Characteristics[0];
                            module.Log("Attempting to read device name from GapDeviceName characteristic");
                            var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (value != null)
                            {
                                deviceName = DataReader.FromBuffer(value.Value).ReadString(value.Value.Length);
                                module.Log($"Read device name from GapDeviceName characteristic ({deviceName})");
                            }
                            else
                            {
                                module.Log($"Could not read device name from GapDeviceName characteristic");
                            }
                        }
                    }
                    module.Log($"Cleaning up all found services");
                    foreach (var service in services.Services)
                        service.Dispose();
                }
                else
                {
                    module.Log($"Device name received from DeviceInfo class ({deviceName})");
                }
            //}
            //catch (Exception ex)
            //{
            //    module.Log($"Could not get device name for address {Converter.FormatAsMac(bluetoothAddress)}: {ex.Message}\n{ex.StackTrace}");
            // }
            return GetDeviceNameOrFallback(deviceName);
        }

        private string GetDeviceNameOrFallback(string? deviceName)
        {
            if (deviceName == string.Empty || deviceName == null)
            {
                module.Log($"Device name not found, using fallback value");
                return "Unknown";
            }
            return deviceName;
        }
    }
}
