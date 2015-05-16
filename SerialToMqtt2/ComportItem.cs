using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace SerialToMqtt2
{
    public class ComportItem : INotifyPropertyChanged, IDisposable
    {
        //+++ should be disposable to kill timer? (but timer doesnt have a dispose)

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] String T = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(T));
        }

        #endregion INotifyPropertyChanged

        private readonly TimeSpan LightsOffDelay = new TimeSpan(0, 0, 0, 0, 100);

        private DateTime? ReceiveLightOffAt;
        private DateTime? TransmitLightOffAt;

        public string Name { get { return _Name; } set { _Name = value; OnPropertyChanged(); } } private string _Name;

        public string Topic { get { return _Topic; } set { _Topic = value; OnPropertyChanged(); } } private string _Topic;

        public Brush ReceiveBrush { get { return _ReceiveBrush; } set { _ReceiveBrush = value; OnPropertyChanged(); } } private Brush _ReceiveBrush = Brushes.DarkRed;

        public Brush TransmitBrush { get { return _TransmitBrush; } set { _TransmitBrush = value; OnPropertyChanged(); } } private Brush _TransmitBrush = Brushes.DarkRed;

        public SerialPort SerialPort { get { return _SerialPort; } set { _SerialPort = value; OnPropertyChanged(); } } private SerialPort _SerialPort;

        public DispatcherTimer DispatchTimer { get; set; }

        public int LastSequence { get; set; }

        public ComportItem(SerialPort s, Dispatcher d)
        {
            SerialPort = s;
            DispatchTimer = new DispatcherTimer(LightsOffDelay, DispatcherPriority.Normal, TimerTick, d);
            DispatchTimer.Start();
        }

        public int RecvIdx { get; set; }
        public byte[] RecieveBuff = new byte[1024];

        private void TimerTick(object sender, EventArgs e)
        {
            if (ReceiveLightOffAt.HasValue && DateTime.Now > ReceiveLightOffAt.Value)
            {
                ReceiveBrush = Brushes.DarkRed;
                ReceiveLightOffAt = null;
            }

            if (TransmitLightOffAt.HasValue && DateTime.Now > TransmitLightOffAt.Value)
            {
                TransmitBrush = Brushes.DarkRed;
                TransmitLightOffAt = null;
            }
        }

        public void ReceiveActivity()
        {
            ReceiveBrush = _ReceiveBrush.Equals(Brushes.DarkRed) ? Brushes.Red : Brushes.DarkRed;
            ReceiveLightOffAt = DateTime.Now + LightsOffDelay;
        }

        public void TransmitActivity()
        {
            TransmitBrush = _TransmitBrush.Equals(Brushes.DarkRed) ? Brushes.Red : Brushes.DarkRed;
            TransmitLightOffAt = DateTime.Now + LightsOffDelay;
        }

        public void Dispose()
        {
            DispatchTimer.Stop();
            DispatchTimer = null;
        }
    }
}