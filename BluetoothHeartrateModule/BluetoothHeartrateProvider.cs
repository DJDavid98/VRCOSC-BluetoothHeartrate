using System.Runtime.Intrinsics.Arm;
using VRCOSC.App.SDK.Modules.Heartrate;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    public class BluetoothHeartrateProvider : HeartrateProvider
    {
        private CancellationTokenSource watcherStopper = new();
        private readonly BluetoothHeartrateModule module;
        private readonly DeviceDataManager ddm;
        private readonly AsyncHelper ah;
        private readonly DeviceNameResolver dnr;
        public override bool IsConnected => ddm.currentDevice != null && ddm.heartRateCharacteristic != null && ddm.currentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;

        public BluetoothHeartrateProvider(BluetoothHeartrateModule module)
        {
            this.module = module;
            this.ah = module.ah;
            this.ddm = module.deviceDataManager;
            dnr = new DeviceNameResolver(module);
        }

        public override async Task<bool> Initialise()
        {
            module.LogDebug("Initializing BluetoothHeartrateProvider");
            if (module.GetDeviceMacSetting() == string.Empty)
            {
                module.LogDebug("Device MAC setting is not set, module will log discovered devices");
            }

            if (module.watcher == null)
            {
                module.LogDebug("Watcher is not defined");
                return false;
            }

            return await StartWatcher();
        }

        private async Task<bool> StartWatcher(bool invokeDisconnect = true)
        {
            if (module.watcher != null)
            {
                module.LogDebug("Registering watcher received handler");
                module.watcher.Received += Watcher_Received;
            }
            module.LogDebug("Registering characteristic change handler");
            ddm.OnHeartRateCharacteristicValueChange += HandleHeartRateCharacteristicValueChange;
            ddm.OnConnected += HandleConneted;
            ddm.OnDisconnected += HandleDisconneted;
            module.LogDebug("Starting watcher");
            watcherStopper = new();
            module.LogDebug("Generated new watcherStopper");
            var startResult = await module.StartWatcher();
            if (invokeDisconnect && !startResult)
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
            module.LogDebug("Cancelling watcherStopper");
            watcherStopper.Cancel();
            module.LogDebug("Clearing device names");
            ddm.ClearDevices();
            ddm.ResetMissingCharacterisicsDevices();
            module.ResetDeviceData();
            module.LogDebug("Clearing processing device MAC");
            ddm.ProcessingDeviceMac = string.Empty;
            module.LogDebug("Clearing connected device MAC");
            ddm.ConnectedDeviceMac = string.Empty;
            ddm.Refresh();
            ddm.OnHeartRateCharacteristicValueChange -= HandleHeartRateCharacteristicValueChange;
            ddm.OnConnected -= HandleConneted;
            ddm.OnDisconnected -= HandleDisconneted;
        }


            private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var advertisementMac = Converter.FormatAsMac(args.BluetoothAddress);
            if (ddm.ProcessingDeviceMac == advertisementMac)
            {
                // Skip advertisements for the device which is already being processed
                return;
            }

            // We need a prefix to follow the logs as advertisements can come in pretty rapidly
            var logPrefix = $"[MAC:{advertisementMac}]";
            var deviceMacSetting = module.GetDeviceMacSetting();
            var isDeviceConfigured = deviceMacSetting != string.Empty;
            var isConfiguredDevice = advertisementMac == deviceMacSetting;
            if (isDeviceConfigured && !isConfiguredDevice)
            {
                // Not the droid we're looking for
                return;
            }

            try
            {
                var deviceData = ddm.Get(advertisementMac);
                if (deviceData == null)
                {
                    var newDeviceData = await ddm.Add(args.BluetoothAddress, args.Advertisement, watcherStopper);
                    if (newDeviceData != null)
                    {
                        deviceData = newDeviceData;
                        module.LogDebug($"{logPrefix} Discovered device: {deviceData?.Label}");
                    }
                }
                else
                {
                    deviceData.LastAdvertisementDateTime = DateTime.Now;
                }

                if (!isDeviceConfigured)
                {
                    // Waiting for device mac selection before progressing further
                    return;
                }

                if (ddm.heartRateCharacteristic != null)
                {
                    // Characteristic already found
                    return;
                }

                if (ddm.ProcessingDeviceMac == advertisementMac)
                {
                    // Skip advertisements for the device which is already being processed
                    return;
                }

                ddm.ProcessingDeviceMac = advertisementMac;
                ddm.Refresh();
                module.LogDebug($"{logPrefix} Begin processing advertisement data");
                IEnumerable<GattDeviceService>? cleanupServices = null;
                try
                {
                    if (ddm.currentDevice == null)
                    {
                        module.LogDebug($"{logPrefix} Setting currrent device");
                        var newCurrentDevice = await ah.WaitAsync(BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress), AsyncTask.SetCurrentDevice, watcherStopper);
                        if (newCurrentDevice != null)
                        {
                            ddm.currentDevice = newCurrentDevice;
                            var currentDeviceName = ddm.Get(advertisementMac)?.Name ?? string.Empty;
                            module.LogDebug($"{logPrefix} Found device named {currentDeviceName} for MAC {advertisementMac}");
                            module.SetDeviceName(currentDeviceName);
                            ddm.RegiterConnectionStatusChangeHandler(logPrefix);
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

                    var missingCharacteristicUnknown = !ddm.missingCharacteristicDevices.Contains(deviceMacSetting);
                    cleanupServices = await FindHeartrateCharacteristic(ddm.currentDevice, advertisementMac, logPrefix, deviceMacSetting, missingCharacteristicUnknown);

                    if (ddm.heartRateCharacteristic == null && missingCharacteristicUnknown)
                    {
                        ddm.missingCharacteristicDevices.Add(deviceMacSetting);
                    }
                }
                catch (Exception ex)
                {
                    module.LogDebug($"{logPrefix} Failed to connect: {ex.Message}");
                    module.ResetDeviceData();
                }
                finally
                {
                    if (cleanupServices != null && cleanupServices.Any())
                    {
                        foreach (var service in cleanupServices)
                            service.Dispose();
                    }
                    ddm.ProcessingDeviceMac = string.Empty;
                    ddm.Refresh();
                    module.LogDebug($"{logPrefix} Stopped processing advertisement");
                }

            }
            catch (TaskCanceledException)
            {
                module.LogDebug($"{logPrefix} Watcher cancelled, discarding advertisement");
                return;
            }
        }

        private async Task<IEnumerable<GattDeviceService>?> FindHeartrateCharacteristic(BluetoothLEDevice currentDevice, string advertisementMac, string logPrefix, string deviceMacSetting, bool missingCharacteristicUnknown)
        {
            IEnumerable<GattDeviceService>? cleanupServices = null;
            module.LogDebug($"{logPrefix} Finding HeratRate service");
            GattDeviceServicesResult? servicesResult = null;
            servicesResult = await ah.WaitAsync(currentDevice.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached), AsyncTask.GetHeartRateService, watcherStopper);
            if (servicesResult != null && servicesResult.Services.Count > 0)
            {
                cleanupServices = servicesResult.Services;
                var firstService = cleanupServices.First();
                if (missingCharacteristicUnknown)
                {
                    module.LogDebug($"{logPrefix} Found heartrate service");
                }
                GattCharacteristicsResult? characteristicsResult = await ah.WaitAsync(firstService.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached), AsyncTask.GetHeartRateCharacteristic, watcherStopper);
                module.LogDebug($"{logPrefix} Finding HeartRateMeasurement characteristic");
                if (characteristicsResult != null && characteristicsResult.Characteristics.Count > 0)
                {
                    ddm.ConnectedDeviceMac = advertisementMac;
                    ddm.Refresh();
                    await ddm.HandleHeartRateCharacteristicFound(logPrefix, firstService, characteristicsResult.Characteristics[0], watcherStopper);
                    if (ddm.heartRateService != null)
                    {
                        cleanupServices = servicesResult.Services.Where(s => s != ddm.heartRateService);
                    }
                }
                else if (missingCharacteristicUnknown)
                {
                    module.Log($"No heartrate characteristics found");
                }
            }
            else if (missingCharacteristicUnknown)
            {
                module.Log($"No heartrate service found");
            }

            return cleanupServices;
        }

        private void HandleConneted()
        {
            OnConnected?.Invoke();
        }
        private void HandleDisconneted()
        {
            OnDisconnected?.Invoke();
            Reset();
            _ = StartWatcher();
        }

        private void HandleHeartRateCharacteristicValueChange(byte updateData)
        {
            OnHeartrateUpdate?.Invoke(updateData);
        }
    }
}
