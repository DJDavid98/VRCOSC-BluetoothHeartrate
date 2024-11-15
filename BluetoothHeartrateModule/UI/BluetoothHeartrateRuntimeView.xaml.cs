using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;

namespace BluetoothHeartrateModule.UI
{
    partial class BluetoothHeartrateRuntimeView: UserControl
    {

        public BluetoothHeartrateModule module { get; }
        public ObservableCollection<DeviceData> Devices { get; } = new();

        public bool DeviceReadOnly = true;
        public bool DeviceEditable = false;

        public BluetoothHeartrateRuntimeView(BluetoothHeartrateModule module)
        {
            this.module = module;
            InitializeComponent();

            DataContext = this;

            this.module.deviceDataManager.OnDeviceListUpdate += HandleDeviceListUpdate;
            HandleDeviceListUpdate();
        }

        private void HandleDeviceListUpdate()
        {
            Dispatcher.Invoke(new Action(() =>
            {
                DeviceData[] updatedDevices = module.deviceDataManager.GetDevices();
                var selectedDeviceMac = module.GetDeviceMacSetting();
                DeviceReadOnly = selectedDeviceMac != string.Empty;
                DeviceEditable = !DeviceReadOnly;
                var deviceCount = updatedDevices.Count();
                DeviceSelection.Watermark = $"{(deviceCount > 0 ? $"{deviceCount} " : "")}Device{(deviceCount != 1 ? "s" : "")}";
                Devices.Clear();

                var sortedDevices = new List<DeviceData>(updatedDevices);
                sortedDevices.Sort((a, b) =>
                {
                    if (a.GetIsInactive()) { return 1; }
                    if (b.GetIsInactive()) { return -1; }
                    return a.Label.CompareTo(b.Label);
                });
                bool selectedItemFound = false;
                foreach (var deviceData in sortedDevices)
                {
                    deviceData.UpdateStatusColor();
                    Devices.Add(deviceData);
                    if (!selectedItemFound && deviceData.MacAddress == selectedDeviceMac)
                    {
                        DeviceSelection.SelectedItem = deviceData;
                        selectedItemFound = true;
                    }
                }
                if (!selectedItemFound) {
                    var missingDevice = new DeviceData(selectedDeviceMac, module.deviceDataManager, true);
                    missingDevice.Name = selectedDeviceMac == string.Empty ? (string)DeviceSelection.Watermark : "Scanning for device…";
                    Devices.Add(missingDevice);
                    DeviceSelection.SelectedItem = missingDevice;
                }
            }));
        }


        private void DeviceSelection_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var selectedValue = (DeviceData)comboBox.SelectedValue;

            if (selectedValue != null)
            {
                module.SetDeviceMacSetting(selectedValue.MacAddress);
            }
        }

        private void Reset_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            module.ClearDeviceMacSetting();
        }

        private void DeviceSelection_LostMouseCapture(object sender, MouseEventArgs e)
        {

        }
    }
}
