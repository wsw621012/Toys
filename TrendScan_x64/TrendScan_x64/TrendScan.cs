using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using log4net;

namespace TrendScan_x64
{
    class TrendScan
    {
        readonly Properties.Settings Settings = Properties.Settings.Default;

        readonly string SevenZipPath = @".\7z.exe";

        readonly string PatternDir = @".\avbase";

        readonly string AtlasDir = @".\atlasin";

        readonly string PollDir = @".\poll";

        readonly string ResultDir = @".\result";

        List<BranchInfo> BranchInfoList = new List<BranchInfo>();

        readonly int ScanLevel = Properties.Settings.Default.ScanLevel;
        
        private static readonly ILog Log = LogManager.GetLogger(typeof(TrendScan));
        
        public static ManualResetEvent end_update_thread_event_ = new ManualResetEvent(false);

        Thread update_thread_;

        readonly List<int> UpdateInterval = new List<int>();

        public TrendScan()
        {
            foreach (string interval in Settings.UpdateInterval.Split(','))
            {
                UpdateInterval.Add(Convert.ToInt32(interval));
            }
        }

        public bool Initialize()
        {
            try
            {
                CreateAllDirectory();

                CreateBranches();

                foreach (BranchInfo branch in BranchInfoList)
                {
                    if (UnzipBranchPattern(branch))
                    {
                        SetupBranchParam(branch);
                    }
                    else if (DownloadBranchPattern(branch) && UnzipBranchPattern(branch))
                    {
                        SetupBranchParam(branch);
                    }
                    else
                    {
                        return false;
                    }
                }
                Log.Info("Initialize sucessful");
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Initialize fail: {0}", ex.Message);
                throw new Exception(string.Format("Initialize fail: {0}", ex.Message));
            }
        }

        private bool UnzipBranchPattern(BranchInfo branch)
        {
            if (!File.Exists(branch.ZipFilePath))
            {
                return false;
            }

            List<string> backupFileName = new List<string>();

            try
            {
                Log.InfoFormat("Unzipping pattern file {0}....", branch.ZipFilePath);
                Utility.UnzipFile(SevenZipPath, branch.ZipFilePath, branch.PatternPath + "_tmp", "*"); // extract to _tmp first

                if (Directory.Exists(branch.PatternPath))
                {
                    Directory.Delete(branch.PatternPath, true);
                }
                Directory.Move(branch.PatternPath + "_tmp", branch.PatternPath);
                branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[0]);
            }
            catch (System.Exception ex)
            {
                Log.ErrorFormat("UnzipBranchPattern {0} fail: {1}", branch.BranchName, ex.Message);
                if (Directory.Exists(branch.PatternPath + "_tmp"))
                {
                    Directory.Delete(branch.PatternPath + "_tmp", true);
                }
                branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[2]);
                throw new Exception(string.Format("UnzipBranchPattern {0} fail: {1}", branch.BranchName, ex.Message));

            }

            Regex rx = new Regex("^(lpt\\$vpn|icrc\\$(oth|tbl))\\.\\d{3}$");
            branch.VersionInfo = string.Empty;
            
