//
// Copyright (C) 2012 Timo Dörr
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using JsonFx.Json;

namespace JsonConfig
{
    public class Config
    {
        private static List<string> _privateBinPath;

        static Config()
        {
            _privateBinPath = new List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
            };

        }
        public delegate void UserConfigFileChangedHandler();

        /// <summary>
        ///     Get the global <see cref="Config" /> instance of the current appdomain.
        /// </summary>
        public static readonly Config Global = new Config();

        private static readonly ConcurrentDictionary<Assembly, Config> Cache =
            new ConcurrentDictionary<Assembly, Config>();

        public static readonly string DefaultEnding = ".conf";
        private readonly dynamic _default;
        private readonly ReaderWriterLockSlim _readerWriterLockSlim = new ReaderWriterLockSlim();
        private dynamic _user = new ConfigObject();
        private FileInfo _userConfigFileInfo;
        private FileSystemWatcher _userConfigWatcher;

        private Config(Assembly callingAssembly = null)
        {
            // run to check for compiled/embedded config

            // scan ALL linked assemblies and merge their default configs while
            // giving the entry assembly top priority in merge
            var entryAssembly = Assembly.GetEntryAssembly();

            if (callingAssembly != null)
            {
                _default = GetDefaultConfig(callingAssembly);
            }
            else
            {
                _default = entryAssembly != null ? GetDefaultConfig(entryAssembly) : new ConfigObject();
            }


            // User config (provided through a settings.conf file for Global config or a [AssemblyName].conf for Local config)
            var userConfigFilename = callingAssembly == null ? "settings" : callingAssembly.GetName().Name;
            var getUserConfigFileInfo = (Func<string, FileInfo>)(searchDirectory =>
                !string.IsNullOrWhiteSpace(searchDirectory) && Directory.Exists(searchDirectory) ?
                (from FileInfo fi in new DirectoryInfo(searchDirectory).GetFiles()
                 where (
                     fi.FullName.EndsWith(userConfigFilename + ".conf") ||
                     fi.FullName.EndsWith(userConfigFilename + ".json") ||
                     fi.FullName.EndsWith(userConfigFilename + ".conf.json") ||
                     fi.FullName.EndsWith(userConfigFilename + ".json.conf")
                     )
                 select fi).FirstOrDefault() : null);
            FileInfo userConfigFileInfo = null;

            if (callingAssembly == null)
            {
                var searchDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.RelativeSearchPath ?? "");
                userConfigFileInfo = getUserConfigFileInfo(searchDirectoryPath);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(callingAssembly.CodeBase) && callingAssembly.CodeBase.StartsWith("file:///"))
                {
                    var searchDirectoryPath = Path.GetDirectoryName(
                        callingAssembly.CodeBase.Substring("file:///".Length).Replace(Path.DirectorySeparatorChar, '\\'));

                    userConfigFileInfo = getUserConfigFileInfo(searchDirectoryPath);
                }
                if (userConfigFileInfo == null)
                {
                    if (!string.IsNullOrWhiteSpace(callingAssembly.Location))
                    {
                        var searchDirectoryPath = Path.GetDirectoryName(
                            callingAssembly.Location);

                        userConfigFileInfo = getUserConfigFileInfo(searchDirectoryPath);
                    }
                }
                if (userConfigFileInfo == null)
                {
                    var searchDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.RelativeSearchPath ?? "");
                    userConfigFileInfo = getUserConfigFileInfo(searchDirectoryPath);
                }
                
            }

            if (userConfigFileInfo != null)
            {
                SetAndWatchUserConfig(userConfigFileInfo);
            }
            else
            {
                _user = _default.Clone();
            }
        }

        /// <summary>
        ///     Get the <see cref="Config" /> instance for the Executing Assembly
        /// </summary>
        public static Config Local
        {
            get { return GetConfig(Assembly.GetCallingAssembly()); }
        }

        public dynamic User
        {
            get
            {
                _readerWriterLockSlim.EnterReadLock();
                try
                {
                    return _user;
                }
                finally
                {
                    _readerWriterLockSlim.ExitReadLock();
                }
            }
            set
            {
                dynamic configObject = null;
                if (value is ConfigObject)
                {
                    configObject = value;
                }
                else
                {
                    var expandoObject = value as ExpandoObject;
                    if (expandoObject != null)
                    {
                        configObject = ConfigObject.FromExpando(expandoObject);
                    }
                }
                if (configObject != null)
                {
                    SetUserConfig(value);
                }
                else
                {
                    _user = _default.Clone();
                }
            }
        }

        /// <summary>
        ///     Get the <see cref="Config" /> instance for the specified assembly.
        /// </summary>
        /// <param name="assembly">The specified assembly</param>
        /// <returns>The <see cref="Config" /> instance for the specified assembly.</returns>
        public static Config GetConfig(Assembly assembly)
        {
            return Cache.GetOrAdd(assembly, _ => new Config(assembly));
        }

        /// <summary>
        ///     Gets a ConfigObject that represents the current configuration. Since it is
        ///     a cloned copy, changes to the underlying configuration files that are done
        ///     after GetCurrentScope() is called, are not applied in the returned instance.
        /// </summary>
        public ConfigObject GetCurrentScope()
        {
            _readerWriterLockSlim.EnterReadLock();
            try
            {
                if (_user is NullExceptionPreventer)
                    return new ConfigObject();
                else
                    return _user.Clone();
            }
            finally
            {
                _readerWriterLockSlim.ExitReadLock();
            }
        }

        public event UserConfigFileChangedHandler OnUserConfigFileChanged;

        /// <summary>
        ///     Read <see cref="_user" /> from the given json file, and watch the file for change.
        /// </summary>
        /// <param name="info">The given json file.</param>
        public void SetAndWatchUserConfig(FileInfo info)
        {
            if (info == null || info.Directory == null) return;

            _readerWriterLockSlim.EnterWriteLock();

            try
            {
                //Suspend previous User Config Watcher if exists.
                if (_userConfigWatcher != null)
                {
                    _userConfigWatcher.EnableRaisingEvents = false;
                }

                try
                {
                    _user = _default + ParseJson(File.ReadAllText(info.FullName));
                }
                catch (Exception)
                {
                    if (_userConfigWatcher != null)
                    {
                        _userConfigWatcher.EnableRaisingEvents = true;
                    }
                    throw;
                }
                var userConfigWatcher = new FileSystemWatcher(info.Directory.FullName, info.Name)
                {
                    NotifyFilter = NotifyFilters.LastWrite
                };
                userConfigWatcher.Changed += delegate
                {
                    _readerWriterLockSlim.EnterWriteLock();
                    try
                    {
                        _user = _default + ParseJson(File.ReadAllText(info.FullName));
                        Debug.WriteLine("user configuration has changed, updating config information");

                        // trigger our event
                        var handler = OnUserConfigFileChanged;
                        if (handler != null)
                        {
                            handler();
                        }
                    }
                    finally
                    {
                        _readerWriterLockSlim.ExitWriteLock();
                    }
                };

                //Dispose previous User Config Watcher if exists.
                if (_userConfigWatcher != null)
                {
                    _userConfigWatcher.Dispose();
                }
                userConfigWatcher.EnableRaisingEvents = true;

                _userConfigWatcher = userConfigWatcher;

                _userConfigFileInfo = info;
            }
            finally
            {
                _readerWriterLockSlim.ExitWriteLock();
            }
        }

        public void Save()
        {
            SaveInternal(false);
        }

        private void SaveInternal(bool hasLock)
        {
            if (!hasLock)
            {
                _readerWriterLockSlim.EnterReadLock();
            }
            try
            {
                if (_userConfigFileInfo == null)
                {
                    return;
                }
                if (_userConfigWatcher != null)
                {
                    //Pause the user config watcher.
                    _userConfigWatcher.EnableRaisingEvents = false;
                }
                var configObject = _user as ConfigObject;
                string configString = configObject != null
                    ? configObject.GetJsonToSave()
                    : (_user ?? new ConfigObject()).ToString();
                File.WriteAllText(_userConfigFileInfo.FullName, configString);
            }
            finally
            {
                if (!hasLock)
                {
                    _readerWriterLockSlim.ExitReadLock();
                }
            }
        }

        public static ConfigObject ApplyJsonFromFileInfo(FileInfo file, ConfigObject config = null)
        {
            var overlayJson = File.ReadAllText(file.FullName);
            dynamic overlayConfig = ParseJson(overlayJson);
            return Merger.Merge(overlayConfig, config);
        }

        public static ConfigObject ApplyJsonFromPath(string path, ConfigObject config = null)
        {
            return ApplyJsonFromFileInfo(new FileInfo(path), config);
        }

        public static ConfigObject ApplyJson(string json, ConfigObject config = null)
        {
            if (config == null)
                config = new ConfigObject();

            dynamic parsed = ParseJson(json);
            return Merger.Merge(parsed, config);
        }

        // seeks a folder for .conf files
        public static ConfigObject ApplyFromDirectory(string path, ConfigObject config = null, bool recursive = false)
        {
            if (!Directory.Exists(path))
                throw new Exception("no folder found in the given path");

            if (config == null)
                config = new ConfigObject();

            var info = new DirectoryInfo(path);
            if (recursive)
            {
                foreach (var dir in info.GetDirectories())
                {
                    Console.WriteLine("reading in folder {0}", dir);
                    config = ApplyFromDirectoryInfo(dir, config, true);
                }
            }

            // find all files
            var files = info.GetFiles();
            foreach (var file in files)
            {
                Console.WriteLine("reading in file {0}", file);
                config = ApplyJsonFromFileInfo(file, config);
            }
            return config;
        }

        public static ConfigObject ApplyFromDirectoryInfo(DirectoryInfo info, ConfigObject config = null,
            bool recursive = false)
        {
            return ApplyFromDirectory(info.FullName, config, recursive);
        }

        public static ConfigObject ParseJson(string json, bool isDefault = false)
        {
            var lines = json.Split('\n');
            // remove lines that start with a dash # character 
            var filtered = from l in lines
                           where !(Regex.IsMatch(l, @"^\s*#(.*)"))
                           select l;

            var filteredJson = string.Join("\n", filtered);

            var jsonReader = new JsonReader();
            dynamic parsed = jsonReader.Read(filteredJson);
            // convert the ExpandoObject to ConfigObject before returning
            return ConfigObject.FromExpando(parsed, isDefault);
        }

        //// overrides any default config specified in default.conf
        //public void SetDefaultConfig(dynamic config)
        //{
        //    _user = config;
        //}
        public void SetUserConfig(ConfigObject config)
        {
            _readerWriterLockSlim.EnterWriteLock();
            try
            {
                _user = _default + config;
                if (_userConfigFileInfo != null)
                {
                    if (_userConfigWatcher != null)
                    {
                        _userConfigWatcher.EnableRaisingEvents = false;
                    }
                    SaveInternal(true);
                    if (_userConfigWatcher != null)
                    {
                        _userConfigWatcher.EnableRaisingEvents = true;
                    }
                }
            }
            finally
            {
                _readerWriterLockSlim.ExitWriteLock();
            }
        }

        private dynamic GetDefaultConfig(Assembly assembly)
        {
            var dconfJson = ScanForDefaultConfig(assembly);
            if (string.IsNullOrWhiteSpace(dconfJson))
                return new ConfigObject(true);
            return ParseJson(dconfJson, true);
        }

        private string ScanForDefaultConfig(Assembly assembly)
        {
            if (assembly == null)
                assembly = Assembly.GetEntryAssembly();

            string[] res;
            try
            {
                // this might fail for the 'Anonymously Hosted DynamicMethods Assembly' created by an Reflect.Emit()
                res = assembly.GetManifestResourceNames();
            }
            catch
            {
                // for those assemblies, we don't provide a config
                return null;
            }
            var dconfResource =
                res.FirstOrDefault(r => r.EndsWith("default.conf", StringComparison.OrdinalIgnoreCase) ||
                                        r.EndsWith("default.json", StringComparison.OrdinalIgnoreCase) ||
                                        r.EndsWith("default.conf.json", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(dconfResource))
                return null;

            var stream = assembly.GetManifestResourceStream(dconfResource);
            if (stream != null)
            {
                var defaultJson = new StreamReader(stream).ReadToEnd();
                return defaultJson;
            }
            return "";
        }
    }
}