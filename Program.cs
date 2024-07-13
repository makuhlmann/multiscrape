using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Threading;
using System.Web;

namespace multiscrape {
    internal class Program {
        static bool skipWayback = false;
        static bool skipDirect = false;
        static readonly bool dirStruct = true;
        static readonly List<string> patterns = new List<string>();
        static readonly WebClient webClient = new WebClient();
        //static string minTimestamp = "19950101120000";
        static readonly string startTime = DateTime.Now.ToString("yyyyMMddhhmmss");
        static string currentFile = "";
        static readonly List<string> downloadLists = new List<string>();

        static List<string> currentList = new List<string>();

        static void Main(string[] args) {
            for (int i = 0; i < args.Length; i++) {
                string path = args[i];
                if ((path.StartsWith("/") || path.StartsWith("-")) && path.Length == 2) {
                    switch (path.Substring(1, 1)) {
                        case "w":
                            skipWayback = true;
                            continue;
                        case "d":
                            skipDirect = true;
                            continue;
                        case "p":
                            patterns.Add(args[i + 1]);
                            i++;
                            continue;
                        case "?":
                            Console.WriteLine("Multiscrape Usage: multiscrape [/options] path1 [path2] [path3] ...\n\n" +
                            "/w - Skip Wayback Machine scraping\n" +
                            "/d - Skip direct download scraping\n" +
                            "/p - Add custom mirror pattern\n");
                            return;
                        default:
                            Console.WriteLine($"Unknown option {path}");
                            return;
                    }
                } else {
                    if (File.Exists(path)) {
                        downloadLists.Add(path);
                    } else {
                        Log("Invalid path: " + path);
                    }
                }
            }
            foreach (var path in downloadLists) {
                currentFile = path;
                currentList = new List<string>();
                currentList.AddRange(File.ReadLines(path));
                DownloadList();
            }
        }

        static void DownloadList() {
            // Step 1 - Direct
            if (!skipDirect) {
                foreach (string url in currentList.ToArray()) {
                    if (url.StartsWith("#") || string.IsNullOrWhiteSpace(url))
                        continue;
                    if (url.Contains("://"))
                        DownloadDirect(url);
                    else
                        DownloadDirect("http://" + url);
                }
            }

            // Step 2 - Pattern
            if (patterns.Count > 0 && currentList.Count > 0) {
                foreach (string url in currentList.ToArray()) {
                    if (url.StartsWith("#") || string.IsNullOrWhiteSpace(url))
                        continue;
                    if (url.Contains("://"))
                        DownloadPattern(url);
                    else
                        DownloadPattern("http://" + url);
                }
            }

            // Step 3 - Wayback
            if (!skipWayback && currentList.Count > 0) {
                foreach (string url in currentList.ToArray()) {
                    if (url.StartsWith("#") || string.IsNullOrWhiteSpace(url))
                        continue;
                    if (url.Contains("://"))
                        DownloadWayback(url);
                    else
                        DownloadWayback("http://" + url);
                }
            }

            // Fail
            if (currentList.Count > 0) {
                Log($"Failed to download {currentList.Count} files via all available methods.");
                foreach (var url in currentList) {
                    File.AppendAllLines($"mslog_err_{startTime}_{Path.GetFileNameWithoutExtension(currentFile)}.txt", new string[] { url });
                }
            }
        }

        static void DownloadDirect(string url) {
            // Step 1 - Direct
            Log($"Downloading {url} [direct]");
            if (DownloadFile(url)) {
                Log("Download completed.");
                File.AppendAllLines($"mslog_ok_{startTime}_{Path.GetFileNameWithoutExtension(currentFile)}.txt", new string[] { $"d|{url}" });
                currentList.Remove(url);
                return;
            }
        }

