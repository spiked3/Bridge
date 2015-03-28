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
using Newtonsoft.Json;


namespace SerialToMqtt2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : RibbonWindow
    {
        // attempt to open each specified com port, dont freak out if fails
        // listen on ports sucessfully opened
        // accept // comment, SUB:, UNS: commands, anything else, publish
        // todo port closing drops subscriptions
        // todo sequence and CRC support on serial link
        // todo make ports to monitor a parameter

        public ObservableCollection<ComportItem> ComPortItems { get; set; }

        private string[] CompPortsToMonitor = { "com14" };
        private int[] ComPortsBaud = { 9600 };

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
            spiked3.Console.MessageLevel = 4;   // default

            Trace.WriteLine("MQTT to Serial Bridge 2.1/WPF © 2015 Spiked3.com", "+");

            Trace.WriteLine("Connecting to broker ...", "1");
            Mqtt = new MqttClient(Broker);
            Mqtt.Connect("pcbridge");
            Trace.WriteLine(string.Format("...Connected to {0}", Mqtt.ProtocolVersion.ToString()), "1");

            Mqtt.MqttMsgPublishReceived += MqttMsgPublishReceived;

            StartSerial();
        }

        private void StopSerial()
        {
            Trace.WriteLine("StopSerial", "2");
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
            Trace.WriteLine("StartSerial", "2");
            for (int i = 0; i < CompPortsToMonitor.Length; i++)
            {
                SerialPort s;
                try
                {
                    s = new SerialPort(CompPortsToMonitor[i], ComPortsBaud[i], Parity.None, 8, StopBits.One);
                    s.Open();
                }
                catch (Exception)
                {
                    Trace.WriteLine(string.Format("Serial port {0} failed to open, skipping.", CompPortsToMonitor[i]), "1");
                    continue;
                }

                s.ReadTimeout = 1000;
                s.DataReceived += Serial_DataReceived;
                ComportItem ci = new ComportItem(s, Dispatcher);
                ComPortItems.Add(ci);
                ComPortItemsDictionary.Add(s, ci);
                Trace.WriteLine(string.Format("Serial port {0} opened.", CompPortsToMonitor[i]));
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
                    line = line.Trim(new char[] { '\r', '\n' });     // not supposed to be there, throw away if they are
                }
                catch (TimeoutException)
                {
                    Trace.WriteLine("timeout on ReadLine()", "warn");
                    break;
                }
                catch (System.IO.IOException ioe)
                {
                    Trace.WriteLine("System.IO.IOException: " + ioe.Message, "warn");
                    break;
                }

                // comments ok, echo'd
                if (line.StartsWith("//"))
                {
                    Trace.WriteLine(line);
                    continue;
                }

                if (line.Length < 3)
                {
                    Trace.WriteLine(string.Format("{0} framing error", p.PortName), "warn");
                    // attempt to recover
                    p.ReadTo("\n");
                    continue;
                }

                if (line.StartsWith("SUB:"))
                {
                    try
                    {
                        string Topic = line.Substring(4);
                        if (!TopicListeners.ContainsKey(Topic))
                        {
                            TopicListeners.Add(Topic, new List<SerialPort>());
                            Mqtt.Subscribe(new string[] { Topic }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                        }
                        TopicListeners[Topic].Add(p);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("recv Exception-> " + line, "error");
                        Trace.WriteLine(ex.Message, "2");
                    }
                }
                else
                {
                    try
                    {
                        dynamic j = JsonConvert.DeserializeObject(line);
                        Mqtt.Publish((string)j.Topic, UTF8Encoding.ASCII.GetBytes(line));
                        Trace.WriteLine("pub-> " + line, "3");
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("recv Exception-> " + line, "error");
                        Trace.WriteLine(ex.Message, "2");
                    }
                }


                // +++ unsubscribe
                //        case "UNS":
                //            System.Diagnostics.Debugger.Break();
                //            topic = line.Substring(3);
                //            Trace.WriteLine(string.Format("{0} Unsubscribed topic({1})", p.PortName, topic), "1");
                //            if (TopicListeners.ContainsKey(topic))
                //            {
                //                for (int j = 0; j < TopicListeners[topic].Count; j++)
                //                    TopicListeners[topic].Remove(p);
                //                if (TopicListeners[topic].Count < 1)    // if no listeners remain
                //                {
                //                    Mqtt.Unsubscribe(new string[] { topic });
                //                    Trace.WriteLine(string.Format("Bridge Unsubscribed topic({0})", topic), "1");
                //                }
                //            }
                //            break;
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