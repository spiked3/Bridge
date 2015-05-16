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
                    //s = new SerialPort(CompPortsToMonitor[i], 115200, Parity.None, 8, StopBits.One);
                    s = new SerialPort(CompPortsToMonitor[i], 115200);

                    s.Open();
                }
                catch (Exception)
                {
                    Trace.WriteLine(string.Format("Serial port {0} failed to open, skipping.", CompPortsToMonitor[i]), "1");
                    continue;
                }

                s.WriteTimeout = 200;
                //s.DataReceived += Serial_DataReceived;
                ComportItem ci = new ComportItem(s, Dispatcher);
                SerialHandler(ci);
                ci.Topic = "robot1";
                ComPortItems.Add(ci);
                ComPortItemsDictionary.Add(s, ci);
                Trace.WriteLine(string.Format("Serial port {0} opened.", CompPortsToMonitor[i]));

                string subscribeTopic = ci.Topic + "/Cmd";
                Mqtt.Subscribe(new string[] { subscribeTopic }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
                TopicListeners.Add(subscribeTopic, new List<SerialPort>());
                Trace.WriteLine(string.Format("{0} listen {1}", s.PortName, subscribeTopic), "2");
                TopicListeners[subscribeTopic].Add(s);
            }
        }

        void ProcessLine(ComportItem ci, string line)
        {
            // comments ok, echo'd
            if (line.StartsWith("//"))
            {
                Trace.WriteLine(ci.SerialPort.PortName + "->" + line);
                return;
            }

            if (line.Length < 3)
            {
                Trace.WriteLine(string.Format("{0} framing error", ci.SerialPort.PortName), "warn");
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
                Trace.WriteLine(ci.SerialPort.PortName + "->" + line);
                try
                {
                    dynamic j = JsonConvert.DeserializeObject(line);
                    Mqtt.Publish("robot1", UTF8Encoding.ASCII.GetBytes(line));
                    Trace.WriteLine("#PUB# " + line, "3");
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
                if (b == '\r')
                    continue;
                else if (b == '\n')
                {
                    ci.RecieveBuff[ci.RecvIdx] = 0;
                    string line = Encoding.UTF8.GetString(ci.RecieveBuff, 0, ci.RecvIdx); // makes a copy
                    Dispatcher.InvokeAsync(() => { ProcessLine(ci, line); });
                    ci.RecvIdx = 0;
                    continue;
                }
                else
                    ci.RecieveBuff[ci.RecvIdx] = (byte)b;

                ci.RecvIdx++;
                if (ci.RecvIdx >= ci.RecieveBuff.Length)
                {
                    System.Diagnostics.Debugger.Break();    // overflow
                    // +++ atempt recovery
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
                            Trace.WriteLine(exc.Message);
                        }
                        kickoffRead();
                    }
                }, null);

            };
            kickoffRead();
        }

        //private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
        //{
        //    SerialPort p = sender as SerialPort;

        //    Debug.Assert(ComPortItemsDictionary.ContainsKey(p));

        //    ComportItem i = ComPortItemsDictionary[p];
        //    i.ReceiveActivity();

        //    while (p.BytesToRead > 0)
        //    {
        //        string line;
        //        try
        //        {
        //            line = p.ReadLine();
        //            line = line.Trim(new char[] { '\r', '\n' });     // not supposed to be there, throw away if they are
        //        }
        //        catch (TimeoutException)
        //        {
        //            Trace.WriteLine("timeout on ReadLine()", "warn");
        //            break;
        //        }
        //        catch (System.IO.IOException ioe)
        //        {
        //            Trace.WriteLine("System.IO.IOException: " + ioe.Message, "warn");
        //            break;
        //        }

        //        // comments ok, echo'd
        //        if (line.StartsWith("//"))
        //        {
        //            Trace.WriteLine(line);
        //            continue;
        //        }

        //        if (line.Length < 3)
        //        {
        //            Trace.WriteLine(string.Format("{0} framing error", p.PortName), "warn");
        //            // attempt to recover
        //            bool recovered = false;
        //            while (!recovered)
        //            {
        //                try
        //                {
        //                    p.ReadTo("\n");
        //                    recovered = true;
        //                }
        //                catch (Exception)
        //                { }
        //            }
        //            continue;
        //        }

        //        else
        //        {
        //            try
        //            {
        //                dynamic j = JsonConvert.DeserializeObject(line);
        //                //Mqtt.Publish((string)j.Topic, UTF8Encoding.ASCII.GetBytes(line));
        //                Mqtt.Publish(i.Topic, UTF8Encoding.ASCII.GetBytes(line));
        //                Trace.WriteLine("#PUB# " + i.Topic + " " + line, "3");
        //            }
        //            catch (Exception ex)
        //            {
        //                Trace.WriteLine("recv Exception-> " + line, "error");
        //                Trace.WriteLine(ex.Message, "2");
        //            }
        //        }
        //    }
        //}

        private void MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            // we have a topic we can assume we subscribed to, which com port wants it?            
            foreach (var key in TopicListeners.Keys)
                if (e.Topic.StartsWith(key))
                    Dispatcher.InvokeAsync(() =>
                    {
                        Trace.WriteLine(string.Format("Found subscribers for {0}", e.Topic), "2");
                        foreach (var p in TopicListeners[key])
                        {
                            // trace.writeline ("#PUB#")
                            ComPortItemsDictionary[p].TransmitActivity();
                            Trace.WriteLine(string.Format("{0} <- {1}", p.PortName, e.Topic), "2");
                            Trace.WriteLine(spiked3.extensions.HexDump(e.Message, 32), "3");
                            p.Write(System.Text.Encoding.UTF8.GetString(e.Message));
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

        private void Bag_Unchecked(object sender, RoutedEventArgs e)
        {
        }

        private void Bag_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void RibbonToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            Bagger = Bagger.Factory(baggerNew.IsChecked ?? false);
            System.Diagnostics.Trace.Listeners.Add(Bagger);
        }

        private void RibbonToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Bagger != null)
            {
                Bagger.Close();
                System.Diagnostics.Trace.Listeners.Remove(Bagger);
            }
        }
    }
}