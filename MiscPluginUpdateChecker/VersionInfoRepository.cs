﻿// Copyright (c) 2022-2024, David Karnok & Contributors
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace MiscPluginUpdateChecker
{
    internal class VersionInfoRepository
    {
        internal readonly List<PluginEntry> plugins = [];
        internal readonly List<IncludeEntry> includes = [];

        internal static VersionInfoRepository Load(Stream stream)
        {
            var root = XDocument.Load(stream).Root;
            var result = new VersionInfoRepository();

            ProcessDocument(root, result);

            return result;
        }
        internal static VersionInfoRepository Parse(string xmlString)
        {
            var root = XDocument.Parse(xmlString).Root;

            var result = new VersionInfoRepository();

            ProcessDocument(root, result);

            return result;
        }

        static void ProcessDocument(XElement root, VersionInfoRepository result)
        {
            foreach (var xPlugin in root.Elements())
            {
                if (xPlugin.Name.LocalName == "plugin")
                {
                    var pluginEntry = new PluginEntry
                    {
                        guid = xPlugin.AttributeWithName("guid"),
                        description = xPlugin.AttributeWithName("description")
                    };

                    var v = xPlugin.AttributeWithName("version");
                    if (v != null) {
                        pluginEntry.explicitVersion = Version.Parse(v);
                    }
                    pluginEntry.discoverUrl = xPlugin.AttributeWithName("discover");
                    pluginEntry.link = xPlugin.AttributeWithName("link");

                    var dm = xPlugin.AttributeWithName("method");
                    pluginEntry.discoverMethod = dm switch
                    {
                        "BepInPluginVersionQuote" => DiscoverMethod.BepInPluginVersionQuote,
                        "CsprojVersionTag" => DiscoverMethod.CsprojVersionTag,
                        _ => DiscoverMethod.Unknown
                    };

                    foreach (var xChangelog in xPlugin.Elements())
                    {
                        if (xChangelog.Name.LocalName == "changelog")
                        {
                            var logEntry = new ChangelogEntry
                            {
                                version = Version.Parse(xChangelog.AttributeWithName("version")),
                                title = xChangelog.AttributeWithName("title"),
                                link = xChangelog.AttributeWithName("link"),
                                content = xChangelog.Value
                            };

                            pluginEntry.changelog.Add(logEntry);
                        }
                    }

 
                    result.plugins.Add(pluginEntry);
                }
                else
                if (xPlugin.Name == "include")
                {
                    var ie = new IncludeEntry
                    {
                        url = xPlugin.AttributeWithName("url")
                    };
                    result.includes.Add(ie);
                }
            }
        }
    }

    internal class PluginEntry
    {
        internal string guid;
        internal string description;
        internal Version explicitVersion;
        internal string discoverUrl;
        internal DiscoverMethod discoverMethod;
        internal Version discoverVersion;
        internal string link;
        internal readonly List<ChangelogEntry> changelog = [];

        internal Version Version
        {
            get
            {
                if (explicitVersion != null)
                {
                    return explicitVersion;
                }
                if (discoverVersion != null)
                {
                    return discoverVersion; ;
                }
                return null;
            }
        }

        internal int CompareToVersion(Version other)
        {
            if (explicitVersion != null)
            {
                return explicitVersion.CompareTo(other);
            }
            if (discoverVersion != null)
            {
                return discoverVersion.CompareTo(other);
            }
            return 0;
        }
    }

    internal class ChangelogEntry
    {
        internal Version version;
        internal string title;
        internal string link;
        internal string content;
    }
    
    enum DiscoverMethod
    {
        Unknown,
        BepInPluginVersionQuote,
        CsprojVersionTag
    }

    internal class IncludeEntry
    {
        internal string url;
    }

    static class XHelper
    {
        public static XElement ElementWithName(this XElement src, string name)
        {
            return src.Element(XName.Get(name));
        }
        public static string AttributeWithName(this XElement src, string name)
        {
            return src.Attribute(XName.Get(name))?.Value;
        }
    }
}
