﻿// Copyright (c) 2022-2024, David Karnok & Contributors
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Text.RegularExpressions;

namespace MiscPluginUpdateChecker

{
    /// <summary>
    /// Helper methods.
    /// Keeping them separate so they can be unit tested.
    /// </summary>
    internal class Helpers
    {
        internal const string defaultVersionInfoRepository = "https://raw.githubusercontent.com/akarnokd/ThePlanetCrafterMods/main/version_info_repository.xml";

        internal static string GetBepInPluginVersionQuoteFrom(string text)
        {
            string pattern = "BepInPlugin\\(.*\"(.*?)\"\\s*\\)";
            var regEx = new Regex(pattern);

            var match = regEx.Match(text);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return null;
        }

        internal static string GetCsprojVersionTag(string text)
        {
            string pattern = "<Version>(.*?)</Version>";
            var regEx = new Regex(pattern);

            var match = regEx.Match(text);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return null;
        }
        internal static Dictionary<string, PluginEntry> DownloadPluginInfos(
            string startUrl,
            Action<object> logInfo, 
            Action<object> logWarning,
            Action<object> logError,
            bool randomArgument)
        {
            logInfo("Download Repositories <- Begin");

            HashSet<string> repoUrlsSeen = [];
            Queue<string> repoUrls = new();
            repoUrls.Enqueue(startUrl);

            Dictionary<string, PluginEntry> plugins = [];

            while (repoUrls.Count != 0)
            {
                var repoUrl = repoUrls.Dequeue();
                if (repoUrlsSeen.Add(repoUrl))
                {
                    logInfo("  Repository " + repoUrl);
                    try
                    {
                        var request = WebRequest.Create(MaybeRandom(repoUrl, randomArgument)).NoCache();

                        using var response = request.GetResponse();
                        using var stream = response.GetResponseStream();
                        var vir = VersionInfoRepository.Load(stream);

                        foreach (PluginEntry pe in vir.plugins)
                        {
                            if (plugins.ContainsKey(pe.guid))
                            {
                                logWarning("    Duplicate plugin info");
                                logWarning("      repository = " + repoUrl);
                                logWarning("      guid = " + pe.guid);
                            }
                            else
                            {
                                plugins[pe.guid] = pe;
                            }
                        }

                        logInfo("    Plugins found: " + vir.plugins.Count);

                        foreach (var inc in vir.includes)
                        {
                            repoUrls.Enqueue(inc.url);
                        }
                    }
                    catch (Exception ex)
                    {
                        logError(ex);
                    }
                }
            }

            logInfo("Download Repositories <- Done");
            logInfo("Discovering versions <- Begin");

            DiscoverVersions(plugins, logInfo, logWarning, logError, randomArgument);

            logInfo("Discovering versions <- Done");

            return plugins;
        }

        internal static void DiscoverVersions(
            Dictionary<string, PluginEntry> plugins,
            Action<object> logInfo,
            Action<object> logWarning,
            Action<object> logError,
            bool randomArgument)
        {
            foreach (var kv in plugins)
            {
                var key = kv.Key;
                var value = kv.Value;

                try
                {
                    if (value.discoverMethod != DiscoverMethod.Unknown)
                    {
                        logInfo("  Plugin version discovery " + key);
                        logInfo("    discover = " + value.discoverUrl);
                        logInfo("    method = " + value.discoverMethod);

                        string source = GetStringFromUrl(MaybeRandom(value.discoverUrl, randomArgument));

                        if (value.discoverMethod == DiscoverMethod.BepInPluginVersionQuote)
                        {
                            value.discoverVersion = Version.Parse(Helpers.GetBepInPluginVersionQuoteFrom(source));
                            logInfo("    version = " + value.discoverVersion); ;
                        }
                        else
                        if (value.discoverMethod == DiscoverMethod.CsprojVersionTag)
                        {
                            value.discoverVersion = Version.Parse(Helpers.GetCsprojVersionTag(source));
                            logInfo("    version = " + value.discoverVersion); ;
                        }
                        else
                        {
                            logWarning("    version = unsupported discovery method");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logError(ex);
                }
            }
        }

        static string GetStringFromUrl(string url)
        {
            var request = WebRequest.Create(url).NoCache();
            using var response = request.GetResponse();
            using var stream = response.GetResponseStream();

            var reader = new StreamReader(stream, Encoding.UTF8);

            return reader.ReadToEnd();
        }

        static string MaybeRandom(string url, bool randomize)
        {
            if (randomize)
            {
                return url + "?v=" + DateTime.UtcNow.Ticks;
            }
            return url;
        }
    }

    static class HelpersExt
    {
        internal static WebRequest NoCache(this WebRequest request)
        {
            request.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            return request;
        }
    }
}
