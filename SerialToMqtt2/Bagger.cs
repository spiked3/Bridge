using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SerialToMqtt2
{
    public class Bagger : TextWriterTraceListener
    {
        public Bagger(string file)
            : base(file)
        {
        }

        public static Bagger Factory(bool newFile)
        {
            string bagFile;
            if (newFile)
                bagFile = string.Format("{0}_BridgeBag.txt", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            else
            {
                bagFile = Settings1.Default.lastBagFile;
                if (bagFile == null || bagFile.Length < 1)
                    bagFile = "BridgeBag.txt";
            }
            Trace.WriteLine(string.Format("using logfile {0}", bagFile), "+");
            Settings1.Default.lastBagFile = bagFile;
            Settings1.Default.Save();
            return new Bagger(bagFile);
        }

        // this is ignored for text writers :(
        // Bagger.TraceOutputOptions |= TraceOptions.Timestamp;

        public override void WriteLine(string message)
        {
            string t = string.Format("{0}|{1}", DateTime.Now.ToString("HHmmssffff"), message);
            base.WriteLine(t);
        }

        public override void WriteLine(string message, string category)
        {
            string t = string.Format("{0}|{1}", DateTime.Now.ToString("HHmmssffff"), message);
            base.WriteLine(message, category);
        }
    }
}
