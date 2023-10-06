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
            var provider = new BluetoothHeartrateProvider(this);
            provider.OnHeartrateUpdate += SendWebcoketHeartrate;
            return provider;
        }

        internal new void Log(string message)
        {
            base.Log(message);
        }

        protected override void CreateAttributes()
        {
            base.CreateAttributes();
            CreateSetting(BluetoothHeartrateSetting.DeviceMac, "Device MAC address", "MAC address of the Bluetooth heartrate monitor", string.Empty);

            CreateSetting(BluetoothHeartrateSetting.WebsocketServerEnabled, @"Websocket Server Enabled", @"Broadcast the heartrate data over a local Websocket server", false);
            CreateSetting(BluetoothHeartrateSetting.WebsocketServerHost, @"Websocket Server Hostname", @"Hostname (IP address) for the Websocket server", "127.0.0.1", () => GetSetting<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled));
            CreateSetting(BluetoothHeartrateSetting.WebsocketServerPort, @"Websocket Server Port", @"Port for the Websocket server", 36210, () => GetSetting<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled));

            CreateVariable(BluetoothHeartratevariable.DeviceName, @"Device Name", "device");
        }

        protected override async void OnModuleStart()
        {
            CreateWatcher();
            base.OnModuleStart();
            if (GetWebocketEnabledSetting())
            {
                await wsServer.Start();
            }
        }

        protected override void OnModuleStop()
        {
            StopWatcher();
            if (GetWebocketEnabledSetting())
            {
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
                return;
            }

            await wsServer.SendIntMessage(heartrate);
        }
        internal BluetoothLEAdvertisementWatcher CreateWatcher()
        {
            if (watcher == null)
            {
                watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
                watcher.Stopped += Watcher_Stopped;
            }
            return watcher;
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
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
                HeartrateProvider?.OnDisconnected?.Invoke();
            }
        }

        internal void StartWatcher()
        {
            if (watcher != null)
            {
                if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    watcher.Start();
                    Log("Scanning for devices");
                }
            }
        }

        internal void StopWatcher()
        {
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
