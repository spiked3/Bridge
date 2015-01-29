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
using System.Windows.Threading;

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

        readonly TimeSpan blinkTime = new TimeSpan(0, 0, 0, 0, 100);

        int ReceiveActivityCountdown = 0;
        int TransmitActivityCountdown = 0;

        public Dispatcher DispatcherToUse { get; set; }
        public string Name { get { return _Name; } set { _Name = value; OnPropertyChanged(); } } string _Name;
        public Brush ReceiveBrush { get { return _ReceiveBrush; } set { _ReceiveBrush = value; OnPropertyChanged(); } } Brush _ReceiveBrush = Brushes.DarkRed;
        public Brush TransmitBrush { get { return _TransmitBrush; } set { _TransmitBrush = value; OnPropertyChanged(); } } Brush _TransmitBrush = Brushes.DarkRed;
        public SerialPort SerialPort { get { return _SerialPort; } set { _SerialPort = value; OnPropertyChanged(); } } SerialPort _SerialPort;

        public ComportItem(SerialPort s, Dispatcher d)
        {
            DispatcherTimer t = new DispatcherTimer(blinkTime, DispatcherPriority.Normal, TimerTick, d);
            t.Start();
        }

        void TimerTick(object sender, EventArgs e)
        {
            if (--ReceiveActivityCountdown < 1)
            {
                ReceiveBrush = Brushes.DarkRed;
                ReceiveActivityCountdown = 0;
            }
            if (--TransmitActivityCountdown < 1)
            {
                TransmitBrush = Brushes.DarkRed;
                TransmitActivityCountdown = 0;
            }
        }

        public void ReceiveActivity()
        {
            ReceiveBrush = _ReceiveBrush.Equals(Brushes.DarkRed) ? Brushes.Red : Brushes.DarkRed;
            ReceiveActivityCountdown = 2;
        }

        public void TransmitActivity()
        {
            TransmitBrush = _TransmitBrush.Equals(Brushes.DarkRed) ? Brushes.Red : Brushes.DarkRed;
            TransmitActivityCountdown = 2;
        }

    }
}
