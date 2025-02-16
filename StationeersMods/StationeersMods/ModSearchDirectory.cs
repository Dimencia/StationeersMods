﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace StationeersMods
{
    /// <summary>
    ///     Represents a directory that is monitored for Mods.
    /// </summary>
    public class ModSearchDirectory : IDisposable
    {
        private readonly Dictionary<string, long> _modPaths;

        private readonly Thread backgroundRefresh;
        private bool disposed;
        private readonly AutoResetEvent refreshEvent;

        /// <summary>
        ///     Initialize a new ModSearchDirectory with a path.
        /// </summary>
        /// <param name="path">The path to the search directory.</param>
        public ModSearchDirectory(string path)
        {
            this.BasePath = Path.GetFullPath(path);

            if (!Directory.Exists(this.BasePath))
                throw new DirectoryNotFoundException(this.BasePath);

            _modPaths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            refreshEvent = new AutoResetEvent(false);

            backgroundRefresh = new Thread(BackgroundRefresh);
            backgroundRefresh.Start();
        }

        /// <summary>
        ///     This ModSearchDirectory's path.
        /// </summary>
        public string BasePath { get; }

        /// <summary>
        ///     Releases all resources used by the ModSearchDirectory.
        /// </summary>
        public void Dispose()
        {
            ModFound = null;
            ModRemoved = null;
            ModChanged = null;

            disposed = true;
            refreshEvent.Set();
            backgroundRefresh.Join();
        }

        /// <summary>
        ///     Occurs when a new Mod has been found.
        /// </summary>
        public event Action<string,string> ModFound;

        /// <summary>
        ///     Occurs when a Mod has been removed.
        /// </summary>
        public event Action<string,string> ModRemoved;

        /// <summary>
        ///     Occurs when a change to a Mod's directory has been detected.
        /// </summary>
        public event Action<string,string> ModChanged;

        /// <summary>
        ///     Occurs when any change was detected for any Mod in this search directory.
        /// </summary>
        public event Action ModsChanged;

        /// <summary>
        ///     Refresh the collection of mod paths. Remove all missing paths and add all new paths.
        /// </summary>
        public void Refresh()
        {
            refreshEvent.Set();
        }

        private void BackgroundRefresh()
        {
            Thread.CurrentThread.IsBackground = true;

            try
            {
                refreshEvent.WaitOne();

                while (!disposed)
                {
                    DoRefresh();

                    refreshEvent.WaitOne();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void DoRefresh()
        {
            //LogUtility.LogDebug("Refreshing Mod search directory: " + path);

            var changed = false;

            var modInfoPaths = GetModInfoPaths();

            foreach (var path in _modPaths.Keys.ToArray())
            {
                // if (!modInfoPaths.Contains(path))
                // {
                //     changed = true;
                //     RemoveModPath(path);
                //     continue;
                // }

                var modDirectory = new DirectoryInfo(Path.GetDirectoryName(path));

                var currentTicks = DateTime.Now.Ticks;
                var lastWriteTime = _modPaths[path];

                if (modDirectory.LastWriteTime.Ticks > lastWriteTime)
                {
                    changed = true;
                    _modPaths[path] = currentTicks;
                    UpdateModPath(path);
                    continue;
                }

                foreach (var directory in modDirectory.GetDirectories("*", SearchOption.AllDirectories))
                    if (directory.LastWriteTime.Ticks > lastWriteTime)
                    {
                        changed = true;
                        _modPaths[path] = currentTicks;
                        UpdateModPath(path);
                        break;
                    }

                foreach (var file in modDirectory.GetFiles("*", SearchOption.AllDirectories))
                {
                    if (file.Extension == ".info")
                        continue;

                    if (file.LastWriteTime.Ticks > lastWriteTime)
                    {
                        changed = true;
                        _modPaths[path] = currentTicks;
                        UpdateModPath(path);
                        break;
                    }
                }
            }

            foreach (var path in modInfoPaths)
                if (!_modPaths.ContainsKey(path))
                {
                    changed = true;
                    AddModPath(path);
                }

            if (changed)
                ModsChanged?.Invoke();
        }

        private void AddModPath(string path)
        {
            if (_modPaths.ContainsKey(path))
                return;

            _modPaths.Add(path, DateTime.Now.Ticks);

            ModFound?.Invoke(GetSubFolderPath(this.BasePath, path), path);
        }

        private void RemoveModPath(string path)
        {
            if (!_modPaths.ContainsKey(path))
                return;

            _modPaths.Remove(path);
            ModRemoved?.Invoke(GetSubFolderPath(this.BasePath, path), path);
        }

        private void UpdateModPath(string path)
        {
            if (!File.Exists(path))
            {
                RemoveModPath(path);
                return;
            }
            
            ModChanged?.Invoke(GetSubFolderPath(this.BasePath, path), path);
        }
        public string GetSubFolderPath(string basePath, string path)
        {
            var baseUri = new Uri(basePath);
            var pathUri = new Uri(path);

            if (baseUri.IsBaseOf(pathUri))
            {
                var relativeUri = baseUri.MakeRelativeUri(pathUri);
                var segments = relativeUri.ToString().Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length > 0)
                {
                    return Path.Combine(basePath, segments[0]);
                }
            }

            return path;
        }
        private string[] GetModInfoPaths()
        {
            string[] paths = Directory.GetFiles(BasePath, "*.info", SearchOption.AllDirectories);
            if (paths.Length == 0)
            {
                paths = Directory.GetFiles(BasePath, "*.dll", SearchOption.AllDirectories);
            }

            return paths;
        }
    }
}