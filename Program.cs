using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace multiscrape {
    internal class Program {
        static bool skipWayback = false;
        static bool skipDirect = false;
        static bool dirStruct = false;
        static List<string> patterns = new List<string>();
        static WebClient webClient = new WebClient();
        static void Main(string[] args) {
            for (int i = 0; i < args.Length; i++) {
                string path = args[i];
                if ((path.StartsWith("/") || path.StartsWith("-")) && path.Length == 2) {
                    switch (path.Substring(1, 1)) {
                        case "w":
                            skipWayback = true;
                            break;
                        case "s":
                            dirStruct = true;
                            break;
                        case "d":
                            skipDirect = true;
                            break;
                        case "p":
                            patterns.Add(args[i + 1]);
                            i++;
                            break;
                        case "?":
                            Console.WriteLine("Multiscrape Usage: multiscrape [/options] path1 [path2] [path3] ...\n\n" +
                            "/w - Skip Wayback Machine scraping\n" +
                            "/d - Skip direct download scraping" +
                            "/p - Add custom mirror pattern\n" +
                            "/s - Recreate directory structure");
                            return;
                        default:
                            Console.WriteLine($"Unknown option {path}");
                            return;
                    }
                } else {
                    if (File.Exists(path)) {
                        Download(File.ReadLines(path).ToArray());
                    } else {
                        Console.WriteLine("Invalid path: " + path);
                    }
                }
            }
        }

        static void Download(string[] urls) {
            foreach (string url in urls) {
                // Step 1 - Direct
                if (!skipDirect) {
                    if (DownloadFile(url))
                        continue;
                }
                // Step 2 - Wayback
                if (!skipWayback) {
                    WaybackAvailable waybackAvailable = JsonSerializer.Deserialize<WaybackAvailable>(url);
                }

                // Step 3 - Pattern
                foreach (var pattern in patterns) {

                }
            }
        }

        static string DownloadString(string url) {
            try {
                return webClient.DownloadString(url);
            } catch (WebException we) {
                Console.WriteLine(we.Message);
                return null;
            }
        }

        static bool DownloadFile(string url) {
            Uri uri = new Uri(url);
            string path = String.Join("/", uri.Segments.Skip(1).Take(uri.Segments.Length - 2));
            string filePath = String.Join("/", uri.Segments.Skip(1));

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            try {
                webClient.DownloadFile(url, filePath);
            } catch (WebException we) {
                Console.WriteLine(we.Message);
                return false;
            }
            return true;
        }
    }
}
