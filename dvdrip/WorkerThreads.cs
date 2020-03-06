using System;
using System.Collections.Generic;
using System.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Management;
using System.Net;
using Newtonsoft;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Runtime.Serialization;
using System.Threading;

namespace dvdrip
{
    public class ShortTimeElpasedException : System.Exception
    {
        public ShortTimeElpasedException(string message,
            Exception innerException) : base(message, innerException)
        {
        }
    }

    public class MakeMKVUpdateAvailableException : System.Exception
    {
        public MakeMKVUpdateAvailableException(string message, 
            Exception innerException) : base(message,innerException)
        { }
    }

    public class FileIOException : System.Exception
    {
        public FileIOException(string message,
            Exception innerException) : base(message, innerException)
        {
        }
    }
    public class FileNotFoundException : System.Exception
    {
        public FileNotFoundException(string message,
            Exception innerException) : base(message, innerException)
        {
        }
    }

    public partial class MainWindow : Window
    {
  
        

        private static void MyProcOutputHandler(object sendingProcess,
            DataReceivedEventArgs outLine)
        {
            // Collect the sort command output. 
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                Debug.WriteLine(outLine.Data);
                
            }
        }

        private String RipDiscToMkv(QueuedItem itemToRip)
        {

            ////remove the directory if it exists
            string pathToCreateFiles = System.IO.Path.GetFullPath(itemToRip.fullPath);
            StringBuilder ripMkvOutput;
            //try { Directory.Delete(pathToCreateFiles, true); }
            //catch (Exception e) { }

            try { DirectoryInfo di = Directory.CreateDirectory(pathToCreateFiles); }
            catch (Exception e) { }

            try
            {
                ripMkvOutput = new StringBuilder();

                using (Process process = new Process())
                {
                    process.StartInfo.FileName = System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\MakeMKV\\makemkvcon64.exe";
                    process.StartInfo.Arguments = "mkv disc:0 " + itemToRip.selectedTrackIndex + " \"" + itemToRip.fullPath + "\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.StartInfo.CreateNoWindow = true;

                    process.OutputDataReceived += new DataReceivedEventHandler

                    
                    (
                        delegate (object sender, DataReceivedEventArgs e)
                        {
                            // append the new data to the data already read-in
                            ripMkvOutput.Append(e.Data);
                            Debug.WriteLine(e.Data);
                        }
                    );
                    process.Start();
                    process.BeginOutputReadLine();
                    process.WaitForExit();
                    process.CancelOutputRead();
                    string result = ripMkvOutput.ToString();
                    if(result.Contains("0 titles saved"))
                    {
                        notifier.Notify(mqttNotifyState.rip, "Rip Failed: " + itemToRip.title);
                        throw new Exception("failed rip", new Exception(result));
                    }

                }
                
                string rippedTitle = itemToRip.fullPath + "\\" + itemToRip.rippedMKVTitle;
                //renames the ripped mkv track to the name specified in the previous step
                itemToRip.pathToRip = System.IO.Path.Combine(itemToRip.fullPath, itemToRip.title) + "_rip.mkv";
                itemToRip.pathToCompression = System.IO.Path.Combine(itemToRip.fullPath, itemToRip.title) + "_compressed.mkv";
                try
                { 
                    System.IO.File.Move(rippedTitle, itemToRip.pathToRip);
                    
                }
                catch (Exception e)
                {
                    notifier.Notify(mqttNotifyState.generic, "failed to move: ripped file - " + rippedTitle + "      path - " + itemToRip.pathToRip);
                    throw new FileIOException("failed to move: ripped file - " + rippedTitle + "      path - " + itemToRip.pathToRip, e.InnerException);
                    
                }
            }
            catch (Exception e)
            {
                
                if (e.InnerException != null)
                {
                   
                    StringBuilder errormessage = new StringBuilder(e.InnerException.ToString());
                    itemToRip.failedRipText = errormessage;
                    itemToRip.failedRip = true;
                }

                
            }

            return "";
        }

