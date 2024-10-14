using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace BluetoothHeartrateModule.UI
{
    partial class BluetoothHeartrateRuntimeView: UserControl
    {

        public BluetoothHeartrateModule Module { get; }
        public ObservableCollection<string> Devices { get; } = new();

        public BluetoothHeartrateRuntimeView(BluetoothHeartrateModule module)
        {
            Module = module;
            InitializeComponent();

            DataContext = this;

            Module.OnDeviceListUpdate += HandleDeviceListUpdate;
        }

        private void HandleDeviceListUpdate(string[] obj)
        {
            Devices.Clear();

            foreach (string mac in obj)
            {
                Devices.Add(mac);
            }
        }
    }
}
