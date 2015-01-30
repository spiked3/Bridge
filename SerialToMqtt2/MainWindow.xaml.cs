﻿using System;
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



        public int MessageLevel
        {
            get { return (int)GetValue(MessageLevelProperty); }
            set { SetValue(MessageLevelProperty, value); }
        }
        public static readonly DependencyProperty MessageLevelProperty =
            DependencyProperty.Register("MessageLevel", typeof(int), typeof(MainWindow), new PropertyMetadata(1));

        private byte[] CompPortsToMonitor = { 3, 4, 5, 14 };     // +++ make as a parameter

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
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            spiked3.Console.MessageLevel = MessageLevel;

            Trace.WriteLine("MQTT to Serial Bridge 2/WPF © 2015 Spiked3.com", "+");

            Trace.WriteLine("Connecting to broker ...", "1");
            Mqtt = new MqttClient(Broker);

            Mqtt.Connect("pcbridge");
            Trace.WriteLine("...Connected", "1");

            Mqtt.MqttMsgPublishReceived += MqttMsgPublishReceived;

            StartSerial();
        }

        private void StopSerial()
        {
            foreach (SerialPort s in ComPortItemsDictionary.Keys)
                if (s.IsOpen)
                {
                    s.Close();
                    Trace.WriteLine(string.Format("Closed {0}", s.PortName), "1");
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
                    Trace.WriteLine(string.Format("Serial port {0} failed to open, skipping.", p), "1");
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
                                Trace.WriteLine(string.Format("{0}, Publish topic({1}) payload({2})", p.PortName, topic, payload), "3");
                                Mqtt.Publish(topic, System.Text.Encoding.UTF8.GetBytes(payload));
                            });
                        }
                        else if (line.Substring(0, 3).Equals("SUB"))
                        {
                            string topic = line.Substring(3);
                            Trace.WriteLine(string.Format("{0} Subscribed topic({1})", p.PortName, topic), "1");
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
                            Trace.WriteLine(string.Format("{0} Unsubscribed topic({1})", p.PortName, topic), "1");

                            if (TopicListeners.ContainsKey(topic))
                            {
                                for (int j = 0; j < TopicListeners[topic].Count; j++)
                                    TopicListeners[topic].Remove(p);
                                if (TopicListeners[topic].Count < 1)
                                {
                                    Mqtt.Unsubscribe(new string[] { topic });
                                    Trace.WriteLine(string.Format("Bridge Unsubscribed topic({0})", topic), "1");
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
                            Trace.WriteLine(string.Format("...pushing to {0}", p.PortName), "2");
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

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            spiked3.Console.Clear();
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            spiked3.Console.Test();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            spiked3.Console.MessageLevel = (int)e.NewValue;
        }
    }
}