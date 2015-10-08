using System;
using System.Xml;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using log4net;

namespace TrendScan_x64
{
    class Utility
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Utility));
        
        public static bool TestZipFile(string seven_zip_path, string zip_file_path)
        {
            string std_out = string.Empty;
            string std_err = string.Empty;
            CallProc(seven_zip_path, "", "t " + zip_file_path, ref std_out, ref std_err, Timeout.Infinite, null);
            bool test_result = true;
            if (std_err.Length != 0 || std_out.IndexOf("Error: ") != -1)
            {
                test_result = false;
                Log.DebugFormat("Zip file: {0} is damaged!", zip_file_path);
            }
            return test_result;
        }

        
        public static void KillAllProcsInDirectory(string directory_path)
        {
            Process[] procs = Process.GetProcesses();
            foreach (Process proc in procs)
            {
                try
                {
                    string proc_file_path;
                    try
                    {
                        proc_file_path = proc.MainModule.FileName;
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        // it's a excpeted exception and don't need to be procesed
                        Log.DebugFormat("Exception occurs when getting process module file name: {0}", ex.Message);
                        continue;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // the process has exited, ignoring it...
                        Log.DebugFormat("Exception occurs when getting process module file name: {0}", ex.Message);
                        continue;
                    }
                    try
                    {
                        string proc_file_dir = Path.GetDirectoryName(proc_file_path);
                        if (proc_file_dir.StartsWith(directory_path))
                        {
                            Log.InfoFormat("Killing {0}...", proc_file_path);
                            proc.Kill();
                            proc.WaitForExit();
                        }
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        // it's a excpeted exception and don't need to be procesed
                        Log.DebugFormat("Exception occurs when killing process: {0}", ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // the process has exited, ignoring it...
                        Log.DebugFormat("Exception occurs when killing process: {0}", ex.Message);
                    }
                }
                catch (System.Exception ex)
                {
                    string error_message = string.Format("Exception occurs when getting process module file name: {0}", ex.Message);
                    Log.ErrorFormat(error_message);
                    throw new ApplicationException(error_message);
                }
            }
        }

        public static void UnzipFile(string seven_zip_path, string zipped_file_path, string unzip_dir_path, string search_pattern)
        {
            string std_out = string.Empty;
            string std_err = string.Empty;
            string arguments = string.Format("x {0} {1} -y -o{2}", zipped_file_path, search_pattern, unzip_dir_path);
            CallProc(seven_zip_path, "", arguments, ref std_out, ref std_err, Timeout.Infinite, null);
            if (std_err.Length != 0)
                throw new Exception(string.Format("Unzip command: {0}\r\n{1}", seven_zip_path + " " + arguments, std_err));
            if (std_out.IndexOf("Error:") != -1)
                throw new Exception(string.Format("Unzip command: {0}\r\n{1}", seven_zip_path + " " + arguments, std_out));
            else
                Log.InfoFormat("Unzip result: {0}", "success");
        }

        public static bool CallProc(string exec_path, string working_dir, string arguments, int timed_out_msec, EventHandler handler)
        {
            string null_string = null;
            return CallProc(exec_path, working_dir, arguments, ref null_string, ref null_string, timed_out_msec, handler);
        }

        public static bool CallProc(string exec_path, string working_dir, string arguments, ref string std_out, ref string std_err, int timed_out_msec, EventHandler handler)
        {
            if ((std_out != null || std_err != null) && timed_out_msec != System.Threading.Timeout.Infinite)
            {
                throw new ApplicationException("Error: time_out_msec must be System.Threading.Timeout.Infinite if std_out or std_err redirect is used.");
            }
            Process process = new Process();
            StringBuilder sb_out = null;
            StringBuilder sb_err = null;
            ProcessStartInfo StartInfo = new ProcessStartInfo();
            StartInfo.FileName = exec_path;
            StartInfo.WorkingDirectory = working_dir;
            StartInfo.Arguments = arguments;
            StartInfo.UseShellExecute = false;
            if (handler != null)
            {
                process.EnableRaisingEvents = true;
                process.Exited += handler;
            }
            if (std_out != null)
            {
                StartInfo.RedirectStandardOutput = true;
                sb_out = new StringBuilder();
                process.OutputDataReceived += new DataReceivedEventHandler((object sender_proc, DataReceivedEventArgs line) => OutputHandler(sb_out, sender_proc, line));
            }
            else
            {
                StartInfo.RedirectStandardOutput = false;
            }
            if (std_err != null)
            {
                StartInfo.RedirectStandardError = true;
                sb_err = new StringBuilder();
                process.ErrorDataReceived += new DataReceivedEventHandler((object sender_proc, DataReceivedEventArgs line) => OutputHandler(sb_err, sender_proc, line));
            }
            else
            {
                StartInfo.RedirectStandardError = false;
            }
            Log.InfoFormat("Calling: {0} {1}", StartInfo.FileName, StartInfo.Arguments);
            process.StartInfo = StartInfo;
            process.Start();
            if (std_out != null)
                process.BeginOutputReadLine();
            if (std_err != null)
                process.BeginErrorReadLine();
            bool has_redirect = std_out != null || std_err != null;
            bool proc_ended = true;
            if (has_redirect)
                process.WaitForExit();
            else
                proc_ended = process.WaitForExit(timed_out_msec);
            if (std_out != null)
                std_out = sb_out.ToString();
            if (std_err != null)
                std_err = sb_err.ToString();
            return proc_ended;
        }

        private static void OutputHandler(StringBuilder sb, object sender_proc, DataReceivedEventArgs line)
        {
            if (!String.IsNullOrEmpty(line.Data))
            {
                sb.Append(line.Data);
                sb.Append(Environment.NewLine);
            }
        }

        public static void StopProcessing(ILog Log, string error_msg)
        {
            Log.Error(error_msg);
        }
        
        public static void KillCurrentProc(int delay_msec)
        {
            Thread.Sleep(delay_msec);
            Process.GetCurrentProcess().Kill();
        }

        public static UInt32 GetPatternVersion(string file_path)
        {
            byte[] buffer = new byte[4];
            using (FileStream fs = new FileStream(file_path, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(22, SeekOrigin.Begin);
                fs.Read(buffer, 0, 4);
                fs.Close();
            }
            return BitConverter.ToUInt32(buffer, 0);
        }
        
        public static string GetPtnVersion(string ptnPath)
        {
            Regex Exp = new Regex(@"(\d*)(\d\d\d)(\d\d)$");
            
            Match mv;
            
            string ptnName = Path.GetFileName(ptnPath).ToLower();
            string ptnVersion = Utility.GetPatternVersion(ptnPath).ToString();
            StringBuilder sb = new StringBuilder(64);

            if (ptnName.StartsWith("lpt$vpn."))
            {
            	if ((mv = Exp.Match(ptnVersion)).Success)
                {
                	sb.AppendFormat("LPTPTN[{0}.{1}.{2}]", mv.Groups[1].Value,
                		mv.Groups[2].Value, mv.Groups[3].Value);
                }
            }
            else if (ptnName.StartsWith("icrc$"))
            {
                sb.AppendFormat("{0}[{1}]", ptnName.Substring(5, 3).ToUpper(), ptnVersion);
            }
            return sb.ToString();
        }

        public class ProcessMaintainer
        {
            HashSet<string> exe_paths = new HashSet<string>();
            ManualResetEvent end_thread_event_ = new ManualResetEvent(false);
            Thread thread_;

            public ProcessMaintainer()
            {
                thread_ = new Thread(() => MonitorThread());
            }

            public void Start()
            {
                end_thread_event_.Reset();
                thread_.Start();
            }

            public void Stop()
            {
                end_thread_event_.Set();
            }

            public void Add(string exe_path) { exe_paths.Add(exe_path); }
            public void Remove(string exe_path) { exe_paths.Remove(exe_path); }

            void MonitorThread()
            {
                while (true)
                {
                    if (end_thread_event_.WaitOne(5000))
                        break;
                    HashSet<string> exited_proc = exe_paths;
                    Process[] procs = Process.GetProcesses();
                    foreach (Process proc in procs)
                    {
                        try
                        {
                            string proc_file_path;
                            try
                            {
                                proc_file_path = proc.MainModule.FileName;
                            }
                            catch (System.ComponentModel.Win32Exception)
                            {
                                // it's a excpeted exception and don't need to be procesed
                                continue;
                            }
                            catch (InvalidOperationException)
                            {
                                // the process has exited, ignoring it...
                                continue;
                            }
                            if (exited_proc.Contains(proc_file_path))
                                exited_proc.Remove(proc_file_path);
                        }
                        catch (System.Exception ex)
                        {
                            string error_message = string.Format("Exception occurs when getting process module file name: {0}", ex.Message);
                            Log.ErrorFormat(error_message);
                            throw new ApplicationException(error_message);
                        }
                    }
                    foreach (string exe_path in exited_proc)
                    {
                        Utility.CallProc(exe_path, "", "", Timeout.Infinite, null);
                    }
                }
            }
        }
    }

    public class Tuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public Tuple(T1 t1, T2 t2)
        {
            Item1 = t1;
            Item2 = t2;
        }
    }

    public class Tuple<T1, T2, T3>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public Tuple(T1 t1, T2 t2, T3 t3)
        {
            Item1 = t1;
            Item2 = t2;
            Item3 = t3;
        }
    }

    public class ProgramPool
    {
        public interface IProgram : IDisposable
        {
            string WorkingDirectory
            {
                get;
            }

            bool Run(string arguments, string std_out, string std_err, int timeout_ms);
        }

        Queue<ProgramExecuter> pool_ = new Queue<ProgramExecuter>();
        Object lock_ = new Object();

        public Int32 Count
        {
            get
            {
                return pool_.Count;
            }
        }

        public ProgramPool(string pool_path, string program_file)
        {
            Enumerate(pool_path, program_file);
        }

        public IProgram GetProgram()
        {
            lock (lock_)
            {
                while (IsEmpty())
                {
                    Monitor.Wait(lock_);
                }
                ProgramExecuter program = pool_.Dequeue();
                return (new ProgramImpl(program, () => Release(program)));
            }
        }

        void Release(ProgramExecuter program)
        {
            lock (lock_)
            {
                pool_.Enqueue(program);
                Monitor.Pulse(lock_);
            }
        }

        void Enumerate(string pool_path, string program_file)
        {
            foreach (string folder_path in Directory.GetDirectories(pool_path))
            {
                string command_path = Path.Combine(folder_path, program_file);
                if (File.Exists(command_path))
                {
                    pool_.Enqueue(new ProgramExecuter(folder_path, program_file));
                }
            }
        }

        bool IsEmpty()
        {
            return pool_.Count == 0;
        }

        public class ProgramImpl : IProgram
        {
            readonly ProgramPool.ProgramExecuter program_;
            Action release_;

            public string WorkingDirectory
            {
                get
                {
                    return program_.WorkingDirectory;
                }
            }

            public ProgramImpl(ProgramPool.ProgramExecuter program, Action release)
            {
                program_ = program;
                release_ = release;
            }

            public bool Run(string arguments, string std_out, string std_err, int timeout_ms)
            {
                return program_.Run(arguments, std_out, std_err, timeout_ms);
            }

            public void Dispose()
            {
                if (release_ != null)
                {
                    release_();
                    release_ = null;
                }
            }

            ~ProgramImpl()
            {
                if (release_ != null)
                {
                    release_();
                    release_ = null;
                }
            }
        }

        public class ProgramExecuter
        {
            private static readonly ILog Log = LogManager.GetLogger(typeof(ProgramExecuter));

            readonly string working_dir_;
            readonly string program_path_;

            public string WorkingDirectory
            {
                get
                {
                    return this.working_dir_;
                }
            }

            public ProgramExecuter(string working_dir, string program_file_name)
            {
                working_dir_ = working_dir;
                program_path_ = Path.Combine(working_dir_, program_file_name);
            }

            public bool Run(string arguments, string std_out, string std_err, int timeout_ms)
            {
                return Utility.CallProc(program_path_, working_dir_, arguments, ref std_out, ref std_err, timeout_ms, null);
            }
        }

    }

    public partial class FS
    {
        /// <summary>
        /// Create Symbolic Link. Only work on Vista/2008/Win7/Win8 and upper version. add by lucifer 2012-08-16
        /// </summary>
        /// <param name="lpSymlinkFileName">New Link File Path Name</param>
        /// <param name="lpTargetFileName">Source File Path Name</param>
        /// <param name="dwFlags">0x0: file, 0x1: directory</param>
        /// <returns></returns>

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, uint dwFlags);

        public static bool CreateSymbolicLinkFile(string lpSymlinkFileName, string lpTargetFileName)
        {
            return CreateSymbolicLink(lpSymlinkFileName, lpTargetFileName, SYMBLOC_LINK_FLAG_FILE);
        }

        public static bool CreateSymbolicLinkPath(string lpSymlinkFileName, string lpTargetFileName)
        {
            return CreateSymbolicLink(lpSymlinkFileName, lpTargetFileName, SYMBLOC_LINK_FLAG_DIRECTORY);
        }

        const uint SYMBLOC_LINK_FLAG_FILE      = 0x0;
        const uint SYMBLOC_LINK_FLAG_DIRECTORY = 0x1;

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        public static bool CreateHardLink(string source, string target)
        {
            return CreateHardLink(target, source, (IntPtr)0);
        }

        public static void CreateHardLinkRecursive(string source, string target)
        {
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(target);
                foreach (string source_dir_path in Directory.GetDirectories(source))
                {
                    string dir_name = Path.GetFileName(source_dir_path);
                    string target_dir_path = Path.Combine(target, dir_name);
                    CreateHardLinkRecursive(source_dir_path, target_dir_path);
                }
                foreach (string source_file_path in Directory.GetFiles(source))
                {
                    string file_name = Path.GetFileName(source_file_path);
                    string target_file_path = Path.Combine(target, file_name);
                    File.Delete(target_file_path);
                    bool ret = CreateHardLink(source_file_path, target_file_path);
                }
            }
            else if (File.Exists(source))
            {
                File.Delete(target);
                CreateHardLink(source, target);
            }
        }
    }
}
