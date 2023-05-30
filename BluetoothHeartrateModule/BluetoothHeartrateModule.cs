using VRCOSC.Game.Modules;
using VRCOSC.Game.Modules.ChatBox;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace BluetoothHeartrateModule
{
    public partial class BluetoothHeartrateModule : ChatBoxModule
    {
        private static readonly TimeSpan heartrate_timeout = TimeSpan.FromSeconds(30);

        public override string Title => "BluetoothHeartrate";
        public override string Description => "Displays heartrate data from Bluetooth-based heartrate trackers";
        public override string Author => "DJDavid98";
        public override ModuleType Type => ModuleType.Health;
        protected override TimeSpan DeltaUpdate => TimeSpan.FromSeconds(1);

        private BluetoothLEAdvertisementWatcher watcher;
        private byte currentHeartrate = 0;
        private byte currentBatteryLevel = 0;
        private Dictionary<string, string?> potentialDevices = new();
        private static GattCharacteristic? heartRateCharacteristic;
        private static GattCharacteristic? batteryCharacteristic;

        protected override void CreateAttributes()
        {
            CreateSetting(BluetoothHeartrateSetting.DeviceMac, "Device MAC address", "MAC address of the Bluetooth heartrate monitor", string.Empty);

            CreateSetting(BluetoothHeartrateSetting.NormalisedLowerbound, @"Normalised Lowerbound", @"The lower bound BPM the normalised parameter should use", 0);
            CreateSetting(BluetoothHeartrateSetting.NormalisedUpperbound, @"Normalised Upperbound", @"The upper bound BPM the normalised parameter should use", 240);

            CreateParameter<bool>(BluetoothHeartrateParameter.Enabled, ParameterMode.Write, "VRCOSC/Heartrate/Enabled", "Enabled", "Whether this module is attempting to emit values");
            CreateParameter<float>(BluetoothHeartrateParameter.Normalised, ParameterMode.Write, "VRCOSC/Heartrate/Normalised", "Normalised", "The heartrate value normalised to 240bpm");
            CreateParameter<float>(BluetoothHeartrateParameter.Units, ParameterMode.Write, "VRCOSC/Heartrate/Units", "Units", "The units digit 0-9 mapped to a float");
            CreateParameter<float>(BluetoothHeartrateParameter.Tens, ParameterMode.Write, "VRCOSC/Heartrate/Tens", "Tens", "The tens digit 0-9 mapped to a float");
            CreateParameter<float>(BluetoothHeartrateParameter.Hundreds, ParameterMode.Write, "VRCOSC/Heartrate/Hundreds", "Hundreds", "The hundreds digit 0-9 mapped to a float");
            CreateParameter<float>(BluetoothHeartrateParameter.Battery, ParameterMode.Write, "VRCOSC/Heartrate/Battery", "Battery", "The amount of battery left in the device");

            CreateVariable(BluetoothHeartrateVariable.Heartrate, @"Heartrate", @"hr");

            CreateState(BluetoothHeartrateState.Default, @"Default", $@"Heartrate/v{GetVariableFormat(BluetoothHeartrateVariable.Heartrate)} bpm");
        }

        private bool isConnected = false;
        private DateTimeOffset lastHeartrateTime;
        BluetoothLEDevice? device;
        private bool isReceiving => isConnected && lastHeartrateTime + heartrate_timeout >= DateTimeOffset.Now;

        protected override void OnModuleStart()
        {
            currentHeartrate = 0;
            currentBatteryLevel = 0;
            device = null;
            heartRateCharacteristic = null;
            batteryCharacteristic = null;
            potentialDevices.Clear();
            lastHeartrateTime = DateTimeOffset.MinValue;
            ChangeStateTo(BluetoothHeartrateState.Default);
            isConnected = false;
            if (getDeviceMacSetting() == string.Empty)
            {
                Log("Device MAC setting is not set, module will log discovered devices");
            }
            attemptConnection();
        }

        private void attemptConnection()
        {
            watcher = new BluetoothLEAdvertisementWatcher();

            watcher.Received += Watcher_Received;
            watcher.Stopped += Watcher_Stopped;

            watcher.Start();
            Log("Watcher started");
        }

        private string formatAsMac(ulong deviceMac)
        {
            return BitConverter.ToString(BitConverter.GetBytes(deviceMac)).Replace('-', ':');
        }

        private string getDeviceMacSetting()
        {
            return GetSetting<string>(BluetoothHeartrateSetting.DeviceMac);
        }

        private string getDeviceNameWithFallback(string? deviceName)
        {
            return deviceName == string.Empty || deviceName == null ? "Unknown" : deviceName;
        }

        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var advertisementMac = formatAsMac(args.BluetoothAddress);
            var advertisementDeviceName = args.Advertisement.LocalName;
            var deviceMacSetting = getDeviceMacSetting();
            if (deviceMacSetting == string.Empty)
            {
                if (advertisementDeviceName == string.Empty)
                {
                    device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);

                    // Get the device name
                    advertisementDeviceName = await GetDeviceNameAsync(device);
                }
                var potentialDevicesValue = potentialDevices.GetValueOrDefault(advertisementMac, null);
                if (advertisementDeviceName != potentialDevicesValue)
                {
                    potentialDevices[advertisementMac] = advertisementDeviceName;
                }
                if (potentialDevicesValue == null)
                {
                    var deviceList = potentialDevices.ToList().Select(kvp => $"{getDeviceNameWithFallback(kvp.Value)} (MAC: {kvp.Key})").ToArray();
                    Log($"Discovered devices:");
                    foreach (var deviceListItem in deviceList)
                    {
                        Log(deviceListItem);
                    }
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

            try
            {
                if (device == null)
                {
                    device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                    Log($"Found device for MAC {advertisementMac}");
                }

                var services = await device.GetGattServicesAsync();
                foreach (var service in services.Services)
                {
                    if (service.Uuid != GattServiceUuids.HeartRate || service.Uuid != GattServiceUuids.Battery) { continue; }

                    var characteristics = await service.GetCharacteristicsAsync();
                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        if (heartRateCharacteristic == null && characteristic.Uuid == GattCharacteristicUuids.HeartRateMeasurement)
                        {
                            heartRateCharacteristic = characteristic;
                            heartRateCharacteristic.ValueChanged += HeartRateCharacteristic_ValueChanged;
                            Log("Registered heartrate characteristic value change handler");

                            // Enable notifications for heart rate measurements
                            var status = await heartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            isConnected = status == GattCommunicationStatus.Success;
                            if (isConnected)
                            {
                                ChangeStateTo(BluetoothHeartrateState.Connected);
                                Log("Heart rate notifications enabled");
                            }
                            else
                            {
                                ChangeStateTo(BluetoothHeartrateState.Disconnected);
                                Log($"Failed to enable heart rate notifications. Status: {status}");
                            }
                        }
                        else if (false && batteryCharacteristic == null && characteristic.Uuid == GattCharacteristicUuids.BatteryLevel)
                        {
                            batteryCharacteristic = characteristic;
                            batteryCharacteristic.ValueChanged += BatteryLevelCharacteristic_ValueChanged;
                            Log("Registered battery level characteristic value change handler");

                            // Enable notifications for battery level changed
                            var status = await batteryCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            if (status == GattCommunicationStatus.Success)
                            {
                                ChangeStateTo(BluetoothHeartrateState.Connected);
                                Log("Battery level notifications enabled");
                            }
                            else
                            {
                                ChangeStateTo(BluetoothHeartrateState.Disconnected);
                                Log($"Failed to enable battery level notifications. Status: {status}");
                            }
                        }
                    }
                }

                if (heartRateCharacteristic == null)
                {
                    throw new Exception("No heartrate characteristic found");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to connect: {ex.Message}");
                ChangeStateTo(BluetoothHeartrateState.Disconnected);
                isConnected = false;
            }
        }
        private static async Task<string> GetDeviceNameAsync(BluetoothLEDevice device)
        {
            // Get the device name using the DeviceInformation class
            DeviceInformation deviceInfo = await DeviceInformation.CreateFromIdAsync(device.DeviceId);
            string deviceName = deviceInfo.Name;

            return deviceName;
        }

        private void HeartRateCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = new byte[args.CharacteristicValue.Length];
            Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

            currentHeartrate = data[1];
            lastHeartrateTime = DateTimeOffset.Now;
        }
        private void BatteryLevelCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = new byte[args.CharacteristicValue.Length];
            Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

            currentBatteryLevel = data[1];
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            SendParameter(BluetoothHeartrateParameter.Enabled, false);
            ChangeStateTo(BluetoothHeartrateState.Disconnected);
            if (heartRateCharacteristic != null)
            {
                heartRateCharacteristic.ValueChanged -= HeartRateCharacteristic_ValueChanged;
            }
            if (batteryCharacteristic != null)
            {
                batteryCharacteristic.ValueChanged -= BatteryLevelCharacteristic_ValueChanged;
            }
            isConnected = false;
            Log("Watcher stopped");
        }

        protected override void OnModuleUpdate()
        {
            sendParameters();
        }

        private void sendParameters()
        {
            SendParameter(BluetoothHeartrateParameter.Enabled, isReceiving);

            if (isReceiving)
            {
                var normalisedHeartRate = Map(currentHeartrate, GetSetting<int>(BluetoothHeartrateSetting.NormalisedLowerbound), GetSetting<int>(BluetoothHeartrateSetting.NormalisedUpperbound), 0, 1);
                var individualValues = toDigitArray(currentHeartrate, 3);

                SendParameter(BluetoothHeartrateParameter.Normalised, normalisedHeartRate);
                SendParameter(BluetoothHeartrateParameter.Units, individualValues[2] / 10f);
                SendParameter(BluetoothHeartrateParameter.Tens, individualValues[1] / 10f);
                SendParameter(BluetoothHeartrateParameter.Hundreds, individualValues[0] / 10f);
                SendParameter(BluetoothHeartrateParameter.Battery, currentBatteryLevel / 100f);
                SetVariableValue(BluetoothHeartrateVariable.Heartrate, currentHeartrate.ToString());
            }
            else
            {
                SendParameter(BluetoothHeartrateParameter.Normalised, 0);
                SendParameter(BluetoothHeartrateParameter.Units, 0);
                SendParameter(BluetoothHeartrateParameter.Tens, 0);
                SendParameter(BluetoothHeartrateParameter.Hundreds, 0);
                SendParameter(BluetoothHeartrateParameter.Battery, 0);
                SetVariableValue(BluetoothHeartrateVariable.Heartrate, @"0");
            }
        }
        private static int[] toDigitArray(int num, int totalWidth)
        {
            return num.ToString().PadLeft(totalWidth, '0').Select(digit => int.Parse(digit.ToString())).ToArray();
        }

        protected override void OnModuleStop()
        {
            watcher.Stop();
        }

        private enum BluetoothHeartrateSetting
        {
            DeviceMac,
            NormalisedLowerbound,
            NormalisedUpperbound
        }

        private enum BluetoothHeartrateParameter
        {
            Enabled,
            Normalised,
            Units,
            Tens,
            Hundreds,
            Battery
        }

        private enum BluetoothHeartrateVariable
        {
            Heartrate
        }

        private enum BluetoothHeartrateState
        {
            Default,
            Connected,
            Disconnected
        }
    }
}
