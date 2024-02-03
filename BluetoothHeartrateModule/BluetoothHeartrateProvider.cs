using System.Reflection.PortableExecutable;
using VRCOSC.SDK.Modules.Heartrate;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Zeroconf;

namespace BluetoothHeartrateModule
{
    public class BluetoothHeartrateProvider : HeartrateProvider
    {
        private readonly Dictionary<string, string?> deviceNames = new();
        private GattDeviceService? heartRateService;
        private GattCharacteristic? heartRateCharacteristic;
        private readonly HashSet<string> missingCharacteristicDevices = new();
        private CancellationTokenSource watcherStopper = new();
        private string? processingAdvertisementId;
        private BluetoothLEDevice? currentDevice;
        private readonly BluetoothHeartrateModule module;
        private readonly AsyncHelper ah;
        public override bool IsConnected => currentDevice != null && heartRateCharacteristic != null && currentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;

        public BluetoothHeartrateProvider(BluetoothHeartrateModule module, AsyncHelper ah)
        {
            this.module = module;
            this.ah = ah;
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
            watcherStopper = new();
            module.LogDebug("Generated new watcherStopper");
            var startResult = await module.StartWatcher();
            if (!startResult)
            {
                OnDisconnected?.Invoke();
            }
            return startResult;
        }

        public override Task Teardown()
        {
            Reset();

            return Task.CompletedTask;
        }

        private void Reset()
        {
            module.LogDebug("Resetting provider");
            watcherStopper.Cancel();
            module.LogDebug("Cancelled watcherStopper");
            if (module.watcher != null)
            {
                module.LogDebug("Unregistering watcher received handler");
                module.watcher.Received -= Watcher_Received;
            }
            module.LogDebug("Clearing device names");
            deviceNames.Clear();
            module.LogDebug("Clearing missing characteristics");
            missingCharacteristicDevices.Clear();
            ResetDeviceData();
            module.LogDebug("Clearing processingAdvertisementId");
            processingAdvertisementId = null;
        }

        private void ResetHeartRateService()
        {
            if (heartRateService != null)
            {
                module.LogDebug("Resetting heartRateService");
                try
                {
                    module.LogDebug("Disposing of heartRateService");
                    heartRateService.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                    module.LogDebug("heartRateService already disposed");
                }
                heartRateService = null;
                module.LogDebug("heartRateService has been reset");
            }
        }

        private void ResetHeartRateCharacteristic()
        {
            if (heartRateCharacteristic != null)
            {
                module.LogDebug("Resetting heartRateCharacteristic");
                try
                {
                    module.LogDebug("Unregistering ValueChanged handler");
                    heartRateCharacteristic.ValueChanged -= HeartRateCharacteristic_ValueChanged;
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                    module.LogDebug("heartRateCharacteristic already disposed");
                }
                heartRateCharacteristic = null;
                module.LogDebug("heartRateCharacteristic has been reset");
            }
        }
        
        private void ResetCurrentDevice()
        {
            if (currentDevice != null)
            {
                module.LogDebug("Resetting currentDevice");
                try
                {
                    module.LogDebug("Unregistering ConnectionStatusChanged handler");
                    currentDevice.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
                    module.LogDebug("Disposing of currentDevice");
                    currentDevice.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                    module.LogDebug("currentDevice already disposed");
                }
                currentDevice = null;
                module.LogDebug("currentDevice has been reset");
            }
        }

        private void ResetDeviceData()
        {
            module.LogDebug("Resetting device data");
            ResetHeartRateService();
            ResetHeartRateCharacteristic();
            ResetCurrentDevice();
            module.LogDebug("Device data has been reset");
        }


        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var advertisementId = Guid.NewGuid().ToString();
            // We need a prefix to follow the logs as advertisements can come in pretty rapidly
            var logPrefix = $"[{advertisementId}]";
            if (processingAdvertisementId != null)
            {
                module.LogDebug($"{logPrefix} Currently advertisement {processingAdvertisementId} is being processed, ignore this advertisement");
                return;
            }
            var advertisementMac = Converter.FormatAsMac(args.BluetoothAddress);
            module.LogDebug($"{logPrefix} Watcher received advertisement with MAC {advertisementMac}");

            var deviceMacSetting = module.GetDeviceMacSetting();
            var isDeviceConfigured = deviceMacSetting != string.Empty;
            var isConfiguredDevice = advertisementMac == deviceMacSetting;
            if (isDeviceConfigured && !isConfiguredDevice)
            {
                // Not the droid we're looking for
                module.LogDebug($"{logPrefix} Not the configured device, stop further advertisement processing");
                return;
            }
            processingAdvertisementId = advertisementId;
            var cancelMessage = $"{logPrefix} Watcher cancelled, discarding advertisement";

