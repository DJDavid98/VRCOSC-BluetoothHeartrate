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
        private GattCharacteristic? heartRateCharacteristic;
        private HashSet<string> missingCharacteristicDevices = new();
        private bool processingData = false;
        BluetoothLEDevice? currentDevice;
        private readonly BluetoothHeartrateModule module;
        public override bool IsConnected => currentDevice != null && heartRateCharacteristic != null;

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
            module.watcher.Stopped += Watcher_Stopped;
            switch (module.watcher.Status)
            {
                case BluetoothLEAdvertisementWatcherStatus.Stopped:
                case BluetoothLEAdvertisementWatcherStatus.Created:
                    module.watcher.Start();
                    Log("Watching for devices");
                    break;
            }
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
                module.watcher.Stopped -= Watcher_Stopped;
            }
            deviceNames.Clear();
            missingCharacteristicDevices.Clear();
            ResetDevice();
            processingData = false;
        }

        private void ResetDevice()
        {
            if (heartRateCharacteristic != null)
            {
                heartRateCharacteristic.ValueChanged -= HeartRateCharacteristic_ValueChanged;
                heartRateCharacteristic = null;
            }
            if (currentDevice != null)
            {
                currentDevice.Dispose();
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
                    var advertisementDeviceName = await DeviceNameResolver.GetDeviceNameAsync(args.Advertisement, args.BluetoothAddress);
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
                }

                var missungUnknown = !missingCharacteristicDevices.Contains(deviceMacSetting);
                var services = await currentDevice.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached);
                if (services.Services.Count > 0)
                {
                    var firstService = services.Services[0];
                    if (missungUnknown)
                    {
                        Log("Found heartrate service");
                    }
                    var characteristics = await firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached);
                    if (characteristics.Characteristics.Count > 0)
                    {
                        if (heartRateCharacteristic == null)
                        {
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
                            }
                            else
                            {
                                Log($"Failed to enable heart rate notifications. Status: {status}");
                            }
                        }
                    }
                    else
                    {
                        foreach (var service in services.Services)
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

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Log("Watcher stopped");
            OnDisconnected?.Invoke();
        }
    }
}
