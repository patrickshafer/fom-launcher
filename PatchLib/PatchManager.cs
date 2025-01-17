﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml.Serialization;
using System.Threading;
using System.ComponentModel;
using System.Reflection;


namespace FoM.PatchLib
{
    /// <summary>
    /// Entry-point for managing the patch process
    /// </summary>
    public static class PatchManager
    {
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static Mutex PatchMutex;
        private static BackgroundWorker UpdateCheckBW;
        private static BackgroundWorker ApplyPatchBW;

        public static bool BootstrapMode = false;


        /// <summary>
        /// Applies a patch (manifest) to a given folder/directory
        /// </summary>
        /// <param name="PatchManifest">Manifest object to apply the patch from</param>
        public static void ApplyPatch(Manifest PatchManifest)
        {
            decimal TotalBytes = 0;
            decimal ProgressBytes = 0;
            int LastProgress = 0;
            bool CancelRequested = false;
            
            foreach (FileNode PatchFile in PatchManifest.FileList)
                TotalBytes += PatchFile.RemoteSize;

            foreach (FileNode PatchFile in PatchManifest.FileList)
            {
                if (ApplyPatchBW != null)
                    if (ApplyPatchBW.CancellationPending)
                        CancelRequested = true;

                if (CancelRequested)
                    break;

                if (PatchFile.CheckUpdate())
                    PatchFile.ApplyUpdate();
                ProgressBytes += PatchFile.RemoteSize;

                if (LastProgress < Convert.ToInt32((ProgressBytes / TotalBytes) * 100))
                {
                    LastProgress = Convert.ToInt32((ProgressBytes / TotalBytes) * 100);
                    if(ApplyPatchBW != null)
                        if(ApplyPatchBW.WorkerReportsProgress)
                            ApplyPatchBW.ReportProgress(LastProgress);
                }
            }
        }

        public static void ApplyPatchAsync(Manifest PatchManifest)
        {
            if (ApplyPatchBW == null)
            {
                ApplyPatchBW = new BackgroundWorker();
                ApplyPatchBW.DoWork += ApplyPatchBW_DoWork;
                ApplyPatchBW.RunWorkerCompleted += ApplyPatchBW_RunWorkerCompleted;
                ApplyPatchBW.WorkerReportsProgress = true;
                ApplyPatchBW.WorkerSupportsCancellation = true;
                ApplyPatchBW.ProgressChanged += ApplyPatchBW_ProgressChanged;
            }
            if (ApplyPatchBW.IsBusy)
            {
                Log.Error("ApplyPatchBW IsBusy");
                throw new InvalidOperationException("ApplyPatchAsync is already busy");
            }
            ApplyPatchBW.RunWorkerAsync(PatchManifest);
        }

        public static void ApplyPatchCancel()
        {
            ApplyPatchBW.CancelAsync();
        }

        static void ApplyPatchBW_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Log.Debug(String.Format("ProgressChanged: {0}", e.ProgressPercentage));
            if (ApplyPatchProgressChanged != null)
                ApplyPatchProgressChanged(sender, e);
        }
        public static event EventHandler ApplyPatchCompleted;
        public static event ProgressChangedEventHandler ApplyPatchProgressChanged;

        private static void ApplyPatchBW_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                if (ApplyPatchCompleted != null)
                    ApplyPatchCompleted(sender, new EventArgs());
            }
            else
                throw e.Error;
        }

        private static void ApplyPatchBW_DoWork(object sender, DoWorkEventArgs e)
        {
            ApplyPatch((Manifest)e.Argument);
            if (ApplyPatchBW.CancellationPending)
                e.Cancel = true;
        }


        public static void ApplicationStart(string SelfUpdateURL)
        {
            if (AcquireLock())
            {
                Manifest SelfUpdateManifest = SelfUpdateNeeded(SelfUpdateURL);
                if (SelfUpdateManifest.NeedsUpdate)
                {
                    ApplySelfUpdate(SelfUpdateManifest);
                }
            }
        }
        private static bool AcquireLock()
        {
            PatchManager.PatchMutex = new Mutex(true, "{D1EF437C-5F7E-4B78-A71A-10489A280E61}");
            bool LaunchOK = false;

            try
            {
                LaunchOK = PatchManager.PatchMutex.WaitOne(TimeSpan.FromSeconds(2), true);
            }
            catch (AbandonedMutexException amex)
            {
                LaunchOK = true;
                Log.Debug("Received an AbandonedMutexException while WaitOne()", amex);
            }

            if (!LaunchOK)
            {
                Log.Warn("Unable to secure the Mutex, abnormal termination");
                Environment.Exit(1);
            }
            return LaunchOK;
        }
        private static Manifest SelfUpdateNeeded(string SelfUpdateURL)
        {
            string ExePath = Assembly.GetEntryAssembly().Location;
            string BootstrapPath = Path.Combine(Path.GetDirectoryName(ExePath), String.Format("_{0}", Path.GetFileName(ExePath)));
            if (File.Exists(BootstrapPath))
                File.Delete(BootstrapPath);
            Manifest SelfUpdateManifest = PatchManager.UpdateCheck(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), SelfUpdateURL);
            Log.Debug(String.Format("SelfUpdateNeeded: {0:True;0;False}", SelfUpdateManifest.NeedsUpdate));
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached && SelfUpdateManifest.NeedsUpdate)
            {
                Log.Warn("Self-update needed, but wont execute as a debugger is attached to this DEBUG version");
                SelfUpdateManifest.NeedsUpdate = false;
            }
