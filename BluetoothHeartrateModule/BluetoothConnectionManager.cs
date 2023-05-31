using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    internal class BluetoothConnectionManager
    {
        private BluetoothLEAdvertisementWatcher? watcher;
        private Dictionary<string, string?> potentialDevices = new();
        private GattCharacteristic? heartRateCharacteristic;
        private HashSet<string> missingCharacteristicDevices = new();
        private bool isConnected = false;
        private bool processingData = false;
        BluetoothLEDevice? currentDevice;
        private readonly BluetoothHeartrateModule module;
        public bool IsConnected => isConnected;

        public BluetoothConnectionManager(BluetoothHeartrateModule module)
        {
            this.module = module;
        }

        internal void Reset()
        {
            processingData = false;
            potentialDevices.Clear();
            missingCharacteristicDevices.Clear();
            ResetDevice();
            ResetWatcher();
        }

        internal void StartWatcher()
        {
            if (watcher == null)
            {
                AttemptConnection();
            }
            watcher?.Start();
            module.Log("Watching for devices");
        }

        internal void StopWatcher()
        {
            watcher?.Stop();
        }

        private void ResetDevice()
        {
            ResetCharacteristic();
            if (currentDevice != null)
            {
                currentDevice.Dispose();
                currentDevice = null;
            }
            module.ChangeStateTo(BluetoothHeartrateModule.BluetoothHeartrateState.Disconnected);
            isConnected = false;
        }

        private void ResetCharacteristic()
        {
            if (heartRateCharacteristic != null)
            {
                heartRateCharacteristic.ValueChanged -= HeartRateCharacteristic_ValueChanged;
                heartRateCharacteristic = null;
            }
        }

        private void ResetWatcher()
        {
            if (watcher != null)
            {
                watcher.Received -= Watcher_Received;
                watcher.Stopped -= Watcher_Stopped;
                watcher.Stop();
                watcher = null;
            }
        }

        private void AttemptConnection()
        {
            if (module.GetDeviceMacSetting() == string.Empty)
            {
                module.Log("Device MAC setting is not set, module will log discovered devices");
            }

            ResetWatcher();
            watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            watcher.Received += Watcher_Received;
            watcher.Stopped += Watcher_Stopped;
        }

        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {

            var advertisementMac = Converter.FormatAsMac(args.BluetoothAddress);
            var deviceMacSetting = module.GetDeviceMacSetting();
            if (deviceMacSetting == string.Empty)
            {
                var potentialDevicesValue = potentialDevices.GetValueOrDefault(advertisementMac, null);
                if (potentialDevicesValue == null)
                {
                    var advertisementDeviceName = await DeviceNameResolver.GetDeviceNameAsync(args.Advertisement, args.BluetoothAddress);
                    potentialDevices[advertisementMac] = advertisementDeviceName;
                    module.Log($"Discovered device: {advertisementDeviceName} (MAC: {advertisementMac})");
                }
                return;
            }

            if (advertisementMac != deviceMacSetting)
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
            module.ChangeStateTo(BluetoothHeartrateModule.BluetoothHeartrateState.Connecting);
            try
            {
                if (currentDevice == null)
                {
                    currentDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                    module.Log($"Found device for MAC {advertisementMac}");
                }

                var missungUnknown = !missingCharacteristicDevices.Contains(deviceMacSetting);
                var services = await currentDevice.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached);
                if (services.Services.Count > 0)
                {
                    var firstService = services.Services[0];
                    if (missungUnknown)
                    {
                        module.Log("Found heartrate service");
                    }
                    var characteristics = await firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached);
                    if (characteristics.Characteristics.Count > 0)
                    {
                        if (heartRateCharacteristic == null)
                        {
                            heartRateCharacteristic = characteristics.Characteristics[0];
                            module.Log("Found heartrate measurement characteristic");

                            heartRateCharacteristic.ValueChanged += HeartRateCharacteristic_ValueChanged;
                            module.Log("Registered heartrate characteristic value change handler");

                            // Enable notifications for heart rate measurements
                            module.Log("Writing client characteristic configuration descriptor");
                            var status = await heartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            isConnected = status == GattCommunicationStatus.Success;
                            if (isConnected)
                            {
                                module.ChangeStateTo(BluetoothHeartrateModule.BluetoothHeartrateState.Connected);
                                module.Log("Connection successful");
                            }
                            else
                            {
                                module.ChangeStateTo(BluetoothHeartrateModule.BluetoothHeartrateState.Disconnected);
                                module.Log($"Failed to enable heart rate notifications. Status: {status}");
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
                module.Log($"Failed to connect: {ex.Message}");
                module.ChangeStateTo(BluetoothHeartrateModule.BluetoothHeartrateState.Disconnected);
                isConnected = false;
                ResetDevice();
            }
            finally
            {
                processingData = false;
                module.ChangeStateTo(BluetoothHeartrateModule.BluetoothHeartrateState.Default);
            }
        }

        private void HeartRateCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

            module.UpdateHeartrate(data[1]);
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            ResetDevice();
            module.SendParameters();
            module.Log("Watcher stopped");
        }
    }
}
