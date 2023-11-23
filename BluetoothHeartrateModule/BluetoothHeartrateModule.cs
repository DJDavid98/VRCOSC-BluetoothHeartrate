using VRCOSC.Game.SDK;
using Windows.Devices.Bluetooth.Advertisement;
using VRCOSC.Game.SDK.Modules.Heartrate;

namespace BluetoothHeartrateModule
{
    [ModuleTitle("Bluetooth Heartrate")]
    [ModuleDescription("Displays heartrate data from Bluetooth-based heartrate sensors")]
    public partial class BluetoothHeartrateModule : HeartrateModule<BluetoothHeartrateProvider>
    {
        private WebsocketHeartrateServer wsServer;
        internal BluetoothLEAdvertisementWatcher? watcher;

        public BluetoothHeartrateModule()
        {
            wsServer = new WebsocketHeartrateServer(this);
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
            base.Log(message);
            // No debug logs in V2 yet
            // base.LogDebug(message);
        }

        protected override void OnLoad()
        {
            LogDebug("OnLoad");
            base.OnLoad();
            CreateTextBox(BluetoothHeartrateSetting.DeviceMac, "Device MAC address", "MAC address of the Bluetooth heartrate monitor", string.Empty);

            CreateToggle(BluetoothHeartrateSetting.WebsocketServerEnabled, @"Websocket Server Enabled", @"Broadcast the heartrate data over a local Websocket server", false);
            CreateTextBox(BluetoothHeartrateSetting.WebsocketServerHost, @"Websocket Server Hostname", @"Hostname (IP address) for the Websocket server", "127.0.0.1");
            CreateTextBox(BluetoothHeartrateSetting.WebsocketServerPort, @"Websocket Server Port", @"Port for the Websocket server", 36210);

            // TODO After chatbox support lands in V2
            // CreateVariable(BluetoothHeartratevariable.DeviceName, @"Device Name", "device");
        }

        protected override void OnPostLoad()
        {
            var wsServerHostSetting = GetSetting(BluetoothHeartrateSetting.WebsocketServerHost);
            if (wsServerHostSetting != null)
            {
                wsServerHostSetting.IsEnabled = () => GetSettingValue<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled);
            }
            var wsServerPortSetting = GetSetting(BluetoothHeartrateSetting.WebsocketServerPort);
            if (wsServerPortSetting != null)
            {
                wsServerPortSetting.IsEnabled = () => GetSettingValue<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled);
            }
        }

        protected override async Task<bool> OnModuleStart()
        {
            LogDebug("Starting module");
            CreateWatcher();
            await base.OnModuleStart();
            if (GetWebocketEnabledSetting())
            {
                LogDebug("Starting wsServer");
                await wsServer.Start();
            }
            return true;
        }

        protected override async Task<bool> OnModuleStop()
        {
            LogDebug("Stopping module");
            StopWatcher();
            if (GetWebocketEnabledSetting())
            {
                LogDebug("Stopping wsServer");
                wsServer.Stop();
            }
            await base.OnModuleStop();
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
            // TODO After chatbox support lands in V2
            // SetVariableValue(BluetoothHeartratevariable.DeviceName, deviceName);
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

        internal void StartWatcher()
        {
            if (watcher != null)
            {
                LogDebug($"Starting watcher, current status: {watcher.Status}");
                if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    watcher.Start();
                    Log("Scanning for devices");
                }
            }
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