#endif
            return SelfUpdateManifest;
        }
        private static void ApplySelfUpdate(Manifest SelfUpdateManifest)
        {
            string ExePath = string.Empty;
            string BootstrapPath = string.Empty;
            if (PatchManager.DetermineBootstrapMode())
            {
                BootstrapPath = Assembly.GetEntryAssembly().Location;
                ExePath = Path.Combine(Path.GetDirectoryName(BootstrapPath), Path.GetFileName(BootstrapPath).Substring(1));

                Log.Info("BOOTSTRAP: Applying update & launching new app...");
                PatchManager.ApplyPatch(SelfUpdateManifest);
                System.Diagnostics.Process.Start(ExePath);
                Environment.Exit(3);
            }
            else
            {
                ExePath = Assembly.GetEntryAssembly().Location;
                BootstrapPath = Path.Combine(Path.GetDirectoryName(ExePath), String.Format("_{0}", Path.GetFileName(ExePath)));
                Log.Info(String.Format("Copying self ({0}) to BootstrapPath: {1}", ExePath, BootstrapPath));
                File.Copy(ExePath, BootstrapPath);
                System.Diagnostics.Process.Start(BootstrapPath);
                Environment.Exit(2);
            }
        }
        private static bool DetermineBootstrapMode()
        {
            if (Path.GetFileName(Assembly.GetEntryAssembly().Location).StartsWith("_"))
                PatchManager.BootstrapMode = true;
            Log.Info(String.Format("BootstrapMode: {0:TRUE;0;FALSE}", PatchManager.BootstrapMode));
            return PatchManager.BootstrapMode;
        }

        /// <summary>
        /// Scan a folder and compare against a manifest to determine if an update is needed
        /// </summary>
        /// <param name="LocalFolder"></param>
        /// <param name="ManifestURL"></param>
        /// <returns></returns>
        public static Manifest UpdateCheck(string LocalFolder, string ManifestURL)
        {
            Log.Debug(String.Format("Entering UpdateCheck(), LocalFolder: {0}, ManifestURL: {1}", LocalFolder, ManifestURL));
            bool NeedsUpdate = false;
            bool CancelRequested = false;
            Manifest PatchManifest = Manifest.CreateFromXML(ManifestURL);

            Log.Debug("Iterating through each FileNode in PatchManifest.FileList...");
            foreach (FileNode PatchFile in PatchManifest.FileList)
                PatchFile.LocalFilePath = Path.Combine(LocalFolder, PatchFile.RemoteFileName);

            FSCache.Load();

            for (int i = 0; (i < PatchManifest.FileList.Count) && !NeedsUpdate && !CancelRequested; i++)
            {
                if (UpdateCheckBW != null)
                    if (UpdateCheckBW.CancellationPending)
                        CancelRequested = true;

                if (PatchManifest.FileList[i].CheckUpdate())
                {
                    Log.Info(String.Format("Found a FileNode that needs updating at {0}", PatchManifest.FileList[i].LocalFilePath));
                    NeedsUpdate = true;
                }
            }
            
            FSCache.GetInstance().Save();

            PatchManifest.NeedsUpdate = NeedsUpdate;
            Log.Debug(String.Format("PatchManifest.NeedsUpdate: {0:true;0;False}", PatchManifest.NeedsUpdate));
            if (CancelRequested)
                PatchManifest = null;
            return PatchManifest;
        }

        public static void UpdateCheckCancel()
        {
            UpdateCheckBW.CancelAsync();
        }

        /// <summary>
        /// Async version of UpdateCheck()
        /// </summary>
        /// <param name="LocalFolder"></param>
        /// <param name="ManifestURL"></param>
        public static void UpdateCheckAsync(string LocalFolder, string ManifestURL)
        {
            UpdateCheckArgs args = new UpdateCheckArgs();
            args.LocalFolder = LocalFolder;
            args.ManifestURL = ManifestURL;

            if (UpdateCheckBW == null)
            {
                UpdateCheckBW = new BackgroundWorker();
                UpdateCheckBW.WorkerSupportsCancellation = true;
                UpdateCheckBW.DoWork += UpdateCheckBW_DoWork;
                UpdateCheckBW.RunWorkerCompleted += UpdateCheckBW_RunWorkerCompleted;
            }
            if (UpdateCheckBW.IsBusy)
            {
                Log.Error("UpdateCheckBW Already Busy");
                throw new InvalidOperationException("UpdateCheckAsync is already busy");
            }
            UpdateCheckBW.RunWorkerAsync(args);
        }

        private static void UpdateCheckBW_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (UpdateCheckCompleted != null)
            {
                if (!e.Cancelled)
                {
                    if(e.Result is Manifest)
                        UpdateCheckCompleted(new UpdateCheckCompletedEventArgs((Manifest)e.Result));
                    if (e.Result is Exception)
                        UpdateCheckCompleted(new UpdateCheckCompletedEventArgs((Exception)e.Result));
                }
            }
        }

        private static void UpdateCheckBW_DoWork(object sender, DoWorkEventArgs e)
        {
            string LocalFolder = ((UpdateCheckArgs)e.Argument).LocalFolder;
            string ManifestURL = ((UpdateCheckArgs)e.Argument).ManifestURL;
            try
            {
                e.Result = UpdateCheck(LocalFolder, ManifestURL);
            }
            catch(Exception ex)
            {
                e.Result = ex;
            }
            if (UpdateCheckBW.CancellationPending)
                e.Cancel = true;
        }

        public static event UpdateCheckCompletedEventHandler UpdateCheckCompleted;


        /// <summary>
        /// Creates a patch (manifest) of a folder to a patch folder
        /// </summary>
        /// <param name="LocalFolder">Folder that serves as the image to patch</param>
        /// <param name="PatchFolder">Folder where patch files should be staged for uploading to a remote server</param>
        /// <param name="ChannelName">Name of the Channel/manifest</param>
        public static void CreatePatch(string LocalFolder, string PatchFolder, string ChannelName, string DistributionURL)
        {
            List<FileNode> LocalFiles = new List<FileNode>();
            FileNode NewFile;

            foreach(string FileName in Directory.EnumerateFiles(LocalFolder, "*", SearchOption.AllDirectories))
            {
                NewFile = new FileNode();
                NewFile.LocalFilePath = FileName;
                NewFile.RemoteFileName = FileName.Remove(0, LocalFolder.Length + 1);
                NewFile.RemoteMD5Hash = NewFile.LocalMD5Hash;
                NewFile.RemoteSize = NewFile.LocalSize;
                NewFile.RemoteURL = DistributionURL;        //changed to seed the distribution URL.  Staging function will build this out fully
                LocalFiles.Add(NewFile);
            }

            if (!Directory.Exists(PatchFolder))
                Directory.CreateDirectory(PatchFolder);

            foreach (FileNode StageFile in LocalFiles)
                StageFile.StageTo(PatchFolder);

            string ManifestFile = Path.Combine(PatchFolder, ChannelName);
            ManifestFile += ".xml";

            string ManifestBackupFolder = Path.Combine(PatchFolder, "ManifestBackup");
            if (!Directory.Exists(ManifestBackupFolder))
                Directory.CreateDirectory(ManifestBackupFolder);

            string ManifestBackupFile = String.Format("{0}-{1}.xml",Path.Combine(ManifestBackupFolder, ChannelName), DateTime.Now.ToString("yyyy-MM-dd_hh-mm-ss"));

            Manifest PatchManifest = new Manifest();
            PatchManifest.FileList = LocalFiles;
            PatchManifest.Save(ManifestFile);
            PatchManifest.Save(ManifestBackupFile);
        }
    }
}
