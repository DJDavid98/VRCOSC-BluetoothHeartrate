using VRCOSC.Game.Modules.Bases.Heartrate;
using Windows.Devices.Bluetooth.Advertisement;

namespace BluetoothHeartrateModule
{
    public partial class BluetoothHeartrateModule : HeartrateModule<BluetoothHeartrateProvider>
    {
        public override string Title => "Bluetooth Heartrate";
        public override string Description => "Displays heartrate data from Bluetooth-based heartrate sensors";
        public override string Author => "DJDavid98";
        public override ModuleType Type => ModuleType.Health;
        protected override TimeSpan DeltaUpdate => TimeSpan.FromSeconds(1);

        private WebsocketHeartrateServer wsServer;
        internal BluetoothLEAdvertisementWatcher? watcher;

        public BluetoothHeartrateModule()
        {
            wsServer = new WebsocketHeartrateServer(this);
        }

        protected override BluetoothHeartrateProvider CreateProvider()
        {
            watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
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
            ResetWatcher();
            base.OnModuleStart();
            await wsServer.Start();
        }

        protected override void OnModuleStop()
        {
            ResetWatcher();
            wsServer.Stop();
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

        internal void StopWatcher()
        {
            watcher?.Stop();
        }

        internal void SetDeviceName(string deviceName)
        {
            SetVariableValue(BluetoothHeartratevariable.DeviceName, deviceName);
        }

        private async void SendWebcoketHeartrate(int heartrate)
        {
            await wsServer.SendIntMessage(heartrate);
        }

        private void ResetWatcher()
        {
            if (watcher != null)
            {
                StopWatcher();
                watcher = null;
            }
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
