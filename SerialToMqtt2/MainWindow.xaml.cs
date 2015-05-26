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
using System.Windows.Threading;

namespace SerialToMqtt2
{
    public partial class MainWindow : RibbonWindow
    {
        public ObservableCollection<ComportItem> ComPortItems { get; set; }

        private string[] CompPortsToMonitor = { "com3", "com4", "com14", "com15", "com7" };

        private Dictionary<string, List<SerialPort>> TopicListeners = new Dictionary<string, List<SerialPort>>();
        private Dictionary<SerialPort, ComportItem> ComPortItemsDictionary = new Dictionary<SerialPort, ComportItem>();

        private const string Broker = "127.0.0.1";
        public MqttClient Mqtt;

        Bagger Bagger;

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

            Trace.WriteLine("MQTT to Serial Bridge 2.2/WPF © 2015 Spiked3.com", "+");

            Trace.WriteLine("Connecting to broker ...", "1");
            Mqtt = new MqttClient(Broker);
            Mqtt.Connect("pcbridge");
            Trace.WriteLine(string.Format("...Connected to {0}", Mqtt.ProtocolVersion.ToString()), "1");

            Mqtt.MqttMsgPublishReceived += MqttMsgPublishReceived;

            StartSerial();
        }

        void Unsubscribe(SerialPort s)
        {
            foreach (string topic in TopicListeners.Keys)
            {
                TopicListeners[topic].Remove(s);
                if (TopicListeners[topic].Count == 0)
                {
                    Trace.WriteLine(string.Format("{0} unsubscribed {1}", s.PortName, topic), "2");
                    Mqtt.Unsubscribe(new[] { topic });
                }
            }
        }

        private void StopSerial()
        {
            Trace.WriteLine("StopSerial", "2");
            foreach (SerialPort s in ComPortItemsDictionary.Keys)
                if (s.IsOpen)
                {
                    Unsubscribe(s);
                    s.DiscardInBuffer();
                    s.Close();  // +++ frequently locks up
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
            foreach (var t in CompPortsToMonitor)
            {
                SerialPort s;
                try
                {
                    s = new SerialPort(t, 115200);

                    s.Open();
                }
                catch (Exception)
                {
                    Trace.WriteLine($"Serial port {t} failed to open, skipping.", "warn");
                    continue;
                }

                s.WriteTimeout = 500;
                ComportItem ci = new ComportItem(s, Dispatcher);
                ci.Topic = "robot1";
                ComPortItems.Add(ci);
                ComPortItemsDictionary.Add(s, ci);

                SerialHandler(ci);      // processes incoming

                Trace.WriteLine($"Serial port {t} opened.");

                string subscribeTopic = ci.Topic + "/Cmd";
                Mqtt.Subscribe(new string[] { subscribeTopic }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                TopicListeners.Add(subscribeTopic, new List<SerialPort>());
                Trace.WriteLine($"{s.PortName} listen {subscribeTopic}", "3");
                TopicListeners[subscribeTopic].Add(s);
            }
        }

        void ProcessLine(ComportItem ci, string line)
        {
            if (line.StartsWith("//"))
            {
                Trace.WriteLine(ci.SerialPort.PortName + "->" + line, "2");
                return;
            }
            if (line.Length < 3)
            {
                Trace.WriteLine($"{ci.SerialPort.PortName} framing error", "warn");
                
                // attempt to recover
                bool recovered = false;
                while (!recovered)
                {
                    Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                    try
                    {
                        ci.SerialPort.ReadTo("\n");
                        recovered = true;
                    }
                    catch (Exception)
                    { }
                }
                return;
            }
            else
            {
                Trace.WriteLine(ci.SerialPort.PortName + "->" + line, "3");
                try
                {
                    Trace.WriteLine("#PUB# " + line, "5");
                    Mqtt.Publish("robot1", UTF8Encoding.ASCII.GetBytes(line));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("recv Exception-> " + line, "error");
                    Trace.WriteLine(ex.Message, "2");
                }
            }
        }

        void AppSerialDataEvent(ComportItem ci, byte[] received)
        {
            Dispatcher.InvokeAsync(() => { ci.ReceiveActivity(); });
            foreach (var b in received)
            {
                if (b == '\n')
                {
                    ci.RecieveBuff[ci.RecvIdx] = 0;
                    string line = Encoding.UTF8.GetString(ci.RecieveBuff, 0, ci.RecvIdx); // makes a copy
                    Dispatcher.InvokeAsync(() => { ProcessLine(ci, line); });
                    ci.RecvIdx = 0;
                }
                else
                {
                    if (b !=  '\r')
                        ci.RecieveBuff[ci.RecvIdx++] = b;
                    if (ci.RecvIdx >= ci.RecieveBuff.Length)
                        Debugger.Break();    // overflow +++ atempt recovery
                }
            }
        }

        void SerialHandler(ComportItem ci)
        {
            byte[] buffer = new byte[1024];
            Action kickoffRead = null;
            kickoffRead = delegate
            {
                ci.SerialPort.BaseStream.BeginRead(buffer, 0, buffer.Length, delegate (IAsyncResult ar)
                {
                    if (ci.SerialPort.IsOpen)
                    {
                        try
                        {
                            int actualLength = ci.SerialPort.BaseStream.EndRead(ar);
                            byte[] received = new byte[actualLength];
                            Buffer.BlockCopy(buffer, 0, received, 0, actualLength);                            
                            AppSerialDataEvent(ci, received);
                        }
                        catch (Exception exc)
                        {
                            Trace.WriteLine($"{ci.SerialPort.PortName} exception", "error");
                            Trace.WriteLine(exc.Message, "2");
                        }
                        kickoffRead();
                    }
                }, null);

            };
            kickoffRead();
        }

        private void MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            // given a topic we can assume we subscribed to, which com port wants it?            
            foreach (var key in TopicListeners.Keys)
                if (e.Topic.StartsWith(key))
                    Dispatcher.InvokeAsync(() =>
                    {
                        Trace.WriteLine($"Found subscribers for {e.Topic}", "4");
                        foreach (var p in TopicListeners[key])
                        {
                            ComPortItemsDictionary[p].TransmitActivity();
                            Trace.WriteLine($"{p.PortName} <- {e.Topic}: ", "3");
                            Trace.WriteLine(System.Text.Encoding.UTF8.GetString(e.Message), "2");
                            //Trace.WriteLine(spiked3.extensions.HexDump(e.Message, 32), "4");
                            try
                            {
                                p.Write(System.Text.Encoding.UTF8.GetString(e.Message));
                                p.Write("\n");
                            }
                            catch (Exception)
                            {
                                Debugger.Break();
                            }
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

        private void RibbonToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            Bagger = Bagger.Factory(baggerNew.IsChecked ?? false);
            Trace.Listeners.Add(Bagger);
        }

        private void RibbonToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Bagger != null)
            {
                Bagger.Close();
                Trace.Listeners.Remove(Bagger);
            }
        }
    }
}