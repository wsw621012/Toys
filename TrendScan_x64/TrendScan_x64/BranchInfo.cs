using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;

using log4net;

namespace TrendScan_x64
{
    class ScanResult
    {
        public ScanResult()
        {
            detected_samples = new Dictionary<string, Dictionary<string, HashSet<string>>>();
            error_samples = new HashSet<string>();
        }

        public Dictionary<string, Dictionary<string, HashSet<string>>> detected_samples { get; set; }

        public HashSet<string> error_samples { get; set; }
    }
    
    class BranchInfo
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TrendScan));

        public string URI;
        
        public string BranchName;
        
        public string ZipFilePath;
        
        public string PatternPath;

        public string LastPatternName = string.Empty;

        public string VscanParam  = string.Empty;
        
        public string VersionInfo = string.Empty;

        public DateTime NextUpdateTime = DateTime.Now;
        
        Regex _regex = new Regex(@"^(Found|Undet)\s.+\]\[\s+(?<pattern_name>.+)\].+\sin\s(?<file_path>[^,]+),\((?<in_file>[^)]+)");

        public BranchInfo(string uri, string branch_name, string zip_file_path, string pattern_path)
        {
            URI         = uri;
            BranchName  = branch_name;
            ZipFilePath = zip_file_path;
            PatternPath = pattern_path;
        }

        public string[] DoScan(string fullPathName, int time_out)
        {
            try
            {
                if (!RunScanner(fullPathName, time_out))
                {
                    return null;
                }

                string output_file_path = PatternPath + @"\raw.log";

                Log.InfoFormat("Scan completed in {0}", PatternPath);
                
                if (!File.Exists(output_file_path))
                {
                    Log.InfoFormat("{0} is not exist!", output_file_path);

                    return null;
                }

                Log.InfoFormat("Reading {0} ...", Path.GetFullPath(output_file_path));
                
                List<string> result = new List<string>();

                foreach (string line in File.ReadAllLines(output_file_path))
                {
                    Match match = _regex.Match(line);

                    if (match.Success && match.Groups["file_path"].Value == fullPathName)
                    {
                        //string file_name = Path.GetFileName(match.Groups["in_file"].Value);

                        string file_name = match.Groups["in_file"].Value;

                        string pattern_name = match.Groups["pattern_name"].Value;

                        if (line.StartsWith("Undet"))
                        {
                            pattern_name = "NO_VIRUS";
                        }

                        result.Add(string.Format("{0},{1}", file_name, pattern_name));
                    }
                }
                return result.ToArray();
            }
            catch (System.Exception ex)
            {
                Log.ErrorFormat("DoScan: {0}", ex);

                return null;
            }
        }

        // return false if timed-out
        bool RunScanner(string fullPathName, int timeout_ms)
        {
            /*
            ProcessStartInfo start_info = new ProcessStartInfo();

            start_info.FileName = Properties.Settings.Default.VscanName;

            //start_info.WorkingDirectory = PatternPath;

            start_info.Arguments = VscanParam + string.Format(@" ""{0}""", fullPathName);

            start_info.UseShellExecute = false;

            Log.InfoFormat("Calling: {0} {1}", start_info.FileName, start_info.Arguments);

            Process process = new Process();

            process.StartInfo = start_info;

            process.Start();

            bool success = process.WaitForExit(timeout_ms);

            */
            string exe = Properties.Settings.Default.VscanName;
            string arg = string.Format("{0} \"{1}\"", VscanParam, fullPathName);
            
            try
            {
            	bool success = Utility.CallProc(exe, "", arg, timeout_ms, null);
            
            	if (!success)
            	{
                	string path_to_kill = Path.GetFullPath(PatternPath);

                	Log.InfoFormat("Scanner timed out, killing process in {0}...", path_to_kill);

                	Utility.KillAllProcsInDirectory(path_to_kill);

                	Log.DebugFormat("Scanner process in {0} terminated", PatternPath);
            	}
            	return success;
            }
            catch (System.Exception ex)
            {
            	Log.ErrorFormat("RunScanner({0}) fail: {1}", BranchName, ex.Message);
            	return false;
            }
        }

        public string GetLastestPattern(string regx)
        {
            string[] files = Directory.GetFiles(PatternPath, regx);

            int lastest = 0;

            string ext = string.Empty;

            foreach (string file in files)
            {
                int dummy = Convert.ToInt32(Path.GetExtension(file).Substring(1));
                
                if (dummy > lastest)
                {
                    lastest = dummy;

                    ext = Path.GetExtension(file);
                }
            }

            return Path.ChangeExtension(regx, ext);
        }

        
    }
}
