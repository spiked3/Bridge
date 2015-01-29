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
    public partial class Console : UserControl
    {
        static ListBox myListbox;
        public Console()
        {
            InitializeComponent();
            myListbox = listBox1;
            new TraceDecorator(myListbox);
        }

        public static void ClearConsole()
        {
            myListbox.Items.Clear();
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

                ListBox.Dispatcher.InvokeAsync(() =>
                {
                    // +++ add timestamp and level to msg like 12:22.78 Warning: xyz is being bad
                    TextBlock t = new TextBlock();
                    t.Text = message;
                    t.Foreground = category.Equals("error") ? Brushes.Red :
                        category.Equals("warn") ? Brushes.Yellow :
                        category.Equals("+") ? Brushes.LightGreen :
                        category.Equals("-") ? Brushes.Gray :
                        category.Equals("1") ? Brushes.Cyan :
                        category.Equals("2") ? Brushes.Magenta :
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
