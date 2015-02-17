using spiked3;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
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
        // todo port closing drops subscriptions
        // todo sequence and CRC support on serial link
        // todo make ports to monitor a parameter

        public ObservableCollection<ComportItem> ComPortItems { get; set; }

        private byte[] CompPortsToMonitor = { 3, 4, 5, 14 };

        private Dictionary<string, List<SerialPort>> TopicListeners = new Dictionary<string, List<SerialPort>>();
        private Dictionary<SerialPort, ComportItem> ComPortItemsDictionary = new Dictionary<SerialPort, ComportItem>();

        private const string Broker = "127.0.0.1";
        public MqttClient Mqtt;

        public MainWindow()
        {
            ComPortItems = new ObservableCollection<ComportItem>();
            InitializeComponent();

            Width = Settings1.Default.Width;
            Height = Settings1.Default.Height;
            Top = Settings1.Default.Top;
            Left = Settings1.Default.Left;

            if (Width == 0 || Height == 0)
            {
                Width = 640;
                Height = 480;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            spiked3.Console.MessageLevel = 1;

            Trace.WriteLine("MQTT to Serial Bridge 2/WPF © 2015 Spiked3.com", "+");

            Trace.WriteLine("Connecting to broker ...", "1");
            Mqtt = new MqttClient(Broker);
            Mqtt.Connect("pcbridge");
            Trace.WriteLine(string.Format("...Connected to {0}", Mqtt.ProtocolVersion.ToString()), "1");

            Mqtt.MqttMsgPublishReceived += MqttMsgPublishReceived;

            StartSerial();
        }

        private void StopSerial()
        {
            foreach (SerialPort s in ComPortItemsDictionary.Keys)
                if (s.IsOpen)
                {
                    s.DataReceived -= Serial_DataReceived;
                    s.Close();
                    Trace.WriteLine(string.Format("Closed {0}", s.PortName), "1");
                }
            ComPortItemsDictionary.Clear();
            TopicListeners.Clear();
            ComPortItems.Clear();
        }

        private void StartSerial()
        {
            StopSerial();
            foreach (var p in CompPortsToMonitor)
            {
                SerialPort s;
                try
                {
                    s = new SerialPort("COM" + p.ToString(), 115200, Parity.None, 8, StopBits.One);
                    s.Open();
                }
                catch (Exception)
                {
                    Trace.WriteLine(string.Format("Serial port {0} failed to open, skipping.", p), "1");
                    continue;
                }

                s.ReadTimeout = 1000;
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

            Debug.Assert(ComPortItemsDictionary.ContainsKey(p));

            ComportItem i = ComPortItemsDictionary[p];
            i.ReceiveActivity();

            while (p.BytesToRead > 0)
            {
                string line;
                try
                {
                    line = p.ReadLine();
                    line = line.Trim(new char[] { '\r', 'n' });     // not supposed to be there, throw away if they are
                }
                catch (TimeoutException)
                {
                    Trace.WriteLine("timeout on ReadLine()", "warn");
                    break;
                }
                catch (System.IO.IOException)
                {
                    break;
                }

                // within a line we expect the first byte to be a sequence 1 greater than the previous
                // and the last byte to be a CRC

                // TODO

                // todo line = line.Substring(1,line-length - 2);

                // comments ok, echo'd
                if (line.StartsWith("//"))
                {
                    Trace.WriteLine(line);
                    continue;
                }

                if (line.Length < 4)
                {
                    Trace.WriteLine(string.Format("{0} framing error", p.PortName), "warn");
                    continue;
                }

                // todo needs a thorough test
                // parse it, it must be CMDtopic[/subTopic][{json}]
                try
                {
                    string topic, subTopic = "N/A";
                    byte[] payload = null;

                    bool hasPayload = false, hasSub = false;
                    int topicEnd, subEnd;

                    int subStart = line.IndexOf('/', 4);
                    int payloadStart = line.IndexOf('{', 4);

                    if (payloadStart != -1)
                    {
                        int payloadEnd = line.IndexOf('}', 4);
                        if (payloadEnd == -1)
                        {
                            Trace.WriteLine("Improperly formatted payload, msg discarded", "warn");
                            return;
                        }
                        payload = line.Substring(payloadStart, payloadEnd - payloadStart + 1).ToBytes();
                        hasPayload = true;
                    }

                    if (subStart != -1)
                    {
                        subEnd = hasPayload ? payloadStart - 1 : line.Length;
                        subTopic = line.Substring(subStart + 1, subEnd - subStart);
                        hasSub = true;
                    }

                    topicEnd = hasSub ? subStart - 1 : hasPayload ? payloadStart - 1 : line.Length - 1;
                    topic = line.Substring(3, topicEnd - 2);

                    var x = spiked3.extensions.HexDump(payload, 32).Trim(new char[] { '\r', '\n' });
                    Trace.WriteLine(string.Format("{0} incoming {1}/{2} |{3}|", p.PortName, topic, subTopic, x), "3");

                    switch (line.Substring(0, 3))
                    {
                        case "PUB":
                            Dispatcher.InvokeAsync(() =>
                            {
                                Trace.WriteLine(string.Format("DataReceived {0}, Publish topic({1})", p.PortName, topic), "2");
                                StringBuilder b = new StringBuilder();
                                b.Append(topic);
                                if (hasSub)
                                    b.Append('/' + subTopic);
                                Mqtt.Publish(b.ToString(), payload, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false); // or QOS_LEVEL_AT_LEAST_ONCE
                            });
                            break;

                        case "SUB":
                            Trace.WriteLine(string.Format("{0} Subscribed topic({1})", p.PortName, topic), "1");
                            if (!TopicListeners.ContainsKey(topic))
                            {
                                TopicListeners.Add(topic, new List<SerialPort>());
                                Mqtt.Subscribe(new string[] { topic + "/#" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                            }
                            TopicListeners[topic].Add(p);
                            break;

                        case "UNS":
                            System.Diagnostics.Debugger.Break();
                            topic = line.Substring(3);
                            Trace.WriteLine(string.Format("{0} Unsubscribed topic({1})", p.PortName, topic), "1");

                            if (TopicListeners.ContainsKey(topic))
                            {
                                for (int j = 0; j < TopicListeners[topic].Count; j++)
                                    TopicListeners[topic].Remove(p);
                                if (TopicListeners[topic].Count < 1)    // if no listeners remain
                                {
                                    Mqtt.Unsubscribe(new string[] { topic });
                                    Trace.WriteLine(string.Format("Bridge Unsubscribed topic({0})", topic), "1");
                                }
                            }
                            break;

                        default:
                            Trace.WriteLine(string.Format("{0}, Unkown data error", p.PortName), "warn");
                            break;
                    }
                }
                catch (Exception ex) // errors make it through - ignore
                {
                    Trace.WriteLine(string.Format("{0}, Rcv exception {1} ignored", p.PortName, ex.Message), "warn");
                }
            }
        }

        private void MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            foreach (var key in TopicListeners.Keys)
                if (e.Topic.StartsWith(key))
                    Dispatcher.InvokeAsync(() =>
                    {
                        Trace.WriteLine(string.Format("Found subscriber list for {0}", e.Topic), "2");
                        foreach (var p in TopicListeners[key])
                        {
                            Trace.WriteLine(string.Format("..MqttPublish {0} / {1}", p.PortName, e.Topic), "2");
                            Trace.WriteLine(spiked3.extensions.HexDump(e.Message, 32), "3");
                            ComPortItemsDictionary[p].TransmitActivity();
                            p.Write(e.Topic);
                            p.Write(e.Message, 0, e.Message.Length);
                            p.Write("\n");
                        }
                    });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopSerial();

            if (Mqtt != null && Mqtt.IsConnected)
                Mqtt.Disconnect();

            Settings1.Default.Width = (float)((Window)sender).Width;
            Settings1.Default.Height = (float)((Window)sender).Height;
            Settings1.Default.Top = (float)((Window)sender).Top;
            Settings1.Default.Left = (float)((Window)sender).Left;
            Settings1.Default.Save();
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

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            Console1.Test();
        }
    }
}