using BluetoothHeartrateModule.UI;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Runtime.Intrinsics.Arm;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Heartrate;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace BluetoothHeartrateModule
{
    [ModuleTitle("Bluetooth Heartrate")]
    [ModuleDescription("Displays heartrate data from Bluetooth-based heartrate sensors")]
    public partial class BluetoothHeartrateModule : HeartrateModule<BluetoothHeartrateProvider>
    {
        private readonly WebsocketHeartrateServer wsServer;
        internal BluetoothLEAdvertisementWatcher? watcher;
        internal AsyncHelper ah;
        internal DeviceDataManager deviceDataManager;

        [ModulePersistent("selectedDeviceMac")]
        private string selectedDeviceMac { get; set; } = string.Empty;

        public BluetoothHeartrateModule()
        {
            ah = new AsyncHelper(this);
            wsServer = new WebsocketHeartrateServer(this);
            deviceDataManager = new(this);
        }

        protected override BluetoothHeartrateProvider CreateProvider()
        {
            LogDebug("Creating provider");
            var provider = new BluetoothHeartrateProvider(this);
            provider.OnHeartrateUpdate += SendWebcoketHeartrate;
            return provider;
        }

        internal new void Log(string message)
        {
            base.Log(message);
        }

        internal new void LogDebug(string message)
        {
            base.LogDebug(message);
        }

        protected override void OnPreLoad()
        {
            LogDebug("Call base class OnLoad");
            base.OnPreLoad();

            LogDebug("Creating settings");
            CreateToggle(BluetoothHeartrateSetting.WebsocketServerEnabled, @"Websocket Server Enabled", @"Broadcast the heartrate data over a local Websocket server", false);
            CreateTextBox(BluetoothHeartrateSetting.WebsocketServerHost, @"Websocket Server Hostname", @"Hostname (IP address) for the Websocket server", "127.0.0.1");
            CreateTextBox(BluetoothHeartrateSetting.WebsocketServerPort, @"Websocket Server Port", @"Port for the Websocket server", 36210);

            CreateVariable<string>(BluetoothHeartratevariable.DeviceName, @"Device Name");

            SetRuntimeView(typeof(BluetoothHeartrateRuntimeView));
        }

        protected override void OnPostLoad()
        {
            LogDebug("Call base class OnPostLoad");
            base.OnPostLoad();
            LogDebug("Updating settings");
            var wsServerEnabledSetting = GetSetting(BluetoothHeartrateSetting.WebsocketServerEnabled);
            if (wsServerEnabledSetting != null)
            {
                wsServerEnabledSetting.OnSettingChange += WsServerEnabledSettingChangeHandler;
            }
            WsServerEnabledSettingChangeHandler();
        }

        private void WsServerEnabledSettingChangeHandler()
        {
            var newValue = GetSettingValue<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled);
            var wsServerHostSetting = GetSetting(BluetoothHeartrateSetting.WebsocketServerHost);
            if (wsServerHostSetting != null)
            {
                wsServerHostSetting.IsEnabled = newValue;
            }
            var wsServerPortSetting = GetSetting(BluetoothHeartrateSetting.WebsocketServerPort);
            if (wsServerPortSetting != null)
            {
                wsServerPortSetting.IsEnabled = newValue;
            }
        }

        protected override async Task<bool> OnModuleStart()
        {
            LogDebug("Starting module");
            CreateWatcher();
            LogDebug("Call base class OnModuleStart");
            await base.OnModuleStart();
            if (GetWebocketEnabledSetting())
            {
                LogDebug("Starting wsServer");
                _ = wsServer.Start();
            }
            return true;
        }

        protected override async Task<bool> OnModuleStop()
        {
            LogDebug("Call base class OnModuleStop");
            await base.OnModuleStop();
            LogDebug("Stopping module");
            StopWatcher();
            if (GetWebocketEnabledSetting())
            {
                LogDebug("Stopping wsServer");
                wsServer.Stop();
            }
            return true;
        }

        internal string GetDeviceMacSetting()
        {
            return selectedDeviceMac;
        }
        internal bool GetWebocketEnabledSetting()
        {
            return GetSettingValue<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled);
        }
        internal string GetWebocketHostSetting()
        {
            return GetSettingValue<string>(BluetoothHeartrateSetting.WebsocketServerHost) ?? "";
        }
        internal int GetWebocketPortSetting()
        {
            return GetSettingValue<int>(BluetoothHeartrateSetting.WebsocketServerPort);
        }

        internal void SetDeviceMacSetting(string deviceMac)
        {
            if (selectedDeviceMac != deviceMac)
            {
                selectedDeviceMac = deviceMac;
                Log($"Selected device with MAC {deviceMac}");
            }
        }

        internal void ClearDeviceMacSetting()
        {
            ResetDeviceData();
            selectedDeviceMac = string.Empty;
            deviceDataManager.ConnectedDeviceMac = string.Empty;
            deviceDataManager.Refresh();
            StartWatcher();
        }
        internal void SetDeviceName(string deviceName)
        {
            SetVariableValue(BluetoothHeartratevariable.DeviceName, deviceName);
        }

        public void ResetCurrentDevice()
        {
            if (deviceDataManager.currentDevice != null)
            {
                LogDebug("Resetting currentDevice");
                try
                {
                    LogDebug("Disposing of currentDevice");
                    deviceDataManager.currentDevice.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if object is already disposed
                    LogDebug("currentDevice already disposed");
                }
                deviceDataManager.currentDevice = null;
                LogDebug("currentDevice has been reset");
            }
        }

        public void ResetDeviceData()
        {
            LogDebug("Resetting device data");
            deviceDataManager.ResetHeartRateService();
            deviceDataManager.ResetHeartRateCharacteristic();
            ResetCurrentDevice();
            deviceDataManager.ResetMissingCharacterisicsDevices();
            LogDebug("Device data has been reset");
        }


        private async void SendWebcoketHeartrate(int heartrate)
        {
            if (!GetWebocketEnabledSetting())
            {
                LogDebug("Not sending HR to websocket because it is disabled");
                return;
            }

            await wsServer.SendIntMessage(heartrate);
        }
        internal BluetoothLEAdvertisementWatcher CreateWatcher()
        {
            if (watcher == null)
            {
                LogDebug("Creating new watcher");
                watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
                LogDebug("Adding watcher stopped event handler");
                watcher.Stopped += Watcher_Stopped;
            }
            return watcher;
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            string scanStatus = args.Error.ToString();
            LogDebug($"Watcher stopped, error: {scanStatus}");
            Log($"Stopped scanning for devices");
            if (deviceDataManager.currentDevice == null)
            {
                LogDebug("Invoking OnDisconnected action");
                deviceDataManager.OnDisconnected?.Invoke();
            }
            deviceDataManager.Refresh();
        }

        internal Task<bool> StartWatcher()
        {
            var existingWatcher = CreateWatcher();
            LogDebug($"Starting watcher, current status: {existingWatcher.Status}");
            if (existingWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
            {
                try
                {
                    existingWatcher.Start();
                    var deviceMacSetting = GetDeviceMacSetting();
                    Log($"Scanning for {(deviceMacSetting == string.Empty ? "devices" : $"device with MAC {deviceMacSetting}")}");
                } catch (Exception ex)
                {
                    Log($"Could not start scanning for devices: {ex.Message}");
                    return Task.FromResult(false);
                }
            }
            return Task.FromResult(true);
        }

        internal void StopWatcher()
        {
            LogDebug("Stopping watcher");
            watcher?.Stop();
        }

        internal enum BluetoothHeartrateSetting
        {
            WebsocketServerEnabled,
            WebsocketServerHost,
            WebsocketServerPort
        }

        internal enum BluetoothHeartratevariable
        {
            DeviceName
        }
    }
}
