using System;
using System.Collections.Generic;
using System.Windows;
using System.IO.Ports;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SerialToMqtt2
{
    public class ComportItem : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] String T = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(T));
        }

        #endregion

        public string Name { get { return _Name; } set { _Name = value; OnPropertyChanged(); } } string _Name;
        public Brush ReceiveBrush { get { return _ReceiveBrush; } set { _ReceiveBrush = value; OnPropertyChanged(); } } Brush _ReceiveBrush = Brushes.DarkRed;
        public Brush TransmitBrush { get { return _TransmitBrush; } set { _TransmitBrush = value; OnPropertyChanged(); } } Brush _TransmitBrush = Brushes.DarkRed;
        public SerialPort SerialPort { get { return _SerialPort; } set { _SerialPort = value; OnPropertyChanged(); } } SerialPort _SerialPort;

        public void ReceiveActivity()
        {
            ReceiveBrush = _ReceiveBrush.Equals(Brushes.DarkRed) ? Brushes.Red : Brushes.DarkRed;
        }

        public void TransmitActivity()
        {
            TransmitBrush = _TransmitBrush.Equals(Brushes.DarkRed) ? Brushes.Red : Brushes.DarkRed;
        }

    }
}