        private String CompressWithHandbrake(QueuedItem item)
        {
            
            try
            {
                int timeoutMiliseconds = 1000 * 60 * 60 * 12; // 12 hours
                using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
                using (AutoResetEvent errorWaitHandle = new AutoResetEvent(false))
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Handbrake\\HandBrakeCLI.exe";
                        process.StartInfo.Arguments = "-e x264  -q 20.0 -a 1 -E ffaac,copy:ac3 -B 160 -6 dpl2 -R Auto -D 0.0 --audio-copy-mask aac,ac3,dtshd,dts,mp3 --audio-fallback ffac3 -f av_mkv --auto-anamorphic --denoise medium -m --x264-preset veryslow --x264-tune film d high --h264-level 3.1 --subtitle scan --subtitle-forced --subtitle-burned -i \"" + item.pathToRip + "\" -o \"" + item.pathToCompression + "\"";
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        process.StartInfo.CreateNoWindow = true;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;

                        StringBuilder output = new StringBuilder();
                        StringBuilder error = new StringBuilder();

                   
                        process.OutputDataReceived += (sender, e) => {
                            if (e.Data == null)
                            {
                                outputWaitHandle.Set();
                            }
                            else
                            {
                                output.AppendLine(e.Data);
                            }
                        };
                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (e.Data == null)
                            {
                                errorWaitHandle.Set();
                            }
                            else
                            {
                                error.AppendLine(e.Data);
                            }
                        };

                        process.Start();

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        if (process.WaitForExit(timeoutMiliseconds) &&
                            outputWaitHandle.WaitOne(timeoutMiliseconds) &&
                            errorWaitHandle.WaitOne(timeoutMiliseconds))
                        {
                            if(process.ExitCode != 0)
                            {
                                output.AppendLine("-----------------------------error--------------------------");
                                output.Append(error.ToString());
                                item.failedCompressText = output;
                                item.failedCompression = true;
                            }
                            

                        }
                        else
                        {
                            // Timed out.
                            //shouldnt ever happen. timeout is set to 12 hours.
                        }
                    }
                }
            }
            catch (Exception e)
            {
                StringBuilder errormessage = new StringBuilder(e.InnerException.ToString());
                item.failedCompressText = errormessage;
                item.failedCompression = true;
            }
            
            return "";
        }

        private void ripDiscThread(object sender, DoWorkEventArgs e)
        {

            //check to see if ripping
            while (isRippingThreadRunning)
            {
                if (!currentlyRipping && waitingToRip.Count > 0)
                {
                    currentlyRipping = true;
                    QueuedItem itemToRip = waitingToRip[0];
                    waitingToRip.RemoveAt(0);

                    QueuedItem thisItem = (QueuedItem)queuedItems.Where(f => f.title == itemToRip.title).FirstOrDefault();
                    thisItem.ripping = true;
                    //refresn the view
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));
                    System.Diagnostics.Debug.WriteLine("ripping " + thisItem.title);
#if TESTINGNODISC
                    System.Threading.Thread.Sleep(22000);
#else
                    RipDiscToMkv(thisItem);
#endif
                    System.Diagnostics.Debug.WriteLine("rip of " + thisItem.title + " complete");
                    thisItem.ripping = false;
                    if (thisItem.failedRip != true)
                    {
                        thisItem.ripped = true;
                        waitingToCompress.Add(thisItem);
                    }

                    //refresn the view
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));
                    currentlyRipping = false;
                    if(waitingToRip.Count == 0)
                    {
                        notifier.Notify(mqttNotifyState.complete, "Disc Complete: " + thisItem.title);
                    }
                }
                else
                {
                    System.Threading.Thread.Sleep(10000);
                }
            }
        }

        private void compressionThread(object sender, DoWorkEventArgs e)
        {

            while (isCompressionThreadRunning)
            {
                if (!currentlyCompressing && waitingToCompress.Count > 0)
                {
                    currentlyCompressing = true;
                    QueuedItem itemToCompress = waitingToCompress[0];
                    waitingToCompress.RemoveAt(0);

                    QueuedItem thisItem = (QueuedItem)queuedItems.Where(f => f.title == itemToCompress.title).FirstOrDefault();
                    thisItem.compressing = true;
                    //refresn the view
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));
                    System.Diagnostics.Debug.WriteLine("compressing " + thisItem.title);
#if TESTINGNODISC
                    System.Threading.Thread.Sleep(22000);
#else
                    CompressWithHandbrake(thisItem);

                    //validate compression with nreco
                    try
                    {
                        var ffProbe = new NReco.VideoInfo.FFProbe();
                        var videoInfo = ffProbe.GetMediaInfo(thisItem.pathToCompression);
                        Console.WriteLine(videoInfo.FormatName);
                    }
                    catch(Exception ex)
                    {
                        StringBuilder errormessage = new StringBuilder(ex.InnerException.ToString());
                        thisItem.failedCompressText = errormessage;
                        thisItem.failedCompression = true;
                        notifier.Notify(mqttNotifyState.compress, "Compression Failed: " + errormessage.ToString());
                    }




#endif
                    System.Diagnostics.Debug.WriteLine("compression of " + thisItem.title + " complete");
                    thisItem.compressing = false;
                    if (thisItem.failedCompression != true)
                    {
                        thisItem.compressed = true;
                        waitingToCopy.Add(thisItem);
                    }

                    //refresn the view
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));

                    currentlyCompressing = false;
                }
                else
                {
                    System.Threading.Thread.Sleep(10000);
                }
            }
        }

        private async void copyfileThread(object sender, DoWorkEventArgs e)
        {
            try
            {
                while (isCopyThreadRunning)
                {
                    if (!currentlyCopying && waitingToCopy.Count > 0)
                    {

                        Debug.WriteLine("Waiting to copy: " + waitingToCopy.Count.ToString());
                        QueuedItem itemToCopy = waitingToCopy[0];
                        waitingToCopy.RemoveAt(0);

                        QueuedItem thisItem = (QueuedItem)queuedItems.Where(f => f.title == itemToCopy.title).FirstOrDefault();


                        thisItem.copying = true;
                        System.Diagnostics.Debug.WriteLine("Start copying " + thisItem.title);
                        await Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                        {
                            lvInProgress.Items.Refresh();
                            lvInProgress.UpdateLayout();
                        }));

                        System.Diagnostics.Debug.WriteLine("copying " + thisItem.title);
                        StringBuilder copyToPath = new StringBuilder();

                        if (thisItem.isTV)
                        {

                            copyToPath.Append(ConfigurationManager.AppSettings["pathToNetworkMedia"]);
                            copyToPath.Append("TV\\");
                            copyToPath.Append(thisItem.tvShowTitle);
                            copyToPath.Append("\\S");
                            copyToPath.Append(thisItem.tvSeason.ToString("D2"));

                            try { DirectoryInfo di = Directory.CreateDirectory(copyToPath.ToString()); }
                            catch (Exception ex) { }
                        }
                        else
                        {   //movie
                            copyToPath.Append(ConfigurationManager.AppSettings["pathToNetworkMedia"]);
                            copyToPath.Append("Movies\\");
                            copyToPath.Append(thisItem.title);

                            try { Directory.Delete(copyToPath.ToString(), true); }
                            catch (Exception ex) { }

                            try { DirectoryInfo di = Directory.CreateDirectory(copyToPath.ToString()); }
                            catch (Exception ex) { }
                        }

                        copyToPath.Append("\\");
                        copyToPath.Append(thisItem.title);
                        copyToPath.Append(".mkv");

                        try
                        {
#if TESTINGNODISC
                        System.Threading.Thread.Sleep(20000);
#else
                            //generate md5 checksum of original
                            string originalMD5 = getMD5Hash(thisItem.pathToCompression);

                            File.Copy(thisItem.pathToCompression, copyToPath.ToString(), true);

                            string copiedMD5 = getMD5Hash(copyToPath.ToString());

                            if (originalMD5 != copiedMD5)
                            {
                                throw new Exception("hashes dont match");
                            }
                            File.Delete(thisItem.pathToRip);
                            File.Delete(thisItem.pathToCompression);
                            Directory.Delete(thisItem.fullPath);

#endif
                            thisItem.copying = false;
                            thisItem.copied = true;
                            thisItem.removed = true;
                        }
                        catch (Exception deleteFailed)
                        {
                            thisItem.failedCopy = true;
                            Debug.WriteLine("failed to delete!");
                            Debug.Write(deleteFailed.InnerException);
                            notifier.Notify(mqttNotifyState.copy, "Copy/Delete Failed: " + deleteFailed.InnerException);

                        }

                        System.Diagnostics.Debug.WriteLine("copy of " + thisItem.title + " complete");

                        await Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                        {
                            lvInProgress.Items.Refresh();
                            lvInProgress.UpdateLayout();
                        }));
                        currentlyCopying = false;

                    }
                }

            }
            catch(Exception genericCopyEx)
            {
                Debug.WriteLine("copy failed");
                Debug.Write(genericCopyEx.InnerException);
                notifier.Notify(mqttNotifyState.copy, "Copy/Delete Failed: " + genericCopyEx.InnerException);
            }
        }

        private string getMD5Hash(string filePath)
        {
            //generate md5 checksum of original
            string hash = filePath;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = md5.ComputeHash(stream);
                    hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
            return hash;
        }
    }
}
