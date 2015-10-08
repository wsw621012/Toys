using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;

using log4net;

namespace TrendScan_x64
{
    class Network
    {
        
        private static readonly ILog Log = LogManager.GetLogger(typeof(Network));

        public static FtpWebRequest GetFtpWebRequest(string uri, string user, string pwd)
        {
            return (FtpWebRequest)GetWebRequest(uri, user, pwd);
        }

        public static WebRequest GetWebRequest(string uri, string user, string pwd)
        {
            WebRequest request = WebRequest.Create(uri);
            request.Credentials = new NetworkCredential(user, pwd);
            return request;
        }

        static WebRequest GetWebRequest(string uri)
        {
            return WebRequest.Create(uri);
        }

        public static void DownloadFile(string full_uri, string dst_file_path)
        {
            Regex regex = new Regex(@"^(?<pre>ftp://)(?<user>\S+):(?<pass>\S+)@(?<post>\S+)$");
            Match match = regex.Match(full_uri);
            WebRequest request;
            if (match.Success)
            {
                string uri = match.Groups["pre"].Value + match.Groups["post"].Value;
                string user = match.Groups["user"].Value;
                string pwd = match.Groups["pass"].Value;
                request = GetWebRequest(uri, user, pwd);
            }
            else
            {
                request = GetWebRequest(full_uri);
            }
            DownloadFile(request, dst_file_path);
        }

        public static bool DownloadFileIfHasUpdate(string full_uri, string dst_file_path)
        {
            string timestamp_file = dst_file_path + ".ts";
            Regex regex = new Regex(@"^(?<pre>ftp://)(?<user>\S+):(?<pass>\S+)@(?<post>\S+)$");
            Match match = regex.Match(full_uri);
            if (!match.Success)
                throw new Exception("Incorrect URI format");
            string uri = match.Groups["pre"].Value + match.Groups["post"].Value;
            string user = match.Groups["user"].Value;
            string pwd = match.Groups["pass"].Value;
            string server_file_time = GetFTPFileTime((FtpWebRequest)GetWebRequest(uri, user, pwd)).ToString();
            bool need_to_download = NeedToDownload(timestamp_file, server_file_time);
            if (need_to_download)
            {
                DownloadFile(full_uri, dst_file_path);
                File.WriteAllText(timestamp_file, server_file_time);
                Log.InfoFormat("File update completed from {0}, {1}", uri, server_file_time);
            }
            return need_to_download;
        }

        public static string GetLatestFileInDirectory(FtpWebRequest request)
        {
            request.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
            DateTime latest_date_time = new DateTime();
            string latest_file = null;
            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                StreamReader reader = new StreamReader(response.GetResponseStream());
                FileListStyle style = FileListStyle.Unknown;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (style == FileListStyle.Unknown)
                        style = GetFileListStyle(line);
                    string date_time_str = null;
                    string file_name = null;
                    if (style == FileListStyle.WindowsStyle)
                    {
                        Match match = Regex.Match(line, @"^(?<date_time>.+M).+\s+(?<file_name>\S+)$");
                        if (match.Success)
                        {
                            date_time_str = match.Groups["date_time"].Value;
                            file_name = match.Groups["file_name"].Value;
                        }
                    }
                    else
                    {
                        Match match = Regex.Match(line, @".+\s(?<date_time>\S+\s+\S+\s+\S+)\s(?<file_name>\S+)$");
                        if (match.Success)
                        {
                            date_time_str = match.Groups["date_time"].Value;
                            file_name = match.Groups["file_name"].Value;
                            Match match2 = Regex.Match(date_time_str, @"^(?<month_day>\S+\s+\S+)\s+(?<time>\d+:\d+)");
                            if (match2.Success)
                            {
                                string month_day = match2.Groups["month_day"].Value;
                                string time = match2.Groups["time"].Value;
                                DateTime now = DateTime.Now;
                                DateTime next_month = now.AddMonths(1);
                                date_time_str = string.Format("{0} {1} {2}", month_day, now.Year, time);
                                DateTime temp_dt = DateTime.Parse(date_time_str);
                                if (temp_dt > next_month)
                                {
                                    date_time_str = string.Format("{0} {1} {2}", month_day, now.Year - 1, time);
                                }
                            }
                        }
                    }
                    DateTime date_time = DateTime.Parse(date_time_str, new CultureInfo("en-us", true));
                    if (date_time > latest_date_time)
                    {
                        latest_date_time = date_time;
                        latest_file = file_name;
                    }
                }
            }
            return latest_file;
        }

        public static DateTime GetFTPFileTime(FtpWebRequest request)
        {
            request.Method = WebRequestMethods.Ftp.GetDateTimestamp;
            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                return response.LastModified;
            }
        }

        static bool NeedToDownload(string timestamp_file, string server_file_time)
        {
            if (File.Exists(timestamp_file))
            {
                string last_download_time = File.ReadAllText(timestamp_file);
                if (server_file_time == last_download_time)
                {
                    return false;
                }
            }
            return true;
        }

        public static void DownloadFile(WebRequest request, string dst_file_path)
        {
            string tmp_file = dst_file_path + ".tmp";
            string uri = request.RequestUri.ToString();
            string protocol = uri.Substring(0, uri.IndexOf(':')).ToLower();
            if (protocol == "ftp")
                FtpDownload((FtpWebRequest)request, tmp_file);
            else if (protocol == "http")
                HttpDownload((HttpWebRequest)request, tmp_file);
            else
                throw new ApplicationException(string.Format("Invalid protocol: {0}", protocol));
            File.Delete(dst_file_path);
            File.Move(tmp_file, dst_file_path);
        }

        static void FtpDownload(FtpWebRequest request, string dst_file_path)
        {
            request.Method = WebRequestMethods.Ftp.DownloadFile;
            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                SaveWebResponseToFile(response, dst_file_path);
            }
        }

        static void HttpDownload(HttpWebRequest request, string dst_file_path)
        {
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                SaveWebResponseToFile(response, dst_file_path);
            }
        }

        static void SaveWebResponseToFile(WebResponse response, string dst_file_path)
        {
            Stream responseStream = response.GetResponseStream();
            BinaryReader reader = new BinaryReader(responseStream);
            using (FileStream fs = new FileStream(dst_file_path, FileMode.Create))
            {
                do
                {
                    Int32 bytes_per_read = 65536;
                    byte[] ftp_buffer = reader.ReadBytes(bytes_per_read);
                    fs.Write(ftp_buffer, 0, (int)ftp_buffer.Length);
                    if (ftp_buffer.Length != bytes_per_read)
                        break;
                } while (true);
            }
        }

        static FileListStyle GetFileListStyle(string str)
        {
            FileListStyle style = FileListStyle.Unknown;
            if (Regex.IsMatch(str.Substring(0, 10), "(-|d)(-|r)(-|w)(-|x)(-|r)(-|w)(-|x)(-|r)(-|w)(-|x)"))
                style = FileListStyle.UnixStyle;
            else if (Regex.IsMatch(str.Substring(0, 8), "[0-9][0-9]-[0-9][0-9]-[0-9][0-9]"))
                style = FileListStyle.WindowsStyle;
            return style;
        }

        enum FileListStyle
        {
            UnixStyle,
            WindowsStyle,
            Unknown
        }

    }
}