        static void DownloadPattern(string url) {
            // Step 2 - Pattern
            for (int i = 0; i < patterns.Count; i++) {
                Log($"Downloading {url} [pattern {i}]");
                string pattern = patterns[i];
                Uri uri = new Uri(url);
                string replacedUrl = pattern.Replace("%s", uri.Scheme)
                                            .Replace("%h", uri.Host)
                                            .Replace("%p", uri.AbsolutePath)
                                            .Replace("%q", uri.Query);
                if (DownloadFile(replacedUrl, url)) {
                    Log("Download completed.");
                    File.AppendAllLines($"mslog_ok_{startTime}_{Path.GetFileNameWithoutExtension(currentFile)}.txt", new string[] { $"p{i}|{url}" });
                    currentList.Remove(url);
                    return;
                }
            }
        }

        static void DownloadWayback(string url) {
            // Step 3 - Wayback
            Log($"Downloading {url} [wayback]");
            string timestamp;
            string waybackApiResponse = DownloadString($"http://web.archive.org/cdx/search/cdx?fl=statuscode,timestamp&filter=statuscode:[23]{{1}}0[02]&url={HttpUtility.UrlEncode(url)}");

            foreach (var line in waybackApiResponse.Split('\n')) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                timestamp = line.Split(' ')[1];

                if (DownloadFile($"http://web.archive.org/web/{timestamp}id_/{url}", url)) {
                    Log("Download completed.");
                    File.AppendAllLines($"mslog_ok_{startTime}_{Path.GetFileNameWithoutExtension(currentFile)}.txt", new string[] { $"w|{url}" });
                    currentList.Remove(url);
                    return;
                }
            }
        }

        static string DownloadString(string url) {
            try {
                return webClient.DownloadString(url);
            } catch (WebException we) {
                Log($"API access failed: {we.Message}");

                if (we.Message.Contains("Unable to connect to the remote server") && url.Contains("web.archive.org")) {
                    Log("Sleeping 60 seconds -> Wayback cooldown");
                    Thread.Sleep(60000);
                    Log("Retrying...");
                    return DownloadString(url);
                }

                return "";
            }
        }

        static void Log(string message) {
            File.AppendAllLines($"mslog_{startTime}_{Path.GetFileNameWithoutExtension(currentFile)}.txt", new string[] { message });
            Console.WriteLine(message);
        }

        static bool DownloadFile(string url, string origurl = "") {
            if (origurl?.Length == 0)
                origurl = url;

            Uri uri = new Uri(HttpUtility.UrlDecode(origurl));
            string path = uri.Host + String.Concat(uri.Segments.Take(uri.Segments.Length - 1)).Replace("://", "/").Replace("%20", " ");
            string filePath = uri.Host + String.Concat(uri.Segments).Replace("://", "/").Replace("%20", " ");

            while (path.Contains(" /"))
                path.Replace(" /", "/");
            while (filePath.Contains(" /"))
                filePath.Replace(" /", "/");

            while (filePath.EndsWith("/") || filePath.EndsWith(".") || filePath.EndsWith("?") || filePath.EndsWith("#") || filePath.EndsWith(" "))
                filePath = filePath.Substring(0, filePath.Length - 1);

            if (File.Exists(filePath + ".dltemp")) {
                Log("Found interrupted download, removed");
            }

            if (File.Exists(filePath)) {
                Log("File already downloaded, skipping");
                return true;
            }

            try {
                if (dirStruct && !Directory.Exists(path))
                    Directory.CreateDirectory(path);

                webClient.DownloadFile(url, filePath + ".dltemp");

                File.Move(filePath + ".dltemp", filePath);
            } catch (WebException we) {
                Log($"Download failed: {we.Message}");

                if (File.Exists(filePath + ".dltemp"))
                    File.Delete(filePath + ".dltemp");

                if (we.Message.Contains("Unable to connect to the remote server") && url.Contains("web.archive.org")) {
                    Log("Sleeping 60 seconds -> Wayback cooldown");
                    Thread.Sleep(60000);
                    Log("Retrying...");
                    return DownloadFile(url, origurl);
                }

                return false;
            }

            return true;
        }
    }
}
