using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls.Ribbon;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace SerialToMqtt2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : RibbonWindow
    {
        // provide serial to mqtt bridge
        //   clients invisioned are APIs for arduino and netduino, and maybe LEGO RobotC
        // attempt to open each specified com port, dont freak out if fails
        // listen on ports sucessfully opened
        // accept pub, sub, unsub commands
        // port closing drops subscriptions

        // I looked at mqtt-sn a bit, and it changes mqtt, with a focus on wireless, I dont want to do that.
        // I am using wired serial connections, and would prefer to just drop in something very mqtt like on the client side.

        public ObservableCollection<ComportItem> ComPortItems { get; set; }

        public bool? ShowDetails
        {
            get { return (bool?)GetValue(ShowDetailsProperty); }
            set { SetValue(ShowDetailsProperty, value); }
        }
        public static readonly DependencyProperty ShowDetailsProperty =
            DependencyProperty.Register("ShowDetails", typeof(bool?), typeof(MainWindow), new PropertyMetadata(false));

        byte[] CompPortsToMonitor = { 3, 4, 5, 14 };     // +++ make as a parameter

        Dictionary<string, List<SerialPort>> TopicListeners = new Dictionary<string, List<SerialPort>>();
        Dictionary<SerialPort, ComportItem> ComPortItemsDictionary = new Dictionary<SerialPort, ComportItem>();

        private const string Broker = "127.0.0.1";
        public MqttClient Mqtt;

        public MainWindow()
        {
            ComPortItems = new ObservableCollection<ComportItem>();
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("MQTT to Serial Bridge 2/WPF © 2015 Spiked3.com", "+");

            Trace.WriteLine("Connecting to broker ...");
            Mqtt = new MqttClient(Broker);
            Mqtt.MqttMsgPublishReceived += Mqtt_MqttMsgPublishReceived;
            Mqtt.Connect("pcbridge");
            Trace.WriteLine("...Connected");

            StartSerial();

            // +++ temp
            Mqtt.Subscribe(new string[] { "Test/#" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
        }

        void StopSerial()
        {
            foreach (ComportItem ci in ComPortItems)
                if (ci.SerialPort != null && ci.SerialPort.IsOpen)
                    ci.SerialPort.Close();

            ComPortItems.Clear();
        }

        private void StartSerial()
        {
            SerialPort s;

            StopSerial();

            foreach (var p in CompPortsToMonitor)
            {
                try
                {
                    s = new SerialPort("COM" + p.ToString(), 57600, Parity.None, 8, StopBits.One);
                    s.Open();
                }
                catch (Exception)
                {
                    Trace.WriteLine(string.Format("Serial port {0} failed to open, skipping.", p));
                    continue;
                }

                s.ReadTimeout = 100;
                s.DataReceived += Serial_DataReceived;
                ComportItem ci = new ComportItem { SerialPort = s };
                ComPortItems.Add(ci);
                ComPortItemsDictionary.Add(s, ci);

                Trace.WriteLine(string.Format("Serial port {0} opened.", p));
            }
        }

        private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort p = sender as SerialPort;
            ComportItem i = ComPortItemsDictionary[p];

            i.ReceiveActivity();

            while (p.BytesToRead > 0)
            {
                string line;
                try
                {
                    line = p.ReadLine();
                }
                catch (TimeoutException)
                {
                    Trace.WriteLine("timeout on ReadLine()", "warn");
                    return;
                }
                catch (System.IO.IOException)
                {
                    return;
                }

                try
                {   // parse it, it should be CMDtopic[,payload]
                    if (line.Substring(0, 3).Equals("PUB"))
                    {
                        int commaIdx = line.IndexOf(',', 3);
                        string topic = line.Substring(3, commaIdx - 3);
                        string payload = line.Substring(commaIdx + 1);
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (ShowDetails ?? false)
                                Trace.WriteLine(string.Format("{0}, Publish topic({1}) payload({2})", p.PortName, topic, payload));
                            Mqtt.Publish(topic, System.Text.Encoding.UTF8.GetBytes(payload));
                        });
                    }
                    else if (line.Substring(0, 3).Equals("SUB"))
                    {
                        string topic = line.Substring(3);
                        Trace.WriteLine(string.Format("{0}, Subscribe topic({1})", p.PortName, topic));
                        if (!TopicListeners.ContainsKey(topic))
                            TopicListeners.Add(topic, new List<SerialPort>());
                        TopicListeners[topic].Add(p);
                        Mqtt.Subscribe(new string[] { topic }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                    }
                    else
                        Trace.WriteLine(string.Format("{0}, Unkown data error", p.PortName), "warn");
                }
                catch (Exception ex)
                {   // errors make it through - ignore                    
                    Trace.WriteLine(string.Format("{0}, Rcv exception: {1}", p.PortName, ex.Message), "warn");
                }
            }
        }

        private void Mqtt_MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            foreach (var key in TopicListeners.Keys)
            {
                if (e.Topic.StartsWith(key))
                {
                    foreach (var p in TopicListeners[key])      // +++treat all subs as wildcards??? need to rethink
                        p.Write("!!!\n");
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopSerial();

            if (Mqtt != null && Mqtt.IsConnected)
                Mqtt.Disconnect();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            StartSerial();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopSerial();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            spiked3.Console.ClearConsole();
        }
    }
}