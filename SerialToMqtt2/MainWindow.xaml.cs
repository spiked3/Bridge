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
        // +++port closing drops subscriptions

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

        private byte[] CompPortsToMonitor = { 3, 4, 5, 14 };     // +++ make as a parameter

        private Dictionary<string, List<SerialPort>> TopicListeners = new Dictionary<string, List<SerialPort>>();
        private Dictionary<SerialPort, ComportItem> ComPortItemsDictionary = new Dictionary<SerialPort, ComportItem>();

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

            Mqtt.Connect("pcbridge");
            Trace.WriteLine("...Connected");

            Mqtt.MqttMsgPublishReceived += MqttMsgPublishReceived;

            StartSerial();
        }

        private void StopSerial()
        {
            foreach (SerialPort s in ComPortItemsDictionary.Keys)
                if (s.IsOpen)
                {
                    s.Close();
                    Trace.WriteLine(string.Format("Closed {0}", s.PortName));
                }
            ComPortItemsDictionary.Clear();
            TopicListeners.Clear();
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
                ComportItem ci = new ComportItem(s, Dispatcher);
                ComPortItems.Add(ci);
                ComPortItemsDictionary.Add(s, ci);

                Trace.WriteLine(string.Format("Serial port {0} opened.", p));
            }
        }

        private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort p = sender as SerialPort;
            if (ComPortItemsDictionary.ContainsKey(p))
            {
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
                            Trace.WriteLine(string.Format("{0} Subscribed topic({1})", p.PortName, topic));
                            if (!TopicListeners.ContainsKey(topic))
                            {
                                TopicListeners.Add(topic, new List<SerialPort>());
                                Mqtt.Subscribe(new string[] { topic + "/#" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                            }
                            TopicListeners[topic].Add(p);
                        }
                        else if (line.Substring(0, 3).Equals("UNS"))
                        {
                            string topic = line.Substring(3);
                            Trace.WriteLine(string.Format("{0} Unsubscribed topic({1})", p.PortName, topic));

                            if (TopicListeners.ContainsKey(topic))
                            {
                                for (int j = 0; j < TopicListeners[topic].Count; j++)
                                    TopicListeners[topic].Remove(p);
                                if (TopicListeners[topic].Count < 1)
                                {
                                    Mqtt.Unsubscribe(new string[] { topic });
                                    Trace.WriteLine(string.Format("Bridge Unsubscribed topic({0})", topic));
                                }
                            }
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
        }

        private void MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            foreach (var key in TopicListeners.Keys)
            {
                if (e.Topic.StartsWith(key))
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        Trace.WriteLine(string.Format("Found subscriber list for {0}", e.Topic), "-");
                        foreach (var p in TopicListeners[key])
                        {
                            if (ShowDetails ?? false)
                                Trace.WriteLine(string.Format("...pushing to {0}", p.PortName), "-");
                            ComPortItemsDictionary[p].TransmitActivity();
                            p.Write("!!!!\n");
                        }
                    });
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