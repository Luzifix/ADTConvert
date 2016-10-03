using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;
using System.Reflection;

namespace ADTConvert
{
    class VersionCheck
    {
        public static void CheckForUpdate()
        {
            ConsoleConfig config = ConsoleConfig.Instance;

            if (config.Verbose)
                Console.WriteLine("\n--- Version Check ---");

            using (var client = new WebClient())
            {
                string AppName = Assembly.GetExecutingAssembly().GetName().Name.ToString();
                string AppVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                string realaseURL = Properties.Settings.Default["ReleaseURL"].ToString();
                string realaseAPI = Properties.Settings.Default["ReleaseAPI"].ToString();
                string userAgent = Properties.Settings.Default["UserAgent"].ToString();
                client.Headers["User-Agent"] = userAgent + AppVersion;

                try
                {
                    string json = client.DownloadString(realaseAPI);
                    var serializer = new JavaScriptSerializer();
                    IList<GithubRealaseModel> model = serializer.Deserialize<IList<GithubRealaseModel>>(json);

                    if (config.Verbose)
                        Console.WriteLine("Found {0} realases on Github", model.Count);

                    if (model.Count > 0 && model[0].tag_name != AppVersion)
                    {
                        if (config.Verbose)
                            Console.WriteLine("Github Version {0}", model[0].tag_name);

                        string text = $"Your {AppName} version is outdated.";

                        if (model[0].assets.Count > 0)
                        {
                            text += $" Press \"Y\" to download the new version {model[0].tag_name}.";
                            realaseURL = model[0].assets[0].browser_download_url;
                        }
                        else
                        {
                            text += " Press \"Y\" to open the github release page.";
                        }

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine(text);
                        Console.ResetColor();

                        if (Console.ReadKey().Key == ConsoleKey.Y)
                        {
                            System.Diagnostics.Process.Start(realaseURL);
                        }
                    }
                }
                catch (WebException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Version check failed:\n{0}", ex.ToString());
                    Console.ResetColor();
                }
            }
        }
    }
}