            if (!isDeviceConfigured || isConfiguredDevice)
            {
                var deviceNamesValue = deviceNames.GetValueOrDefault(advertisementMac, null);
                if (deviceNamesValue == null)
                {
                    module.LogDebug($"{logPrefix} Creating device name resolver");
                    var dnr = new DeviceNameResolver(module, ah);
                    module.LogDebug($"{logPrefix} Resolving device name");
                    string resolvedDeviceName = string.Empty;
                    try
                    {
                        var deviceName = await ah.WaitAsync(dnr.GetDeviceNameAsync(args.Advertisement, args.BluetoothAddress), AsyncTask.GetDeviceName, watcherStopper);
                        if (deviceName != null)
                        {
                            resolvedDeviceName = deviceName;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        module.LogDebug(cancelMessage);
                        return;
                    }
                    module.LogDebug($"{logPrefix} Caching device name");
                    deviceNames[advertisementMac] = resolvedDeviceName;
                    if (!isConfiguredDevice)
                    {
                        Log($"Discovered device: {resolvedDeviceName} (MAC: {advertisementMac})");
                    }
                }
                else
                {
                    module.LogDebug($"{logPrefix} Found cached device name: {deviceNamesValue}");
                }
            }

            if (heartRateCharacteristic != null)
            {
                // Characteristic already found
                module.LogDebug($"{logPrefix} heartRateCharacteristic already found, stop further advertisement processing");
                return;
            }

            module.LogDebug($"{logPrefix} Begin processing advertisement data");
            IEnumerable<GattDeviceService>? cleanupServices = null;
            try
            {
                if (currentDevice == null)
                {
                    module.LogDebug($"{logPrefix} Setting currrent device");
                    try
                    {
                        currentDevice = await ah.WaitAsync(BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress), AsyncTask.SetCurrentDevice, watcherStopper);
                    }
                    catch (TaskCanceledException)
                    {
                        module.LogDebug(cancelMessage);
                        return;
                    }
                    if (currentDevice != null)
                    {
                        var currentDeviceName = deviceNames[advertisementMac] ?? "Unknown";
                        Log($"Found device named {currentDeviceName} for MAC {advertisementMac}");
                        module.SetDeviceName(currentDeviceName);
                        module.LogDebug($"{logPrefix} Register connection status change handler");
                        currentDevice.ConnectionStatusChanged += Device_ConnectionStatusChanged;
                    }
                    else
                    {
                        module.LogDebug($"{logPrefix} Current device could not be set");
                        return;
                    }
                }
                else
                {
                    module.LogDebug($"{logPrefix} Current device already set");
                }

                var missingUnknown = !missingCharacteristicDevices.Contains(deviceMacSetting);
                module.LogDebug($"{logPrefix} missingUnknown = {missingUnknown}");
                module.LogDebug($"{logPrefix} Finding HeratRate service");
                GattDeviceServicesResult? servicesResult = null;
                try
                {
                    servicesResult = await ah.WaitAsync(currentDevice.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached), AsyncTask.GetHeartRateService, watcherStopper);
                }
                catch (TaskCanceledException)
                {
                    module.LogDebug(cancelMessage);
                    return;
                }
                if (servicesResult != null && servicesResult.Services.Count > 0)
                {
                    module.LogDebug($"{logPrefix} Queueing all found services for cleanup");
                    cleanupServices = servicesResult.Services;
                    var firstService = cleanupServices.First();
                    if (missingUnknown)
                    {
                        Log("Found heartrate service");
                    }
                    GattCharacteristicsResult? characteristicsResult = null;
                    try
                    {
                        characteristicsResult = await ah.WaitAsync(firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached), AsyncTask.GetHeartRateCharacteristic, watcherStopper);
                    }
                    catch (TaskCanceledException)
                    {
                        module.LogDebug(cancelMessage);
                        return;
                    }
                    module.LogDebug($"{logPrefix} Finding HeartRateMeasurement characteristic");
                    if (characteristicsResult != null && characteristicsResult.Characteristics.Count > 0)
                    {
                        await HandleHeartRateCharacteristicFound(logPrefix, firstService, characteristicsResult.Characteristics[0], cancelMessage);
                        if (heartRateService != null)
                        {
                            module.LogDebug($"{logPrefix} Excluding heartRateService from cleanup");
                            cleanupServices = servicesResult.Services.Where(s => s != heartRateService);
                        }
                    }
                    else
                    {
                        module.LogDebug($"{logPrefix} No characteristics found");
                    }
                }
                else
                {
                    module.LogDebug($"{logPrefix} No services found");
                }

                if (heartRateCharacteristic == null && missingUnknown)
                {
                    module.LogDebug($"{logPrefix} Adding device to missing characteristic list");
                    missingCharacteristicDevices.Add(deviceMacSetting);
                    Log("No heartrate characteristic found");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to connect: {ex.Message}");
                ResetDeviceData();
            }
            finally
            {
                if (cleanupServices != null && cleanupServices.Any())
                {
                    module.LogDebug($"{logPrefix} Cleaning up services queued for cleanup");
                    foreach (var service in cleanupServices)
                        service.Dispose();
                }
                processingAdvertisementId = null;
                module.LogDebug($"{logPrefix} Stopped processing advertisement");
            }
        }

        private async Task HandleHeartRateCharacteristicFound(string logPrefix, GattDeviceService firstService, GattCharacteristic characteristic, string cancelMessage)
        {
            heartRateService = firstService;
            heartRateCharacteristic = characteristic;
            Log("Found heartrate measurement characteristic");

            heartRateCharacteristic.ValueChanged += HeartRateCharacteristic_ValueChanged;
            module.LogDebug("Registered heartrate characteristic value change handler");

            // Enable notifications for heart rate measurements
            Log("Requesting characteristic notifications");
            GattCommunicationStatus? status = null;
            try
            {
                status = await ah.WaitAsync(heartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify), AsyncTask.WriteCharacteristicConfigDescriptor, watcherStopper);
            }
            catch (TaskCanceledException)
            {
                module.LogDebug(cancelMessage);
                return;
            }
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
            }
            else
            {
                Log($"Failed to enable heart rate notifications. Status: {status}");
            }
        }

        private void HeartRateCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            module.LogDebug("HeartRateCharacteristic_ValueChanged");
            var data = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

            var updateData = data[1];
            module.LogDebug($"Invoking OnHeartrateUpdate action with data {updateData}");
            OnHeartrateUpdate?.Invoke(updateData);
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            module.LogDebug("Device_ConnectionStatusChanged");
            module.LogDebug($"sender.ConnectionStatus = {sender.ConnectionStatus}");
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                OnDisconnected?.Invoke();
            }
        }
    }
}
