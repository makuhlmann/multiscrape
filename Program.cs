using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace multiscrape {
    internal class Program {
        static bool skipWayback = false;
        static bool skipDirect = false;
        static readonly List<string> patterns = new List<string>();
        static readonly WebClient webClient = new WebClient();
        static readonly HttpClient httpClient = new HttpClient();
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
            string filePath = PrepareDownloadDestination(url);
            if (filePath != null && DownloadFile(url, filePath)) {
                Log($"Download completed, {currentList.Count - 1} files remaining");
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
                string filePath = PrepareDownloadDestination(url);

                if (filePath != null && DownloadFile(replacedUrl, filePath)) {
                    Log($"Download completed, {currentList.Count - 1} files remaining");
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

            string filePath = PrepareDownloadDestination(url);
            if (filePath == null)
                return;

            string waybackApiResponse = DownloadString($"http://web.archive.org/cdx/search/cdx?fl=statuscode,timestamp&filter=statuscode:[23]{{1}}0[02]&url={HttpUtility.UrlEncode(url)}");

            foreach (var line in waybackApiResponse.Split('\n')) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                timestamp = line.Split(' ')[1];

                if (DownloadFile($"http://web.archive.org/web/{timestamp}id_/{url}", filePath)) {
                    Log($"Download completed, {currentList.Count - 1} files remaining");
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
                if (url.Contains("web.archive.org") && (we.Message.Contains("Unable to connect to the remote server") ||
                                                        we.Message.Contains("Gateway Timeout") ||
                                                        we.Message.Contains("Bad Gateway"))) {
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

        static string PrepareDownloadDestination(string origurl) {
            Uri uri = new Uri(HttpUtility.UrlDecode(origurl));
            string path = uri.Host + String.Concat(uri.Segments.Take(uri.Segments.Length - 1)).Replace("://", "/").Replace("%20", " ");
            string filePath = uri.Host + String.Concat(uri.Segments).Replace("://", "/").Replace("%20", " ");

            while (path.Contains(" /"))
                path = path.Replace(" /", "/");
            while (filePath.Contains(" /"))
                filePath = filePath.Replace(" /", "/");

            while (filePath.EndsWith("/") || filePath.EndsWith(".") || filePath.EndsWith("?") || filePath.EndsWith("#") || filePath.EndsWith(" "))
                filePath = filePath.Substring(0, filePath.Length - 1);

            if (File.Exists(filePath)) {
                Log($"File already downloaded - skipping, {currentList.Count - 1} files remaining");
                File.AppendAllLines($"mslog_ok_{startTime}_{Path.GetFileNameWithoutExtension(currentFile)}.txt", new string[] { $"x|{origurl}" });
                currentList.Remove(origurl);
                return null;
            }

            try {
                if (File.Exists(filePath + ".dltemp")) {
                    Log("Found interrupted download, removed");
                    File.Delete(filePath + ".dltemp");
                }

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            } catch (Exception ex) {
                Log("Error preparing destination: " + ex.Message);
            }

            return filePath;
        }

        static bool DownloadFile(string url, string filePath) {
            if (url.StartsWith("http")) {
                return DownloadFileHc(url, filePath).Result;
            }

            try {
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
                    return DownloadFile(url, filePath);
                }

                return false;
            }

            return true;
        }

        static async Task<bool> DownloadFileHc(string url, string filePath, bool retry = true) {
            try {
                DateTime lastModified;
                using (HttpResponseMessage response = httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result) {
                    if (!response.IsSuccessStatusCode) {
                        Log($"Download failed: Web server returned status code {(int)response.StatusCode} {response.StatusCode}");
                        // One time retry on this error
                        if ((int)response.StatusCode > 500 && retry && url.Contains("web.archive.org")) {
                            Log("Retrying in 60 seconds -> Wayback cooldown");
                            Thread.Sleep(60000);
                            return await DownloadFileHc(url, filePath);
                        } else if ((int)response.StatusCode > 500 && retry) {
                            Log("Retrying in 10 seconds...");
                            Thread.Sleep(10000);
                            return await DownloadFileHc(url, filePath, false);
                        }
                        return false;
                    }

                    // If response header contains file name, use that instead
                    string filename = response?.Content?.Headers?.ContentDisposition?.FileName;
                    if (!string.IsNullOrWhiteSpace(filename)) {
                        filePath = Path.Combine(Path.GetDirectoryName(filePath), filename);
                    }

                    long fileLength = response?.Content?.Headers?.ContentLength ?? 0;

                    // by eriksendc - https://github.com/dotnet/runtime/issues/16681#issuecomment-195980023
                    // based on code by TheBlueSky - https://stackoverflow.com/questions/21169573/how-to-implement-progress-reporting-for-portable-httpclient
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(filePath + ".dltemp", FileMode.Create, FileAccess.Write, FileShare.None, 131072, true)) {
                        var totalRead = 0L;
                        var buffer = new byte[131072];
                        var isMoreToRead = true;
                        long lastRead = 0;

                        Stopwatch s = Stopwatch.StartNew();

                        do {
                            var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0) {
                                isMoreToRead = false;
                            } else {
                                await fileStream.WriteAsync(buffer, 0, read);

                                totalRead += read;

                                if (s.ElapsedMilliseconds > 1000) {
                                    if (fileLength == 0)
                                        Console.WriteLine(string.Format("Progress: {0:n0} KiB ({1}%, {2:n0} KiB/s)", totalRead / 1024, "?", (totalRead - lastRead) / 1024 / (s.ElapsedMilliseconds / 1000.0)));
                                    else
                                        Console.WriteLine(string.Format("Progress: {0:n0} KiB ({1}%, {2:n0} KiB/s)", totalRead / 1024, totalRead * 100 / fileLength, (totalRead - lastRead) / 1024 / (s.ElapsedMilliseconds / 1000)));
                                    lastRead = totalRead;
                                    s.Restart();
                                }
                            }
                        }
                        while (isMoreToRead);
                    }
                    if (response.Headers.TryGetValues("X-Archive-Orig-Last-Modified", out var lxmf) && !string.IsNullOrWhiteSpace(lxmf.FirstOrDefault())) {
                        lastModified = DateTime.Parse(lxmf.FirstOrDefault());
                    } else {
                        lastModified = response.Content.Headers.LastModified != null ? response.Content.Headers.LastModified.Value.UtcDateTime : DateTime.UtcNow;
                    }
                }

                File.Move(filePath + ".dltemp", filePath);
                File.SetCreationTimeUtc(filePath, lastModified);
                File.SetLastWriteTimeUtc(filePath,lastModified);
            } catch (AggregateException e) {
                string message = "";

                // by Timothy John Laird - https://stackoverflow.com/questions/22872995/flattening-of-aggregateexceptions-for-processing
                foreach (Exception exInnerException in e.Flatten().InnerExceptions) {
                    Exception exNestedInnerException = exInnerException;
                    do {
                        if (!string.IsNullOrEmpty(exNestedInnerException.Message)) {
                            message += exNestedInnerException.Message + " -> ";
                        }
                        exNestedInnerException = exNestedInnerException.InnerException;
                    }
                    while (exNestedInnerException != null);
                }

                message = message.Substring(0, message.Length - 4);

                Log($"Download failed: {message}");

                if (File.Exists(filePath + ".dltemp"))
                    File.Delete(filePath + ".dltemp");

                if (message.Contains("the target machine actively refused") && url.Contains("web.archive.org")) {
                    Log("Sleeping 60 seconds -> Wayback cooldown");
                    Thread.Sleep(60000);
                    Log("Retrying...");
                    return await DownloadFileHc(url, filePath);
                }

                return false;
            } catch (Exception e) {
                Log($"Download failed: {e.Message}");

                // One time retry on this error
                if (e.Message.Contains("The connection was closed") && retry) {
                    Log("Retrying in 10 seconds...");
                    Thread.Sleep(10000);
                    return await DownloadFileHc(url, filePath, false);
                }

                if (File.Exists(filePath + ".dltemp"))
                    File.Delete(filePath + ".dltemp");

                return false;
            }
            return true;
        }
    }
}
