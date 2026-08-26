using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Diver_RaT
{
    public class Device : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _computerName = string.Empty;
        private string _ipAddress = string.Empty;
        private string _os = string.Empty;
        private string _username = string.Empty;
        private string _country = string.Empty;
        private string _lastSeen = string.Empty;
        private string _deviceType = "Desktop";
        private bool _isOnline;
        private string _id = Guid.NewGuid().ToString("N");

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string ComputerName
        {
            get => _computerName;
            set { _computerName = value; OnPropertyChanged(); }
        }

        public string IpAddress
        {
            get => _ipAddress;
            set { _ipAddress = value; OnPropertyChanged(); }
        }

        public string OS
        {
            get => _os;
            set { _os = value; OnPropertyChanged(); }
        }

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string Country
        {
            get => _country;
            set { _country = value; OnPropertyChanged(); }
        }

        public string LastSeen
        {
            get => _lastSeen;
            set { _lastSeen = value; OnPropertyChanged(); }
        }

        public string DeviceType
        {
            get => _deviceType;
            set { _deviceType = value; OnPropertyChanged(); }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set { _isOnline = value; OnPropertyChanged(); }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
