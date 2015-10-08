using System;
using System.IO;
using System.Threading;
using log4net;

namespace TrendScan_x64
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Trend Scan Module";

            log4net.Config.XmlConfigurator.Configure();

            ILog Log = LogManager.GetLogger(typeof(TrendScan));

            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            Log.InfoFormat("Trend Scan x64 version = {0}", version);

            try
            {
                TrendScan trendScan = new TrendScan();

                if (!trendScan.Initialize())
                {
                    return;
                }
                
                trendScan.StartUpdateThread();

                while (Console.ReadKey(true).KeyChar != 'q');
                    
                trendScan.StopUpdateThread();
            }
            catch (Exception e)
            {
                Log.ErrorFormat("Program fail: {0}", e);
                Console.WriteLine("The process failed: {0}", e.ToString());
            }
            finally { }
        }
    }
}
