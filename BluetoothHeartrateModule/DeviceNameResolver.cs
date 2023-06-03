using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    internal class DeviceNameResolver
    {
        internal static async Task<string> GetDeviceNameAsync(BluetoothLEAdvertisement advertisement, ulong bluetoothAddress)
        {
            var advertisementDeviceName = advertisement.LocalName;
            if (advertisementDeviceName != string.Empty)
            {
                return advertisementDeviceName;
            }

            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
            // Get the device name using the DeviceInformation class
            DeviceInformation deviceInfo = await DeviceInformation.CreateFromIdAsync(device.DeviceId);
            var deviceName = deviceInfo.Name;
            if (deviceName == string.Empty)
            {
                var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.GenericAccess);
                if (services.Services.Count > 0)
                {
                    var service = services.Services[0];
                    var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.GapDeviceName);
                    if (characteristics.Characteristics.Count > 0)
                    {
                        var characteristic = characteristics.Characteristics[0];
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        deviceName = DataReader.FromBuffer(value.Value).ReadString(value.Value.Length);
                    }
                }
                foreach (var service in services.Services)
                    service.Dispose();
            }
            return GetDeviceNameOrFallback(deviceName);
        }

        private static string GetDeviceNameOrFallback(string? deviceName)
        {
            return deviceName == string.Empty || deviceName == null ? "Unknown" : deviceName;
        }
    }
}
