using VRCOSC.Game.Modules;
using VRCOSC.Game.Modules.Bases.Heartrate;
using Windows.Devices.Bluetooth.Advertisement;

namespace BluetoothHeartrateModule
{
    [ModuleTitle("Bluetooth Heartrate")]
    [ModuleDescription("Displays heartrate data from Bluetooth-based heartrate sensors")]
    [ModuleAuthor("DJDavid98")]
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
            base.LogDebug(message);
        }

        protected override void CreateAttributes()
        {
            LogDebug("Creating attributes");
            base.CreateAttributes();
            CreateSetting(BluetoothHeartrateSetting.DeviceMac, "Device MAC address", "MAC address of the Bluetooth heartrate monitor", string.Empty);

            CreateSetting(BluetoothHeartrateSetting.WebsocketServerEnabled, @"Websocket Server Enabled", @"Broadcast the heartrate data over a local Websocket server", false);
            CreateSetting(BluetoothHeartrateSetting.WebsocketServerHost, @"Websocket Server Hostname", @"Hostname (IP address) for the Websocket server", "127.0.0.1", () => GetSetting<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled));
            CreateSetting(BluetoothHeartrateSetting.WebsocketServerPort, @"Websocket Server Port", @"Port for the Websocket server", 36210, () => GetSetting<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled));

            CreateVariable(BluetoothHeartratevariable.DeviceName, @"Device Name", "device");
        }

        protected override async void OnModuleStart()
        {
            LogDebug("Starting module");
            CreateWatcher();
            base.OnModuleStart();
            if (GetWebocketEnabledSetting())
            {
                LogDebug("Starting wsServer");
                await wsServer.Start();
            }
        }

        protected override void OnModuleStop()
        {
            LogDebug("Stopping module");
            StopWatcher();
            if (GetWebocketEnabledSetting())
            {
                LogDebug("Stopping wsServer");
                wsServer.Stop();
            }
            base.OnModuleStop();
        }

        internal string GetDeviceMacSetting()
        {
            return GetSetting<string>(BluetoothHeartrateSetting.DeviceMac);
        }
        internal bool GetWebocketEnabledSetting()
        {
            return GetSetting<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled);
        }
        internal string GetWebocketHostSetting()
        {
            return GetSetting<string>(BluetoothHeartrateSetting.WebsocketServerHost);
        }
        internal int GetWebocketPortSetting()
        {
            return GetSetting<int>(BluetoothHeartrateSetting.WebsocketServerPort);
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
