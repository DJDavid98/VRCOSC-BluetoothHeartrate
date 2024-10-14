using BluetoothHeartrateModule.UI;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Heartrate;
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
        internal Action<string[]>? OnDeviceListUpdate;

        public BluetoothHeartrateModule()
        {
            ah = new AsyncHelper(this);
            wsServer = new WebsocketHeartrateServer(this, ah);
        }

        protected override BluetoothHeartrateProvider CreateProvider()
        {
            LogDebug("Creating provider");
            var provider = new BluetoothHeartrateProvider(this, ah);
            provider.OnHeartrateUpdate += SendWebcoketHeartrate;
            provider.OnDeviceListUpdate += OnDeviceListUpdate;
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
            CreateTextBox(BluetoothHeartrateSetting.DeviceMac, "Device MAC address", "MAC address of the Bluetooth heartrate monitor", string.Empty);
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
            return GetSettingValue<string>(BluetoothHeartrateSetting.DeviceMac) ?? "";
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

        internal void SetDeviceName(string deviceName)
        {
            SetVariableValue(BluetoothHeartratevariable.DeviceName, deviceName);
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
            LogDebug($"Watcher stopped, error: {args.Error}");
            string scanStatus;
            bool invokeDisconnect = true;
            switch (args.Error)
            {
                case Windows.Devices.Bluetooth.BluetoothError.RadioNotAvailable:
                    scanStatus = "Bluetooth adapter/module is disconnected";
                    break;
                case Windows.Devices.Bluetooth.BluetoothError.Success:
                    scanStatus = "device found";
                    invokeDisconnect = false;
                    break;
                default:
                    scanStatus = args.Error.ToString();
                    break;
            }
            Log($"Stopped scanning for devices ({scanStatus})");
            if (invokeDisconnect)
            {
                LogDebug("Invoking OnDisconnected action");
                HeartrateProvider?.OnDisconnected?.Invoke();
            }
        }

        internal Task<bool> StartWatcher()
        {
            if (watcher != null)
            {
                LogDebug($"Starting watcher, current status: {watcher.Status}");
                if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    try
                    {
                        watcher.Start();
                        var deviceMacSetting = GetDeviceMacSetting();
                        Log($"Scanning for {(deviceMacSetting == string.Empty ? "devices" : $"device with MAC {deviceMacSetting}")}");
                    } catch (Exception ex)
                    {
                        Log($"Could not start scanning for devices: {ex.Message}");
                        return Task.FromResult(false);
                    }
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
            DeviceMac,
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
