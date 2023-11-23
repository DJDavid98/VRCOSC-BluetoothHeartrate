using VRCOSC.Game.SDK.Modules.Heartrate;
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

        public override async Task<bool> Initialise()
        {
            module.LogDebug("Initializing BluetoothHeartrateProvider");
            if (module.GetDeviceMacSetting() == string.Empty)
            {
                Log("Device MAC setting is not set, module will log discovered devices");
            }

            if (module.watcher == null)
            {
                Log("Watcher is not defined");
                return false;
            }

            module.LogDebug("Registering watcher received handler");
            module.watcher.Received += Watcher_Received;
            module.LogDebug("Starting watcher");
            module.StartWatcher();
            return true;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public override async Task Teardown()
#pragma warning restore CS1998
        {
            Reset();
        }

        private void Reset()
        {
            module.LogDebug("Resetting provider");
            if (module.watcher != null)
            {
                module.LogDebug("Unregistering watcher received handler");
                module.watcher.Received -= Watcher_Received;
            }
            module.LogDebug("Clearing device names");
            deviceNames.Clear();
            module.LogDebug("Clearing missing characteristics");
            missingCharacteristicDevices.Clear();
            ResetDevice();
            processingData = false;
        }

        private async void ResetDevice()
        {
            module.LogDebug("Resetting device data");
            if (heartRateService != null)
            {
                module.LogDebug("Resetting heartRateService");
                try
                {
                    heartRateService.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                    module.LogDebug("heartRateService already disposed");
                }
                heartRateService = null;
            }
            if (heartRateCharacteristic != null)
            {
                module.LogDebug("Resetting heartRateCharacteristic");
                try
                {
                    heartRateCharacteristic.ValueChanged -= HeartRateCharacteristic_ValueChanged;
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                    module.LogDebug("heartRateCharacteristic already disposed");
                }
                heartRateCharacteristic = null;
            }
            if (currentDevice != null)
            {
                module.LogDebug("Resetting currentDevice");
                try
                {
                    currentDevice.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                    currentDevice.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                    module.LogDebug("currentDevice already disposed");
                }
                currentDevice = null;
            }
        }


        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var advertisementId = Guid.NewGuid().ToString();
            // We need a prefix to follow the logs as advertisements can come in pretty rapidly
            var logPrefix = $"[{advertisementId}]";
            module.LogDebug($"{logPrefix} Watcher received advertisement");
            var advertisementMac = Converter.FormatAsMac(args.BluetoothAddress);
            module.LogDebug($"{logPrefix} advertisementMac = {advertisementMac}");
            var deviceMacSetting = module.GetDeviceMacSetting();
            module.LogDebug($"{logPrefix} deviceMacSetting = {deviceMacSetting}");
            var isConfiguredDevice = advertisementMac == deviceMacSetting;
            module.LogDebug($"{logPrefix} isConfiguredDevice = {isConfiguredDevice}");
            if (deviceMacSetting == string.Empty || isConfiguredDevice)
            {
                var deviceNamesValue = deviceNames.GetValueOrDefault(advertisementMac, null);
                module.LogDebug($"{logPrefix} Cached device name: {deviceNamesValue}");
                if (deviceNamesValue == null)
                {
                    module.LogDebug($"{logPrefix} Creating device name resolver");
                    var dnr = new DeviceNameResolver(module);
                    module.LogDebug($"{logPrefix} Resolving device name");
                    var resolvedDeviceName = await dnr.GetDeviceNameAsync(args.Advertisement, args.BluetoothAddress);
                    module.LogDebug($"{logPrefix} Caching device name");
                    deviceNames[advertisementMac] = resolvedDeviceName;
                    if (!isConfiguredDevice)
                    {
                        Log($"Discovered device: {resolvedDeviceName} (MAC: {advertisementMac})");
                    }
                }
            }

            if (!isConfiguredDevice)
            {
                // Not the droid we're looking for
                module.LogDebug($"{logPrefix} Not the configured device, stop further advertisement processing");
                return;
            }
            if (heartRateCharacteristic != null)
            {
                // Characteristic already found
                module.LogDebug($"{logPrefix} heartRateCharacteristic already found, stop further advertisement processing");
                return;
            }

            if (processingData)
            {
                module.LogDebug($"{logPrefix} Currently another advertisement is being processed, ignore this advertisement");
                return;
            }
            processingData = true;
            module.LogDebug($"{logPrefix} Begin processing advertisement data");
            try
            {
                if (currentDevice == null)
                {
                    module.LogDebug($"{logPrefix} Setting currrent device");
                    currentDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                    var currentDeviceName = deviceNames[advertisementMac] ?? "Unknown";
                    Log($"Found device named {currentDeviceName} for MAC {advertisementMac}");
                    module.SetDeviceName(currentDeviceName);
                    module.LogDebug($"{logPrefix} Register connection status change handler");
                    currentDevice.ConnectionStatusChanged += Device_ConnectionStatusChanged;
                }
                else
                {
                    module.LogDebug($"{logPrefix} Current device already set");
                }

                var missungUnknown = !missingCharacteristicDevices.Contains(deviceMacSetting);
                module.LogDebug($"{logPrefix} missungUnknown = {missungUnknown}");
                module.LogDebug($"{logPrefix} Finding HeratRate service");
                var services = await currentDevice.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached);
                if (services.Services.Count > 0)
                {
                    module.LogDebug($"{logPrefix} Queueuing all found services for cleanup");
                    IEnumerable<GattDeviceService> cleanupServices = services.Services;
                    var firstService = cleanupServices.First();
                    if (missungUnknown)
                    {
                        Log("Found heartrate service");
                    }
                    var characteristics = await firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached);
                    module.LogDebug($"{logPrefix} Finding HeartRateMeasurement characteristic");
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
                                    module.LogDebug($"{logPrefix} Unregistering watcher recived handler");
                                    module.watcher.Received -= Watcher_Received;
                                }
                                module.LogDebug($"{logPrefix} Invoking OnConnected action");
                                OnConnected?.Invoke();
                                Log("Connection successful");
                                module.LogDebug($"{logPrefix} Stopping watcher");
                                module.StopWatcher();
                                module.LogDebug($"{logPrefix} Excluding first service from cleanup");
                                cleanupServices = services.Services.Skip(1);
                            }
                            else
                            {
                                Log($"Failed to enable heart rate notifications. Status: {status}");
                            }
                        }
                    }
                    else
                    {
                        module.LogDebug($"{logPrefix} No characteristics found");
                    }

                    if (cleanupServices.Any())
                    {
                        module.LogDebug($"{logPrefix} Cleaning up services queued for cleanup");
                        foreach (var service in cleanupServices)
                            service.Dispose();
                    }
                }
                else
                {
                    module.LogDebug($"{logPrefix} No services found");
                }

                if (heartRateCharacteristic == null && missungUnknown)
                {
                    module.LogDebug($"{logPrefix} Adding device to missing characteristic list");
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
                module.LogDebug($"{logPrefix} Stopped processing advertisement");
            }
        }

        private void HeartRateCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            module.LogDebug("HeartRateCharacteristic_ValueChanged");
            var data = new byte[args.CharacteristicValue.Length];
            module.LogDebug("Reading new hartrate value into buffer");
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

            var updateData = data[1];
            module.LogDebug($"Invoking OnHeartrateUpdate action with data {updateData}");
            OnHeartrateUpdate?.Invoke(updateData);
        }

        private async void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            module.LogDebug("Device_ConnectionStatusChanged");
            module.LogDebug($"sender.ConnectionStatus = {sender.ConnectionStatus}");
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Log("Current device disconected");
                module.LogDebug($"Invoking OnDisconnected action");
                OnDisconnected?.Invoke();
            }
        }
    }
}
