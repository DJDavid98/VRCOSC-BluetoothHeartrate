using org.mariuszgromada.math.mxparser.syntaxchecker;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace BluetoothHeartrateModule
{
    public class DeviceData
    {
        public string MacAddress { get; }

        public string Name { get; set; } = "";

        public string Manufacturer { get; }
        public bool ShowManufacturer { get; }
        public bool IsVirtual { get; }

        public SolidColorBrush StatusColor { get; private set; } = DefaultBrush;
        public Visibility StatusDisplay { get { return StatusColor == DefaultBrush ? Visibility.Collapsed : Visibility.Visible; } }
        public DateTime LastAdvertisementDateTime = DateTime.Now;

        const double INACTIVE_AFTER_SECONDS = 60 * 5;
        private static SolidColorBrush ConnectedBrush = new SolidColorBrush(Color.FromRgb(0, 128, 0));
        private static SolidColorBrush ProcessingBrush = new SolidColorBrush(Color.FromRgb(128, 64, 0));
        private static SolidColorBrush DefaultBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));

        private DeviceDataManager mgr;


        public string Label
        {
            get
            {
                List<string> parts = new();
                if (Name != string.Empty)
                {
                    parts.Add(Name);
                }
                if (MacAddress != string.Empty)
                {
                    parts.Add(parts.Count == 0 ? MacAddress : $"({MacAddress})");
                }
                if (parts.Count == 0)
                {
                    return "Unknown device";
                }
                return String.Join(" ", parts);
            }
        }
        public DeviceData(string mac, DeviceDataManager mgr, bool isVirtual = false)
        {
            this.mgr = mgr;
            MacAddress = mac;
            IsVirtual = isVirtual;

            var macPrefix = MacAddress != string.Empty ? MacAddress.Substring(0, 8) : "";
            this.Manufacturer = this.mgr.prefixData.ContainsKey(macPrefix) ? this.mgr.prefixData[macPrefix] : string.Empty;
            this.ShowManufacturer = this.Manufacturer != string.Empty;

            ConnectedBrush.Freeze();
            ProcessingBrush.Freeze();
            DefaultBrush.Freeze();
        }

        public double GetSecondsSinceLastAdvertisement()
        {
            long lastAdvertisementTimestamp = ((DateTimeOffset)LastAdvertisementDateTime).ToUnixTimeSeconds();
            long currentTimestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds();
            return Convert.ToDouble(currentTimestamp - lastAdvertisementTimestamp);
        }

        public bool GetIsInactive()
        {
            return GetSecondsSinceLastAdvertisement() < INACTIVE_AFTER_SECONDS;
        }
        public void UpdateStatusColor()
        {
            if (IsVirtual)
            {
                StatusColor = DefaultBrush;
                return;
            }
            if (GetIsConnected())
            {
                StatusColor = ConnectedBrush;
                return;
            }
            if (GetIsProcessing())
            {
                StatusColor = ProcessingBrush;
                return;
            }

            double fadeProgress = Math.Min(1f, GetSecondsSinceLastAdvertisement() / INACTIVE_AFTER_SECONDS);
            StatusColor = new SolidColorBrush(Color.FromRgb(
                Convert.ToByte(128 * fadeProgress),
                Convert.ToByte(128 * fadeProgress),
                Convert.ToByte(255 - (128 * fadeProgress))
            ));
        }

        private bool GetIsConnected()
        {
            return MacAddress == mgr.ConnectedDeviceMac;
        }

        private bool GetIsProcessing()
        {
            return MacAddress == mgr.ProcessingDeviceMac;
        }
    }
}
