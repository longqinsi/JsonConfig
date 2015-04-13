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
using System.Diagnostics;
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
        /// <summary>
        /// Get the global <see cref="Config"/> instance of the current appdomain.
        /// </summary>
        public static readonly Config Global = new Config();

        private static readonly ConcurrentDictionary<Assembly, Config> Cache = new ConcurrentDictionary<Assembly, Config>();

        /// <summary>
        /// Get the <see cref="Config"/> instance for the Executing Assembly
        /// </summary>
        public static Config Local
        {
            get { return GetConfig(Assembly.GetCallingAssembly()); }
        }

        /// <summary>
        /// Get the <see cref="Config"/> instance for the specified assembly.
        /// </summary>
        /// <param name="assembly">The specified assembly</param>
        /// <returns>The <see cref="Config"/> instance for the specified assembly.</returns>
        public static Config GetConfig(Assembly assembly)
        {
            return Cache.GetOrAdd(assembly, _ => new Config(assembly));
        }

        private readonly ReaderWriterLockSlim _readerWriterLockSlim = new ReaderWriterLockSlim();

        private readonly dynamic _default;

        private dynamic _user = new ConfigObject();

        public static string DefaultEnding = ".conf";

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
                _readerWriterLockSlim.EnterWriteLock();
                try
                {
                    _user = value;
                    try
                    {
                        SaveInternal(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }
                finally
                {
                    _readerWriterLockSlim.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// Gets a ConfigObject that represents the current configuration. Since it is 
        /// a cloned copy, changes to the underlying configuration files that are done
        /// after GetCurrentScope() is called, are not applied in the returned instance.
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

        public delegate void UserConfigFileChangedHandler();
        public event UserConfigFileChangedHandler OnUserConfigFileChanged;

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


            // User config (provided through a settings.conf file)
            var searchDirectoryPath = callingAssembly == null ? AppDomain.CurrentDomain.BaseDirectory : Path.GetDirectoryName(callingAssembly.Location);
            var userConfigFilename = callingAssembly == null ? "settings" : callingAssembly.GetName().Name;

            if (searchDirectoryPath == null) return;

            var d = new DirectoryInfo(searchDirectoryPath);
            var userConfig = (from FileInfo fi in d.GetFiles()
                              where (
                                  fi.FullName.EndsWith(userConfigFilename + ".conf") ||
                                  fi.FullName.EndsWith(userConfigFilename + ".json") ||
                                  fi.FullName.EndsWith(userConfigFilename + ".conf.json") ||
                                  fi.FullName.EndsWith(userConfigFilename + ".json.conf")
                                  )
                              select fi).FirstOrDefault();



            if (userConfig != null)
            {
                SetAndWatchUserConfig(userConfig);
            }
            else
            {
                _user = new ConfigObject();
            }
        }
        private FileSystemWatcher _userConfigWatcher;
        private FileInfo _userConfigFileInfo;

        /// <summary>
        /// Read <see cref="_user"/> from the given json file, and watch the file for change.
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
                _readerWriterLockSlim.ExitReadLock();
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
                File.WriteAllText(_userConfigFileInfo.FullName, _user);
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

            DirectoryInfo info = new DirectoryInfo(path);
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
        public static ConfigObject ApplyFromDirectoryInfo(DirectoryInfo info, ConfigObject config = null, bool recursive = false)
        {
            return ApplyFromDirectory(info.FullName, config, recursive);
        }

        public static ConfigObject ParseJson(string json)
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
            return ConfigObject.FromExpando(parsed);
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
                    File.WriteAllText(_userConfigFileInfo.FullName, _user);
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
                return new ConfigObject();
            return ParseJson(dconfJson);
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
            var dconfResource = res.FirstOrDefault(r => r.EndsWith("default.conf", StringComparison.OrdinalIgnoreCase) ||
                                                         r.EndsWith("default.json", StringComparison.OrdinalIgnoreCase) ||
                                                         r.EndsWith("default.conf.json", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(dconfResource))
                return null;

            var stream = assembly.GetManifestResourceStream(dconfResource);
            if (stream != null)
            {
                string defaultJson = new StreamReader(stream).ReadToEnd();
                return defaultJson;
            }
            else
            {
                return "";
            }
        }
    }
}
