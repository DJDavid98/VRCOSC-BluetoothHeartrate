using VRCOSC.Game.Modules.Bases.Heartrate;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    public class BluetoothHeartrateProvider : HeartrateProvider
    {
        private Dictionary<string, string?> deviceNames = new();
        private GattDeviceService? heartRateService;
        private GattCharacteristic? heartRateCharacteristic;
        private HashSet<string> missingCharacteristicDevices = new();
        private bool processingData = false;
        BluetoothLEDevice? currentDevice;
        private readonly BluetoothHeartrateModule module;
        public override bool IsConnected => currentDevice != null && heartRateCharacteristic != null && currentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;

        public BluetoothHeartrateProvider(BluetoothHeartrateModule module)
        {
            this.module = module;

        }

        public override void Initialise()
        {
            if (module.GetDeviceMacSetting() == string.Empty)
            {
                Log("Device MAC setting is not set, module will log discovered devices");
            }

            if (module.watcher == null)
            {
                Log("Watcher is not defined");
                return;
            }

            module.watcher.Received += Watcher_Received;
            module.StartWatcher();
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public override async Task Teardown()
#pragma warning restore CS1998
        {
            Reset();
        }

        private void Reset()
        {

            if (module.watcher != null)
            {
                module.watcher.Received -= Watcher_Received;
            }
            deviceNames.Clear();
            missingCharacteristicDevices.Clear();
            ResetDevice();
            processingData = false;
        }

        private async void ResetDevice()
        {
            if (heartRateService != null)
            {
                try
                {
                    heartRateService.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                }
                heartRateService = null;
            }
            if (heartRateCharacteristic != null)
            {
                try
                {
                    heartRateCharacteristic.ValueChanged -= HeartRateCharacteristic_ValueChanged;
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                }
                heartRateCharacteristic = null;
            }
            if (currentDevice != null)
            {
                try
                {
                    currentDevice.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                    currentDevice.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                }
                currentDevice = null;
            }
        }


        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {

            var advertisementMac = Converter.FormatAsMac(args.BluetoothAddress);
            var deviceMacSetting = module.GetDeviceMacSetting();
            var isConfiguredDevice = advertisementMac == deviceMacSetting;
            if (deviceMacSetting == string.Empty || isConfiguredDevice)
            {
                var deviceNamesValue = deviceNames.GetValueOrDefault(advertisementMac, null);
                if (deviceNamesValue == null)
                {
                    var dnr = new DeviceNameResolver(module);
                    var advertisementDeviceName = await dnr.GetDeviceNameAsync(args.Advertisement, args.BluetoothAddress);
                    deviceNames[advertisementMac] = advertisementDeviceName;
                    if (!isConfiguredDevice)
                    {
                        Log($"Discovered device: {advertisementDeviceName} (MAC: {advertisementMac})");
                    }
                }
                if (!isConfiguredDevice)
                {
                    return;
                }
            }

            if (!isConfiguredDevice)
            {
                // Not the droid we're looking for
                return;
            }
            if (heartRateCharacteristic != null)
            {
                // Characteristic already found
                return;
            }

            if (processingData) return;
            processingData = true;
            try
            {
                if (currentDevice == null)
                {
                    currentDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                    var currentDeviceName = deviceNames[advertisementMac] ?? "Unknown";
                    Log($"Found device named {currentDeviceName} for MAC {advertisementMac}");
                    module.SetDeviceName(currentDeviceName);
                    currentDevice.ConnectionStatusChanged += Device_ConnectionStatusChanged;
                }

                var missungUnknown = !missingCharacteristicDevices.Contains(deviceMacSetting);
                var services = await currentDevice.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached);
                if (services.Services.Count > 0)
                {
                    IEnumerable<GattDeviceService> cleanupServices = services.Services;
                    var firstService = cleanupServices.First();
                    if (missungUnknown)
                    {
                        Log("Found heartrate service");
                    }
                    var characteristics = await firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached);
                    if (characteristics.Characteristics.Count > 0)
                    {
                        if (heartRateCharacteristic == null)
                        {
                            heartRateService = firstService;
                            heartRateCharacteristic = characteristics.Characteristics[0];
                            Log("Found heartrate measurement characteristic");

                            heartRateCharacteristic.ValueChanged += HeartRateCharacteristic_ValueChanged;
                            Log("Registered heartrate characteristic value change handler");

                            // Enable notifications for heart rate measurements
                            Log("Writing client characteristic configuration descriptor");
                            var status = await heartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            if (status == GattCommunicationStatus.Success)
                            {
                                // Remove receive handler
                                if (module.watcher != null)
                                {
                                    module.watcher.Received -= Watcher_Received;
                                }
                                OnConnected?.Invoke();
                                Log("Connection successful");
                                module.StopWatcher();
                                cleanupServices = services.Services.Skip(1);
                            }
                            else
                            {
                                Log($"Failed to enable heart rate notifications. Status: {status}");
                            }
                        }
                    }

                    if (cleanupServices.Any())
                    {
                        foreach (var service in cleanupServices)
                            service.Dispose();
                    }
                }

                if (heartRateCharacteristic == null && missungUnknown)
                {
                    missingCharacteristicDevices.Add(deviceMacSetting);
                    throw new Exception("No heartrate characteristic found");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to connect: {ex.Message}");
                ResetDevice();
            }
            finally
            {
                processingData = false;
            }
        }

        private void HeartRateCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

            OnHeartrateUpdate?.Invoke(data[1]);
        }

        private async void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Log("Current device disconected");
                OnDisconnected?.Invoke();
            }
        }
    }
}
