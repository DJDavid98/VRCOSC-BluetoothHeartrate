using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BluetoothHeartrateModule
{
    public class DeviceDataManager
    {
        Dictionary<string, DeviceData> Devices = new();

        BluetoothHeartrateModule module;
        DeviceNameResolver dnr;
        AsyncHelper ah;

        internal string ConnectedDeviceMac = string.Empty;
        internal string ProcessingDeviceMac = string.Empty;
        internal GattDeviceService? heartRateService;
        internal GattCharacteristic? heartRateCharacteristic;
        internal BluetoothLEDevice? currentDevice;
        internal readonly HashSet<string> missingCharacteristicDevices = new();

        public Action? OnDeviceListUpdate { get; internal set; }
        public Action<byte>? OnHeartRateCharacteristicValueChange { get; internal set; }
        public Action? OnConnected { get; internal set; }
        public Action? OnDisconnected { get; internal set; }

        private Dictionary<ulong, bool> processingAdvertisementMap = new();
        internal Dictionary<string, string> prefixData = new();

        public DeviceDataManager(BluetoothHeartrateModule module) {
            this.module = module;
            this.ah = module.ah;
            this.dnr = new DeviceNameResolver(module);
            // TODO Figure this out later
            // this.prefixData = GetPrefixData();
        }

        private Dictionary<string, string> GetPrefixData()
        {
            Dictionary<string, string> data = new();
            var ns = this.GetType().Namespace;
            string fileName = "oui.txt";
            var manufacturerLineRegex = new Regex(@"^([A-F\d]{2}-[A-F\d]{2}-[A-F\d]{2})\s+\(hex\)\s+\w+(.*)\s*$");
            try
            {
                var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ns}.{fileName}");
                if (resourceStream == null)
                {
                    throw new FileNotFoundException();
                }
                using (var sr = new StreamReader(resourceStream))
                {
                    var line = sr.ReadLine();
                    if (line != null)
                    {
                        var lineMatch = manufacturerLineRegex.Match(line);
                        if (lineMatch.Success)
                        {
                            data.Add(lineMatch.Groups[0].Value.Replace('-', ':'), lineMatch.Groups[1].Value);
                        }
                    }
                }
                module.LogDebug($"Loaded manufacturer data ({data.Count} entries)");
            }
            catch (FileNotFoundException)
            {
                module.LogDebug($"Optional resource {fileName} not found, manufacturer data will not be available");
            }
            return data;
        }

        private DeviceData Create(string mac)
        {
            var data = new DeviceData(mac, this);
            Devices[mac] = data;
            return data;
        }
        internal DeviceData Add(string advertisementMac, string deviceName)
        {
            var deviceData = Create(advertisementMac);
            deviceData.Name = deviceName;

            Refresh();
            return deviceData;
        }

        internal async Task<DeviceData?> Add(ulong bluetoothAddress, BluetoothLEAdvertisement advertisement, CancellationTokenSource cancelToken)
        {
            if (processingAdvertisementMap.ContainsKey(bluetoothAddress)) { return null; }

            processingAdvertisementMap.Add(bluetoothAddress, true);
            try
            {
                var advertisementMac = Converter.FormatAsMac(bluetoothAddress);
                var logPrefix = $"[MAC:{advertisementMac}]";
                module.LogDebug($"{logPrefix} Resolving device name");
                string resolvedDeviceName = string.Empty;
                var deviceName = await module.ah.WaitAsync(dnr.GetDeviceNameAsync(advertisement, bluetoothAddress), AsyncTask.GetDeviceName, cancelToken);
                if (deviceName != null)
                {
                    resolvedDeviceName = deviceName;
                }
                return Add(advertisementMac, resolvedDeviceName);
            }
            finally
            {
                processingAdvertisementMap.Remove(bluetoothAddress);
            }
        }

        public DeviceData? Get(string mac) {
            return Devices.ContainsKey(mac) ? Devices[mac] : null;
        }

        public bool Has(string mac) {
            return Devices.ContainsKey(mac);
        }

        public void Remove(string mac)
        {
            Devices.Remove(mac);
            Refresh();
        }
        public void Refresh()
        {
            OnDeviceListUpdate?.Invoke();
        }

        public DeviceData[] GetDevices()
        {
            return Devices.Values.ToArray();
        }

        public void ClearDevices() { Devices.Clear(); }

        public void ResetHeartRateService()
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
        public void ResetHeartRateCharacteristic()
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

        public void ResetMissingCharacterisicsDevices()
        {
            module.LogDebug("Clearing missing characteristics");
            missingCharacteristicDevices.Clear();
        }

        public async Task HandleHeartRateCharacteristicFound(string logPrefix, GattDeviceService firstService, GattCharacteristic characteristic, CancellationTokenSource watcherStopper)
        {
            heartRateService = firstService;
            heartRateCharacteristic = characteristic;
            module.LogDebug("Found heartrate measurement characteristic");

            heartRateCharacteristic.ValueChanged += HeartRateCharacteristic_ValueChanged;
            module.LogDebug("Registered heartrate characteristic value change handler");

            // Enable notifications for heart rate measurements
            module.LogDebug("Requesting characteristic notifications");
            GattCommunicationStatus? status = await ah.WaitAsync(heartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify), AsyncTask.WriteCharacteristicConfigDescriptor, watcherStopper);
            if (status == GattCommunicationStatus.Success)
            {
                module.LogDebug($"{logPrefix} Invoking OnConnected action");
                OnConnected?.Invoke();
                module.Log("Connection successful");
                Refresh();
                module.LogDebug($"{logPrefix} Stopping watcher");
                module.StopWatcher();
            }
            else
            {
                module.LogDebug($"Failed to enable heart rate notifications. Status: {status}");
            }
        }

        private void HeartRateCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            module.LogDebug("HeartRateCharacteristic_ValueChanged");
            var data = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

            var updateData = data[1];
            module.LogDebug($"Invoking OnHeartrateUpdate action with data {updateData}");
            OnHeartRateCharacteristicValueChange?.Invoke(updateData);
        }

        internal void RegiterConnectionStatusChangeHandler(string logPrefix)
        {
            module.LogDebug($"{logPrefix} Register connection status change handler");
            if (currentDevice != null)
            {
                currentDevice.ConnectionStatusChanged += Device_ConnectionStatusChanged;
            }
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            module.LogDebug($"Device connection status changed to {sender.ConnectionStatus}");
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                OnDisconnected?.Invoke();
                Refresh();
            }
        }
    }

}
