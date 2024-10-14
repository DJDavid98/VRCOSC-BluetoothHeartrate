using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BluetoothHeartrateModule
{
    class DeviceDataManager
    {
        Dictionary<string, DeviceData> Devices = new();

        public Action<string[]>? OnDeviceListUpdate { get; internal set; }

        void Add(string mac)
        {
            Devices[mac] = new DeviceData(mac);

            OnDeviceListUpdate?.Invoke(Devices.Keys.ToArray());
        }

        void Remove(string mac)
        {
            Devices.Remove(mac);
            OnDeviceListUpdate?.Invoke(Devices.Keys.ToArray());
        }
    }

    struct DeviceData {
        public string MacAddress { get; private set; }

        public string Name { get; set; } = "";

        public DeviceData(string mac)
        {
            MacAddress = mac;
        }


    }
}
