using VRCOSC.Game.Modules;
using VRCOSC.Game.Modules.ChatBox;

namespace BluetoothHeartrateModule
{
    public partial class BluetoothHeartrateModule : ChatBoxModule
    {
        private static readonly TimeSpan heartrateTimeout = TimeSpan.FromSeconds(30);

        public override string Title => "Bluetooth Heartrate";
        public override string Description => "Displays heartrate data from Bluetooth-based heartrate sensors";
        public override string Author => "DJDavid98";
        public override ModuleType Type => ModuleType.Health;
        protected override TimeSpan DeltaUpdate => TimeSpan.FromSeconds(1);

        private byte currentHeartrate = 0;
        private DateTimeOffset lastHeartrateTime;

        private BluetoothConnectionManager connectionManager;
        private WebsocketHeartrateServer wsServer;

        private bool IsReceiving => connectionManager.IsConnected && lastHeartrateTime + heartrateTimeout >= DateTimeOffset.Now;

        public BluetoothHeartrateModule()
        {
            connectionManager = new BluetoothConnectionManager(this);
            wsServer = new WebsocketHeartrateServer(this);
        }

        internal new void Log(string message)
        {
            base.Log(message);
        }
        internal new void ChangeStateTo(Enum lookup)
        {
            base.ChangeStateTo(lookup);
        }

        protected override void CreateAttributes()
        {
            CreateSetting(BluetoothHeartrateSetting.DeviceMac, "Device MAC address", "MAC address of the Bluetooth heartrate monitor", string.Empty);

            CreateSetting(BluetoothHeartrateSetting.NormalisedLowerbound, @"Normalised Lowerbound", @"The lower bound BPM the normalised parameter should use", 0);
            CreateSetting(BluetoothHeartrateSetting.NormalisedUpperbound, @"Normalised Upperbound", @"The upper bound BPM the normalised parameter should use", 240);
            CreateSetting(BluetoothHeartrateSetting.WebsocketServerEnabled, @"Websocket Server Enabled", @"Broadcast the heartrate data over a local Websocket server", false);
            CreateSetting(BluetoothHeartrateSetting.WebsocketServerHost, @"Websocket Server Hostname", @"Hostname (IP address) for the Websocket server", "127.0.0.1", () => GetSetting<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled));
            CreateSetting(BluetoothHeartrateSetting.WebsocketServerPort, @"Websocket Server Port", @"Port for the Websocket server", 36210, () => GetSetting<bool>(BluetoothHeartrateSetting.WebsocketServerEnabled));

            CreateParameter<bool>(BluetoothHeartrateParameter.Enabled, ParameterMode.Write, "VRCOSC/Heartrate/Enabled", "Enabled", "Whether this module is attempting to emit values");
            CreateParameter<float>(BluetoothHeartrateParameter.Normalised, ParameterMode.Write, "VRCOSC/Heartrate/Normalised", "Normalised", "The heartrate value normalised to 240bpm");
            CreateParameter<float>(BluetoothHeartrateParameter.Units, ParameterMode.Write, "VRCOSC/Heartrate/Units", "Units", "The units digit 0-9 mapped to a float");
            CreateParameter<float>(BluetoothHeartrateParameter.Tens, ParameterMode.Write, "VRCOSC/Heartrate/Tens", "Tens", "The tens digit 0-9 mapped to a float");
            CreateParameter<float>(BluetoothHeartrateParameter.Hundreds, ParameterMode.Write, "VRCOSC/Heartrate/Hundreds", "Hundreds", "The hundreds digit 0-9 mapped to a float");

            CreateVariable(BluetoothHeartrateVariable.Heartrate, @"Heartrate", @"hr");

            CreateState(BluetoothHeartrateState.Default, @"Default", $@"Heartrate/v{GetVariableFormat(BluetoothHeartrateVariable.Heartrate)} bpm");
        }

        protected override async void OnModuleStart()
        {
            ResetModuleState();
            connectionManager.StartWatcher();
            await wsServer.Start();
        }

        internal void ResetModuleState()
        {
            connectionManager.Reset();
            currentHeartrate = 0;
            lastHeartrateTime = DateTimeOffset.MinValue;
            ChangeStateTo(BluetoothHeartrateState.Default);
        }

        internal string GetDeviceMacSetting()
        {
            return GetSetting<string>(BluetoothHeartrateSetting.DeviceMac);
        }
        internal int GetNormalisedLowerboundSetting()
        {
            return GetSetting<int>(BluetoothHeartrateSetting.NormalisedLowerbound);
        }
        internal int GetNormalisedUpperboundSetting()
        {
            return GetSetting<int>(BluetoothHeartrateSetting.NormalisedUpperbound);
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

        internal void UpdateHeartrate(byte heartRate)
        {
            currentHeartrate = heartRate;
            lastHeartrateTime = DateTimeOffset.Now;
        }

        protected override async void OnModuleUpdate()
        {
            SendParameters();
            await wsServer.SendByteMessage(currentHeartrate);
        }

        internal void SendParameters()
        {
            SendParameter(BluetoothHeartrateParameter.Enabled, IsReceiving);

            if (IsReceiving)
            {
                var normalisedHeartRate = Map(currentHeartrate, GetNormalisedLowerboundSetting(), GetNormalisedUpperboundSetting(), 0, 1);
                var individualValues = Converter.ToDigitArray(currentHeartrate, 3);

                SendParameter(BluetoothHeartrateParameter.Normalised, normalisedHeartRate);
                SendParameter(BluetoothHeartrateParameter.Units, individualValues[2] / 10f);
                SendParameter(BluetoothHeartrateParameter.Tens, individualValues[1] / 10f);
                SendParameter(BluetoothHeartrateParameter.Hundreds, individualValues[0] / 10f);
                SetVariableValue(BluetoothHeartrateVariable.Heartrate, currentHeartrate.ToString());
            }
            else
            {
                SendParameter(BluetoothHeartrateParameter.Normalised, 0);
                SendParameter(BluetoothHeartrateParameter.Units, 0);
                SendParameter(BluetoothHeartrateParameter.Tens, 0);
                SendParameter(BluetoothHeartrateParameter.Hundreds, 0);
                SetVariableValue(BluetoothHeartrateVariable.Heartrate, @"0");
            }
        }

        protected override void OnModuleStop()
        {
            connectionManager.Reset();
            wsServer.Stop();
        }

        internal enum BluetoothHeartrateSetting
        {
            DeviceMac,
            NormalisedLowerbound,
            NormalisedUpperbound,
            WebsocketServerEnabled,
            WebsocketServerHost,
            WebsocketServerPort
        }

        internal enum BluetoothHeartrateParameter
        {
            Enabled,
            Normalised,
            Units,
            Tens,
            Hundreds
        }

        internal enum BluetoothHeartrateVariable
        {
            Heartrate
        }

        internal enum BluetoothHeartrateState
        {
            Default,
            Connecting,
            Connected,
            Disconnected
        }
    }
}