            foreach (string file in Directory.GetFiles(branch.PatternPath))
            {
                if (!rx.IsMatch(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
                else
                {
                	branch.VersionInfo += Utility.GetPtnVersion(file);
                }
            }
            return true;
        }

        private bool DownloadBranchPattern(BranchInfo branch)
        {
            if (branch.URI.StartsWith("ftp"))
            {
                Log.InfoFormat("Downloading ({0}) from {1}...", branch.BranchName, branch.URI);

                try
                {
                    if (false == Network.DownloadFileIfHasUpdate(branch.URI, branch.ZipFilePath))
                    {
                        branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[1]); ;
                        Log.InfoFormat("DownloadBranchPattern {0} remote doesn't update yet, next download: {1}.", branch.BranchName, branch.NextUpdateTime);
                        return false;
                    }
                    
                    if (false == Utility.TestZipFile(SevenZipPath, branch.ZipFilePath))
                    {
                        branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[2]); ;
                        Log.InfoFormat("DownloadBranchPattern {0} zip damage, next download: {1}.", branch.BranchName, branch.NextUpdateTime);
                        File.Delete(branch.ZipFilePath);
                        return false;
                    }

                    Log.InfoFormat("DownloadBranchPattern {0} success.", branch.BranchName);
                    branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[0]);
                    return true;
                }
                catch (System.Exception ex)
                {
                    Log.ErrorFormat("DownloadBranchPattern from FTP fail: {0}", ex.Message);
                    throw new System.Exception(string.Format("DownloadBranchPattern fail: {0}", ex.Message));
                }
            }

            string vsapiPath = string.Empty;
            long   vsapiSize = 0;
                
            Regex rx = new Regex("^P\\.4=(?<file_name>[^,]+),(?<file_version>\\d+),(?<file_size>\\d+)$");
            try
            {
                Network.DownloadFile(branch.URI + @"/server.ini", @".\download\server.ini");

                bool hasFindPatternInfo = false;

                foreach (string line in File.ReadLines(@".\download\server.ini"))
                {
                    Match match = rx.Match(line);

                    if (match.Success)
                    {
                        hasFindPatternInfo = true;
                        if ((vsapiPath = match.Groups["file_name"].Value) == branch.LastPatternName)
                        {
                            branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[1]); ;
                            Log.InfoFormat("DownloadBranchPattern {0} remote doesn't update yet, next download: {1}.", branch.BranchName, branch.NextUpdateTime);
                            return false;
                        }
                        vsapiSize = Convert.ToInt64(match.Groups["file_size"].Value);
                        break;
                    }
                }

                if (!hasFindPatternInfo)
                {
                    branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[2]);
                    Log.InfoFormat("DownloadBranchPattern {0} doesn't find pattern infomation in server.ini, next download: {1}", branch.BranchName, branch.NextUpdateTime);
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                Log.ErrorFormat("DownloadBranchPattern from AU fail: {0}", ex.Message);
                throw new System.Exception(string.Format("DownloadBranchPattern fail: {0}", ex.Message));
            }

            try
            {
                Log.InfoFormat("Downloading ({0}) from {1}...", branch.BranchName, branch.URI + "/" + vsapiPath);
                Network.DownloadFile(branch.URI + "/" + vsapiPath, branch.ZipFilePath);

                FileInfo f = new FileInfo(branch.ZipFilePath);
                if (vsapiSize != f.Length)
                {
                    branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[2]);
                    Log.InfoFormat("DownloadBranchPattern {0} filesize wrong, next download: {1}.", branch.BranchName, branch.NextUpdateTime);
                    
                    File.Delete(branch.ZipFilePath);
                    return false;
                }

                if (false == Utility.TestZipFile(SevenZipPath, branch.ZipFilePath))
                {
                    branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[2]);
                    Log.InfoFormat("DownloadBranchPattern {0} zip damage, next download: {1}.", branch.BranchName, branch.NextUpdateTime);
                    File.Delete(branch.ZipFilePath);
                    return false;
                }

                Log.InfoFormat("DownloadBranchPattern {0} success.", branch.BranchName);
                branch.NextUpdateTime = DateTime.Now + TimeSpan.FromSeconds(UpdateInterval[0]);
                branch.LastPatternName = vsapiPath;
                return true;
            }
            catch (System.Exception ex)
            {
                Log.ErrorFormat("DownloadBranchPattern from AU fail: {0}", ex.Message);
                throw new System.Exception(string.Format("DownloadBranchPattern fail: {0}", ex.Message));
            }
        }
        
        private void CreateAllDirectory()
        {
            string[] dirs = { AtlasDir, PatternDir, PollDir, ResultDir };

            foreach (string dir in dirs)
            {
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.ErrorFormat("CreateAllDirectory {0} fail: {1}", dir, ex.Message);
                    throw new Exception(string.Format("CreateAllDirectory {0} fail: {1}", dir, ex.Message));
                }
            }
        }

        private void CreateBranches()
        {
            string downloadDir = @".\download"; ///< ex. "D:\temp\download"

            Directory.CreateDirectory(downloadDir);

            foreach (string uri in Settings.PatternURIs.Split(','))
            {
                if (uri == string.Empty)
                {
                    break;
                }
                string file_name = Path.GetFileName(uri);

                string branch_name = Path.GetFileNameWithoutExtension(file_name);

                BranchInfoList.Add( new BranchInfo(
                    uri, branch_name, Path.Combine(downloadDir, file_name),
                        Path.Combine(PatternDir, branch_name)));
            }

            try
            {
                string[] zipFiles = Directory.GetFiles(downloadDir);

                foreach (string url in Settings.AUURLs.Split(';'))
                {
                    if (url == string.Empty)
                    {
                        break;
                    }

                    string[] urls = url.Split(',');
                    BranchInfoList.Add(new BranchInfo(
                        urls[1], urls[0], Path.Combine(downloadDir, (urls[0] + ".zip")),
                            Path.Combine(PatternDir, urls[0])));
                }
            }

            catch (System.Exception ex)
            {
                Log.ErrorFormat("CreateBranches fail: {0}", ex);
                throw new Exception(string.Format("CreateBranches fail: {0}", ex.Message));
            }

            
        }

        public void ProcessTrendScan()
        {
            foreach (BranchInfo branch in BranchInfoList)
            {
                if (branch.NextUpdateTime < DateTime.Now)
                {
                    try
                    {
                        if (DownloadBranchPattern(branch) && UnzipBranchPattern(branch))
                        {
                            SetupBranchParam(branch);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.ErrorFormat("ProcessTrenscan {0} fail, {1}", branch.BranchName, ex.Message);
                    }
                }
            }
            
            string[] tickets = Directory.GetFiles(PollDir, @"*.wtp");

            if (tickets.Length == 0)
            {
                return;
            }
            
            File.Delete(tickets[0]);
            Log.InfoFormat("ticket({0}) were found in {0}.", tickets[0]);
            
            string[] files = Directory.GetFiles(AtlasDir, @"*.zip");

            if (files.Length == 0)
            {
                Log.InfoFormat("ProcessTrendScan() : No .wtp files were fuound in {0}.", PollDir);
                return;
            }

            string engineVersion = string.Empty;

            if (File.Exists(Settings.VscanEngine))
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Settings.VscanEngine);
                
                engineVersion = string.Format("ENG[{0}]", fvi.FileVersion);
            }

            foreach (string fullPathName in files)
            {
                Dictionary<string, HashSet<string>> statistics = new Dictionary<string, HashSet<string>>();

                StringBuilder botInfo = new StringBuilder();
                
                
                foreach (BranchInfo branch in BranchInfoList)
                {
                    try
                    {
                        string[] results = branch.DoScan(fullPathName, 10 * 60 * 1000);

                        if (results == null)
                        {
                            Log.InfoFormat("{0} scan fail, no result is generated", branch.BranchName);
                            
                            continue;
                        }

                        foreach (string line in results)
                        {
                            string[] columns = line.Split(',');
                            
                            if (!statistics.ContainsKey(columns[0]))
                            {
                                statistics.Add(columns[0], new HashSet<string>());
                            }
                            statistics[columns[0]].Add(string.Format("{0}\t: {1}", branch.BranchName.ToUpper(), columns[1]));
                        }
                    }
                    catch(System.Exception ex)
                    {
                        Log.ErrorFormat("ProcessTrendScan: {0}", ex.Message);
                    }
                }

                StringBuilder sb = new StringBuilder();

                if (statistics.Count > 0)
                {
                    sb.Append(Environment.NewLine);

                    foreach (KeyValuePair<string, HashSet<string>> blocks in statistics)
                    {
                        sb.AppendFormat("FileName\t: {0}", blocks.Key);
                        sb.Append(Environment.NewLine);

                        foreach (string block in blocks.Value)
                        {
                            sb.AppendLine(block);
                        }
                        sb.AppendLine("FileSize\t: N/A");
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("No Detection");
                }
                        
                sb.AppendLine("End Of MIST Result");
                sb.AppendLine();

                sb.AppendLine("Name: " + Path.GetFileName(fullPathName));

                FileInfo fileInfo = new FileInfo(fullPathName);
                sb.AppendLine("Size: " + fileInfo.Length.ToString("#,0") + " Bytes");

                sb.AppendLine(string.Format("Scanned by: {0}", Dns.GetHostName()));
                sb.AppendLine();

                foreach (BranchInfo branch in BranchInfoList)
                {
                    sb.AppendLine(branch.BranchName.ToUpper() + "\t: " + engineVersion + branch.VersionInfo);
                }

                string resultName = string.Format(@".\result\{0}-{1}.done.txt", DateTime.Now.ToString("yyyyMMddHHmmss"), Path.GetFileName(fullPathName));

                Log.InfoFormat("writing {0} ...", Path.GetFullPath(resultName));

                try
                {
                    File.WriteAllText(resultName, sb.ToString());

                    Log.InfoFormat("{0} is generated success!", resultName);
                }
                catch (System.Exception ex)
                {
                    Log.ErrorFormat("{0}, {1} generated fail!", ex.Message, resultName);
                }
                

                try
                {
                    if (Directory.Exists(@".\backup"))
                    {
                        string target = string.Format(@".\backup\{0}", Path.GetFileName(fullPathName));
                        if (File.Exists(target))
                        {
                            File.Delete(target);
                        }
                        File.Move(fullPathName, target);
                    }
                    else
                    {
                        File.Delete(fullPathName);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.ErrorFormat("ProcessTrendScan: {0}", ex.Message);
                }
            }
            
        }

        
        void SetupBranchParam(BranchInfo branch)
        {
            string scanLogFile = Path.Combine(branch.PatternPath, "raw.log");

            if (branch.BranchName.ToLower().Equals("trend_icrc"))
            {
                string othPattern = Path.Combine(branch.PatternPath, branch.GetLastestPattern("icrc$oth.*"));

                string tblPattern = Path.Combine(branch.PatternPath, branch.GetLastestPattern("icrc$tbl.*"));

                branch.VscanParam = string.Format("/vszip={0} ", ScanLevel) + @"/s /nc /longvname /vsspyware+ /p=" + othPattern + @" /tbl=" + tblPattern + @" /lr=" + scanLogFile;
            }
            else if (branch.BranchName.ToLower().Equals("trendmicro"))
            {
                string[] files = Directory.GetFiles(branch.PatternPath, "tmaptn.*");
                foreach (string file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }

                string lptPattern = Path.Combine(branch.PatternPath, branch.GetLastestPattern("lpt$vpn.*"));

                branch.VscanParam = string.Format("/vszip={0} ", ScanLevel) + @" /scantest /longvname /p=" + lptPattern + @" /nm /nb /tmaptn=" + branch.PatternPath + @" /vsspyware+ /vsseclvl=1 /lr=" + scanLogFile;
            }
            else
            {
                string lptPattern = Path.Combine(branch.PatternPath, branch.GetLastestPattern("lpt$vpn.*"));

                branch.VscanParam = string.Format("/vszip={0} ", ScanLevel) + @" /scantest /longvname /p=" + lptPattern + @" /nm /nb /vsseclvl=1 /lr=" + scanLogFile;
            }
        }

        public void StartUpdateThread()
        {
            update_thread_ = new Thread(UpdateThread);
            
            update_thread_.Start();
        }

        public void StopUpdateThread()
        {
            Log.Info("Process stopping...");

            if (update_thread_ != null)
            {
                end_update_thread_event_.Set();
                update_thread_.Join();
                update_thread_ = null;
            }

            Log.Info("Process stopped");
        }

        void UpdateThread()
        {
            //TimeSpan updateInterval = TimeSpan.FromSeconds(Settings.PaternUpdateInterval);

            TimeSpan scanInterval = TimeSpan.FromSeconds(Settings.ScanInterval);

            //DateTime baseLine = DateTime.Now;

            while (!end_update_thread_event_.WaitOne(scanInterval))
            {
                ProcessTrendScan();
			}
        }
    }
}
