﻿/*
 * Copyright (c) Gustave Monce and Contributors
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
using ManagedWimLib;
using Microsoft.Wim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace UnifiedUpdatePlatform.Imaging.NET
{
    public class WimLibImaging : IImaging
    {
        private static string GetExecutableDirectory()
        {
            string fileName = Environment.ProcessPath;
            return fileName.Contains(Path.DirectorySeparatorChar) ? string.Join(Path.DirectorySeparatorChar, fileName.Split(Path.DirectorySeparatorChar).Reverse().Skip(1).Reverse()) : "";
        }

        private static void InitNativeLibrary()
        {
            string executableDirectory = GetExecutableDirectory();
            string libraryDirectory = Path.Combine(executableDirectory, "runtimes");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                libraryDirectory = Path.Combine(libraryDirectory, "win-");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                libraryDirectory = Path.Combine(libraryDirectory, "linux-");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                libraryDirectory = Path.Combine(libraryDirectory, "osx-");
            }

            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    libraryDirectory += "x86";
                    break;
                case Architecture.X64:
                    libraryDirectory += "x64";
                    break;
                case Architecture.Arm:
                    libraryDirectory += "arm";
                    break;
                case Architecture.Arm64:
                    libraryDirectory += "arm64";
                    break;
            }
            libraryDirectory = Path.Combine(libraryDirectory, "native");

            string libPath = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                libPath = Path.Combine(libraryDirectory, "libwim-15.dll");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                libPath = Path.Combine(libraryDirectory, "libwim.so");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                libPath = Path.Combine(libraryDirectory, "libwim.dylib");
            }

            if (libPath == null)
            {
                Console.WriteLine("Unable to find native library.");
                throw new PlatformNotSupportedException("Unable to find native library.");
            }

            if (!File.Exists(libPath))
            {
                libraryDirectory = executableDirectory;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    libPath = Path.Combine(libraryDirectory, "libwim-15.dll");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    libPath = Path.Combine(libraryDirectory, "libwim.so");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    libPath = Path.Combine(libraryDirectory, "libwim.dylib");
                }

                if (!File.Exists(libPath))
                {
                    Console.WriteLine($"Unable to find native library [{libPath}].");
                    throw new PlatformNotSupportedException($"Unable to find native library [{libPath}].");
                }
            }

            try
            {
                Wim.GlobalInit(libPath);
            }
            catch (InvalidOperationException)
            {
                // Lib already initialized
            }
        }

        static WimLibImaging()
        {
            InitNativeLibrary();
        }

        private static CompressionType GetCompressionTypeFromWimCompressionType(WimCompressionType compressionType)
        {
            switch (compressionType)
            {
                case WimCompressionType.Lzms:
                    {
                        return CompressionType.LZMS;
                    }
                case WimCompressionType.Lzx:
                    {
                        return CompressionType.LZX;
                    }
                case WimCompressionType.Xpress:
                    {
                        return CompressionType.XPRESS;
                    }
                default:
                case WimCompressionType.None:
                    {
                        return CompressionType.None;
                    }
            }
        }

        public bool AddFileToImage(string wimFile, int imageIndex, string fileToAdd, string destination, IImaging.ProgressCallback progressCallback = null)
        {
            destination = destination.Replace(Path.DirectorySeparatorChar, '\\');

            string title = $"Adding {destination} to {wimFile.Split(Path.DirectorySeparatorChar).Last()}...";
            try
            {
                CallbackStatus ProgressCallback(ProgressMsg msg, object info, object progctx)
                {
                    switch (msg)
                    {
                        case ProgressMsg.ScanBegin:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.ScanEnd:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.WriteMetadataBegin:
                            break;

                        case ProgressMsg.UpdateBeginCommand:
                            break;

                        case ProgressMsg.UpdateEndCommand:
                            break;

                        case ProgressMsg.WriteStreams:
                            {
                                WriteStreamsProgress m = (WriteStreamsProgress)info;
                                progressCallback?.Invoke(title, m.TotalBytes > 0 ? (int)Math.Round((double)m.CompletedBytes / m.TotalBytes * 100) : 0, false);
                            }
                            break;

                        case ProgressMsg.WriteMetadataEnd:
                            break;
                    }
                    return CallbackStatus.Continue;
                }

                bool originChunked = wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase);
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.WriteAccess);
                wim.RegisterCallback(ProgressCallback);
                wim.UpdateImage(
                    imageIndex,
                    UpdateCommand.SetAdd(fileToAdd, destination, null, AddFlags.None),
                    UpdateFlags.SendProgress);
                wim.Overwrite(originChunked ? WriteFlags.Solid : WriteFlags.None, Wim.DefaultThreads);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return ReformatWindowsImageFileXML(wimFile);
        }

        public bool DeleteFileFromImage(string wimFile, int imageIndex, string fileToRemove, IImaging.ProgressCallback progressCallback = null)
        {
            fileToRemove = fileToRemove.Replace(Path.DirectorySeparatorChar, '\\');

            string title = $"Removing {fileToRemove} from {wimFile.Split(Path.DirectorySeparatorChar).Last()}...";
            try
            {
                CallbackStatus ProgressCallback(ProgressMsg msg, object info, object progctx)
                {
                    switch (msg)
                    {
                        case ProgressMsg.ScanBegin:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.ScanEnd:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.WriteMetadataBegin:
                            break;

                        case ProgressMsg.UpdateBeginCommand:
                            break;

                        case ProgressMsg.UpdateEndCommand:
                            break;

                        case ProgressMsg.WriteStreams:
                            {
                                WriteStreamsProgress m = (WriteStreamsProgress)info;
                                progressCallback?.Invoke(title, m.TotalBytes > 0 ? (int)Math.Round((double)m.CompletedBytes / m.TotalBytes * 100) : 0, false);
                            }
                            break;

                        case ProgressMsg.WriteMetadataEnd:
                            break;
                    }
                    return CallbackStatus.Continue;
                }

                bool originChunked = wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase);
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.None);
                wim.RegisterCallback(ProgressCallback);
                wim.UpdateImage(
                    imageIndex,
                    UpdateCommand.SetDelete(fileToRemove, DeleteFlags.None),
                    UpdateFlags.SendProgress);
                wim.Overwrite(originChunked ? WriteFlags.Solid : WriteFlags.None, Wim.DefaultThreads);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return ReformatWindowsImageFileXML(wimFile);
        }

        public bool ExportImage(string wimFile, string destinationWimFile, int imageIndex, IEnumerable<string> referenceWIMs = null, WimCompressionType compressionType = WimCompressionType.Lzx, IImaging.ProgressCallback progressCallback = null)
        {
            string title = $"Exporting {wimFile.Split(Path.DirectorySeparatorChar).Last()} - Index {imageIndex}";
            try
            {
                CallbackStatus ProgressCallback(ProgressMsg msg, object info, object progctx)
                {
                    switch (msg)
                    {
                        case ProgressMsg.WriteStreams:
                            {
                                WriteStreamsProgress m = (WriteStreamsProgress)info;
                                progressCallback?.Invoke(title, m.TotalBytes > 0 ? (int)Math.Round((double)m.CompletedBytes / m.TotalBytes * 100) : 0, false);
                            }
                            break;

                        case ProgressMsg.WriteMetadataBegin:
                            break;

                        case ProgressMsg.WriteMetadataEnd:
                            break;
                    }
                    return CallbackStatus.Continue;
                }

                using Wim srcWim = Wim.OpenWim(wimFile, OpenFlags.None);
                string imageName = srcWim.GetImageName(imageIndex);
                string imageDescription = srcWim.GetImageDescription(imageIndex);

                if (referenceWIMs?.Any() == true)
                {
                    srcWim.ReferenceResourceFiles(referenceWIMs, RefFlags.None, OpenFlags.None);
                }

                using Wim destWim = File.Exists(destinationWimFile) ? Wim.OpenWim(destinationWimFile, OpenFlags.WriteAccess) : Wim.CreateNewWim(GetCompressionTypeFromWimCompressionType(compressionType));

                destWim.RegisterCallback(ProgressCallback);

                if (destWim.IsImageNameInUse(imageName))
                {
                    imageName = imageName + " " + DateTime.UtcNow.ToString();
                }

                srcWim.ExportImage(imageIndex, destWim, imageName, imageDescription, ExportFlags.None);

                if (File.Exists(destinationWimFile))
                {
                    destWim.Overwrite(GetCompressionTypeFromWimCompressionType(compressionType) == CompressionType.LZMS ? WriteFlags.Solid : WriteFlags.None, Wim.DefaultThreads);
                }
                else
                {
                    destWim.Write(destinationWimFile, Wim.AllImages, GetCompressionTypeFromWimCompressionType(compressionType) == CompressionType.LZMS ? WriteFlags.Solid : WriteFlags.None, Wim.DefaultThreads);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return ReformatWindowsImageFileXML(destinationWimFile);
        }

        public bool ExtractFileFromImage(string wimFile, int imageIndex, string fileToExtract, string destination)
        {
            string filename = Path.GetFileName(fileToExtract.Replace('\\', Path.DirectorySeparatorChar));
            string extractDir = Path.GetTempPath();
            fileToExtract = fileToExtract.Replace(Path.DirectorySeparatorChar, '\\');

            try
            {
                using (Wim wim = Wim.OpenWim(wimFile, OpenFlags.None))
                {
                    wim.ExtractPath(imageIndex, extractDir, fileToExtract, ExtractFlags.NoPreserveDirStructure);
                }
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Move(Path.Combine(extractDir, filename), destination);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        public bool RenameFileInImage(string wimFile, int imageIndex, string sourceFilePath, string destinationFilePath, IImaging.ProgressCallback progressCallback = null)
        {
            sourceFilePath = sourceFilePath.Replace(Path.DirectorySeparatorChar, '\\');
            destinationFilePath = destinationFilePath.Replace(Path.DirectorySeparatorChar, '\\');

            string title = $"Renaming {sourceFilePath} to {destinationFilePath} in {wimFile.Split(Path.DirectorySeparatorChar).Last()}...";
            try
            {
                CallbackStatus ProgressCallback(ProgressMsg msg, object info, object progctx)
                {
                    switch (msg)
                    {
                        case ProgressMsg.ScanBegin:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.ScanEnd:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.WriteMetadataBegin:
                            break;

                        case ProgressMsg.UpdateBeginCommand:
                            break;

                        case ProgressMsg.UpdateEndCommand:
                            break;

                        case ProgressMsg.WriteStreams:
                            {
                                WriteStreamsProgress m = (WriteStreamsProgress)info;
                                progressCallback?.Invoke(title, m.TotalBytes > 0 ? (int)Math.Round((double)m.CompletedBytes / m.TotalBytes * 100) : 0, false);
                            }
                            break;

                        case ProgressMsg.WriteMetadataEnd:
                            break;
                    }
                    return CallbackStatus.Continue;
                }

                bool originChunked = wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase);
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.None);
                wim.RegisterCallback(ProgressCallback);
                wim.UpdateImage(
                    imageIndex,
                    UpdateCommand.SetRename(sourceFilePath, destinationFilePath),
                    UpdateFlags.SendProgress);
                wim.Overwrite(originChunked ? WriteFlags.Solid : WriteFlags.None, Wim.DefaultThreads);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return ReformatWindowsImageFileXML(wimFile);
        }

        public bool ApplyImage(string wimFile, int imageIndex, string OutputDirectory, IEnumerable<string> referenceWIMs = null, bool PreserveACL = true, IImaging.ProgressCallback progressCallback = null)
        {
            string title = $"Applying {wimFile.Split(Path.DirectorySeparatorChar).Last()} - Index {imageIndex}";
            try
            {
                CallbackStatus ProgressCallback(ProgressMsg msg, object info, object progctx)
                {
                    switch (msg)
                    {
                        case ProgressMsg.ExtractImageBegin:
                            {
                                ExtractProgress m = (ExtractProgress)info;
                            }
                            break;

                        case ProgressMsg.ExtractImageEnd:
                            {
                                ExtractProgress m = (ExtractProgress)info;
                            }
                            break;

                        case ProgressMsg.ExtractFileStructure:
                            {
                                ExtractProgress m = (ExtractProgress)info;
                                int i = m.EndFileCount > 0 ? (int)Math.Round((double)m.CurrentFileCount / m.EndFileCount * 100) : 0;
                                progressCallback?.Invoke($"Applying file structure ({i}%)", 0, true);
                            }
                            break;

                        case ProgressMsg.ExtractStreams:
                            {
                                ExtractProgress m = (ExtractProgress)info;
                                progressCallback?.Invoke(title, (int)Math.Round((double)m.CompletedBytes / m.TotalBytes * 100), false);
                            }
                            break;

                        case ProgressMsg.ExtractMetadata:
                            {
                                ExtractProgress m = (ExtractProgress)info;
                                int i = m.TotalBytes > 0 ? (int)Math.Round((double)m.CompletedBytes / m.TotalBytes * 100) : 0;
                                progressCallback?.Invoke($"Applying metadata ({i}%)", 0, true);
                            }
                            break;
                    }
                    return CallbackStatus.Continue;
                }

                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.None);
                wim.RegisterCallback(ProgressCallback);
                if (referenceWIMs?.Any() == true)
                {
                    wim.ReferenceResourceFiles(referenceWIMs, RefFlags.None, OpenFlags.None);
                }

                wim.ExtractImage(imageIndex, OutputDirectory, PreserveACL ? ExtractFlags.StrictAcls : ExtractFlags.NoAcls);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        public bool CaptureImage(
            string wimFile,
            string imageName,
            string imageDescription,
            string imageFlag,
            string InputDirectory,
            TempManager.TempManager tempManager,
            string imageDisplayName = null,
            string imageDisplayDescription = null,
            WimCompressionType compressionType = WimCompressionType.Lzx,
            IImaging.ProgressCallback progressCallback = null,
            int UpdateFrom = -1,
            bool PreserveACL = true)
        {
            string title = $"Creating {imageName} ({wimFile.Split(Path.DirectorySeparatorChar).Last()})";
            try
            {
                CallbackStatus ProgressCallback(ProgressMsg msg, object info, object progctx)
                {
                    switch (msg)
                    {
                        case ProgressMsg.ScanBegin:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, {m.NumDirsScanned} Directories, {m.NumNonDirsScanned} Files, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.ScanDEntry:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, {m.NumDirsScanned} Directories, {m.NumNonDirsScanned} Files, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.ScanEnd:
                            {
                                ScanProgress m = (ScanProgress)info;
                                progressCallback?.Invoke($"Scanning files ({m.NumBytesScanned} bytes scanned, {m.NumDirsScanned} Directories, {m.NumNonDirsScanned} Files, Current directory: {m.CurPath})", 0, true);
                            }
                            break;

                        case ProgressMsg.WriteMetadataBegin:
                            break;

                        case ProgressMsg.WriteStreams:
                            {
                                WriteStreamsProgress m = (WriteStreamsProgress)info;
                                progressCallback?.Invoke(title, (int)Math.Round((double)m.CompletedBytes / m.TotalBytes * 100), false);
                            }
                            break;

                        case ProgressMsg.WriteMetadataEnd:
                            break;
                    }
                    return CallbackStatus.Continue;
                }

                if (File.Exists(wimFile))
                {
                    using Wim wim = Wim.OpenWim(wimFile, OpenFlags.WriteAccess);
                    wim.RegisterCallback(ProgressCallback);
                    wim.AddImage(InputDirectory, imageName, null, PreserveACL ? AddFlags.StrictAcls : AddFlags.NoAcls);
                    if (!string.IsNullOrEmpty(imageDescription))
                    {
                        wim.SetImageDescription((int)wim.GetWimInfo().ImageCount, imageDescription);
                    }

                    if (!string.IsNullOrEmpty(imageDisplayName))
                    {
                        wim.SetImageProperty((int)wim.GetWimInfo().ImageCount, "DISPLAYNAME", imageDisplayName);
                    }

                    if (!string.IsNullOrEmpty(imageDisplayDescription))
                    {
                        wim.SetImageProperty((int)wim.GetWimInfo().ImageCount, "DISPLAYDESCRIPTION", imageDisplayDescription);
                    }

                    if (!string.IsNullOrEmpty(imageFlag))
                    {
                        wim.SetImageFlags((int)wim.GetWimInfo().ImageCount, imageFlag);
                    }

                    if (UpdateFrom != -1)
                    {
                        wim.ReferenceTemplateImage((int)wim.GetWimInfo().ImageCount, UpdateFrom);
                    }

                    wim.Overwrite(GetCompressionTypeFromWimCompressionType(compressionType) == CompressionType.LZMS ? WriteFlags.Solid : WriteFlags.None, Wim.DefaultThreads);
                }
                else
                {
                    using Wim wim = Wim.CreateNewWim(GetCompressionTypeFromWimCompressionType(compressionType));
                    wim.RegisterCallback(ProgressCallback);

                    const string config = @"[ExclusionList]
\$ntfs.log
\hiberfil.sys
\pagefile.sys
\swapfile.sys
\System Volume Information";

                    string configpath = tempManager.GetTempPath();
                    File.WriteAllText(configpath, config);

                    wim.AddImage(InputDirectory, imageName, configpath, PreserveACL ? AddFlags.StrictAcls : AddFlags.NoAcls);
                    if (!string.IsNullOrEmpty(imageDescription))
                    {
                        wim.SetImageDescription((int)wim.GetWimInfo().ImageCount, imageDescription);
                    }

                    if (!string.IsNullOrEmpty(imageDisplayName))
                    {
                        wim.SetImageProperty((int)wim.GetWimInfo().ImageCount, "DISPLAYNAME", imageDisplayName);
                    }

                    if (!string.IsNullOrEmpty(imageDisplayDescription))
                    {
                        wim.SetImageProperty((int)wim.GetWimInfo().ImageCount, "DISPLAYDESCRIPTION", imageDisplayDescription);
                    }

                    if (!string.IsNullOrEmpty(imageFlag))
                    {
                        wim.SetImageFlags((int)wim.GetWimInfo().ImageCount, imageFlag);
                    }

                    wim.Write(wimFile, Wim.AllImages, WriteFlags.None, Wim.DefaultThreads);
                    File.Delete(configpath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return ReformatWindowsImageFileXML(wimFile);
        }

        public bool EnumerateFiles(string wimFile, int imageIndex, string path, out string[] entries)
        {
            List<string> fsentries = new();
            try
            {
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.None);
                int IterateDirTreeCallback(DirEntry dentry, object userData)
                {
                    fsentries.Add(dentry.FileName);
                    return 0;
                }
                _ = wim.IterateDirTree(imageIndex, path, IterateDirTreeFlags.Children, IterateDirTreeCallback);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                entries = fsentries.ToArray();
                return false;
            }
            entries = fsentries.ToArray();
            return true;
        }

        public bool MarkImageAsBootable(string wimFile, int imageIndex)
        {
            try
            {
                bool originChunked = wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase);
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.None);
                wim.SetWimInfo(new ManagedWimLib.WimInfo() { BootIndex = (uint)imageIndex }, ChangeFlags.BootIndex);
                wim.Overwrite(originChunked ? WriteFlags.Solid : WriteFlags.None, Wim.DefaultThreads);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        public bool GetWIMInformation(
            string wimFile,
            out WIMInformationXML.WIM wimInformationObject)
        {
            wimInformationObject = null;
            try
            {
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.None);
                string xmlString = string.Join("", wim.GetXmlData().Skip(1));
                wimInformationObject = WIMInformationXML.DeserializeWIM(xmlString);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        public bool GetWIMImageInformation(
            string wimFile,
            int imageIndex,
            out WIMInformationXML.IMAGE image)
        {
            image = null;
            try
            {
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.None);
                string xmlString = string.Join("", wim.GetXmlData().Skip(1));
                WIMInformationXML.WIM xml = WIMInformationXML.DeserializeWIM(xmlString);
                image = xml.IMAGE.First(x => x.INDEX == imageIndex.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        public bool SetWIMImageInformation(
            string wimFile,
            int imageIndex,
            WIMInformationXML.IMAGE image)
        {
            image = null;
            try
            {
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.WriteAccess);
                string xmlString = string.Join("", wim.GetXmlData().Skip(1));
                WIMInformationXML.WIM wimInformationObject = WIMInformationXML.DeserializeWIM(xmlString);
                int index = wimInformationObject.IMAGE.IndexOf(wimInformationObject.IMAGE.First(x => x.INDEX == imageIndex.ToString()));
                wimInformationObject.IMAGE[index] = image;
                xmlString = WIMInformationXML.SerializeWIM(wimInformationObject);
                // TODO
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        private static bool ReformatWindowsImageFileXML(string wimFile)
        {
            try
            {
                using WimHandle wimHandle = WimgApi.CreateFile(
                    wimFile,
                    WimFileAccess.Write,
                    WimCreationDisposition.OpenExisting,
                    WimCreateFileOptions.Chunked,
                    WimCompressionType.None);
                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                string xmlString = WimgApi.GetImageInformationAsString(wimHandle);
                WIMInformationXML.WIM wimInformationObject = WIMInformationXML.DeserializeWIM(xmlString);
                xmlString = WIMInformationXML.SerializeWIM(wimInformationObject);
                WimgApi.SetImageInformation(wimHandle, xmlString);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return ReformatWindowsImageFileXMLUsingWimgApi(wimFile);
            }
            return true;
        }

        private static bool ReformatWindowsImageFileXMLUsingWimgApi(string wimFile)
        {
            try
            {
                using Wim wim = Wim.OpenWim(wimFile, OpenFlags.WriteAccess);
                string xmlString = string.Join("", wim.GetXmlData().Skip(1));
                WIMInformationXML.WIM wimInformationObject = WIMInformationXML.DeserializeWIM(xmlString);
                xmlString = WIMInformationXML.SerializeWIM(wimInformationObject);
                // TODO
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }
    }
}