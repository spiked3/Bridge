using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace spiked3
{
    // default color foreground
    // + will be bright green
    // warn is yellow
    // error is red
    // level 1-4 detail level of debugging messages, 4 being lowest
    public partial class Console : UserControl
    {
        static ListBox myListbox;

        static public int MessageLevel { get; set; }

        public Console()
        {
            InitializeComponent();
            myListbox = listBox1;
            new TraceDecorator(myListbox);
        }

        public static void Clear()
        {
            myListbox.Items.Clear();
        }

        public static void Test()
        {
            Trace.WriteLine("Test_Click");
            Trace.WriteLine("test +", "+");
            Trace.WriteLine("test warn", "warn");
            Trace.WriteLine("test error", "error");
            Trace.WriteLine("test 1", "1");
            Trace.WriteLine("test 2", "2");
            Trace.WriteLine("test 3", "3");
            Trace.WriteLine("test 4", "4");
            Trace.WriteLine("test 5", "5");
        }

        class TraceDecorator : TraceListener
        {
            ListBox ListBox;
            public TraceDecorator(ListBox listBox)
            {
                ListBox = listBox;
                System.Diagnostics.Trace.Listeners.Add(this);
            }

            public override void WriteLine(string message, string category)
            {
                if (ListBox == null)
                    return;

                int CatagoryLevel;

                if (int.TryParse(category, out CatagoryLevel))  //if a numeric was specified
                    if (CatagoryLevel > MessageLevel)
                        return;

                ListBox.Dispatcher.Invoke(() =>
                {
                    // +++ add timestamp and level to msg like 12:22.78 Warning: xyz is being bad
                    TextBlock t = new TextBlock();
                    t.Text = message;
                    t.Foreground = category.Equals("error") ? Brushes.Red :
                        category.Equals("warn") ? Brushes.Yellow :
                        category.Equals("+") ? Brushes.LightGreen :
                        CatagoryLevel > 0 ? Brushes.Cyan :
                        ListBox.Foreground;

                    int i = ListBox.Items.Add(t);
                    if (ListBox.Items.Count > 1024)
                        ListBox.Items.RemoveAt(0);  // expensive I bet :(
                    var sv = ListBox.TryFindParent<ScrollViewer>();
                    if (sv != null)
                        sv.ScrollToBottom();  //  +++  not doing it
                });
            }

            public override void WriteLine(string message)
            {
                WriteLine(message, "");
            }

            public override void Write(string message)
            {
                throw new NotImplementedException();
            }

        }
    }
}
