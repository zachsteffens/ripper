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

namespace dvdrip
{
    public class ShortTimeElpasedException : System.Exception
    {
        public ShortTimeElpasedException(string message,
            Exception innerException) : base(message, innerException)
        {
        }
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

            //try { Directory.Delete(pathToCreateFiles, true); }
            //catch (Exception e) { }

            try { DirectoryInfo di = Directory.CreateDirectory(pathToCreateFiles); }
            catch (Exception e) { }

            try
            {

                // Start the child process.
                Process p = new Process();
                // Redirect the output stream of the child process.
                p.StartInfo.UseShellExecute = false;

                p.StartInfo.RedirectStandardOutput = true;
                p.OutputDataReceived += new DataReceivedEventHandler(MyProcOutputHandler);
                p.StartInfo.FileName = System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\MakeMKV\\makemkvcon64.exe";
                p.StartInfo.Arguments = "mkv disc:0 " + itemToRip.selectedTrackIndex + " \"" + itemToRip.fullPath + "\"";
                p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                p.StartInfo.CreateNoWindow = true;
                Stopwatch ripStopwatch = new Stopwatch();
                ripStopwatch.Start();
                p.Start();
                // Do not wait for the child process to exit before
                // reading to the end of its redirected stream.
                // p.WaitForExit();
                // Read the output stream first and then wait.

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                ripStopwatch.Stop();
                //if it finished in less than 3 minutes throw exception
                if (ripStopwatch.ElapsedMilliseconds < 10000)
                {
                    throw new ShortTimeElpasedException(output, null);
                }
                //string rippedTitle = Directory.GetFiles(pathToCreateFiles)[0];
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
                    throw new FileIOException("failed to move: ripped file - " + rippedTitle + "      path - " + itemToRip.pathToRip, e.InnerException);
                }
            }
            catch (Exception e)
            {
                string ripFailOutput = System.IO.Path.Combine(itemToRip.fullPath, itemToRip.title) + "_ripFailure.txt";
                File.WriteAllText(ripFailOutput, e.Message);
                itemToRip.failedRip = true;
                itemToRip.failedRipTextFile = ripFailOutput;
            }

            return "";
        }

        private String CompressWithHandbrake(QueuedItem item)
        {
            string output = "";
            try
            {
                // Start the child process.
                Process p = new Process();
                // Redirect the output stream of the child process.
                p.StartInfo.UseShellExecute = false;

                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.FileName = System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Handbrake\\HandBrakeCLI.exe";
                p.StartInfo.Arguments = "-e x264  -q 20.0 -a 1 -E ffaac,copy:ac3 -B 160 -6 dpl2 -R Auto -D 0.0 --audio-copy-mask aac,ac3,dtshd,dts,mp3 --audio-fallback ffac3 -f av_mkv --auto-anamorphic --denoise medium -m --x264-preset veryslow --x264-tune film --h264-profile high --h264-level 3.1 --subtitle scan --subtitle-forced --subtitle-burned -i \"" + item.pathToRip + "\" -o \"" + item.pathToCompression + "\"";
                p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                p.StartInfo.CreateNoWindow = true;
                Stopwatch compressStopwatch = new Stopwatch();
                compressStopwatch.Start();
                p.Start();
                // Do not wait for the child process to exit before
                // reading to the end of its redirected stream.
                // p.WaitForExit();
                // Read the output stream first and then wait.
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                compressStopwatch.Stop();

                if (compressStopwatch.ElapsedMilliseconds < 180000)
                {
                    throw new ShortTimeElpasedException(output, null);
                }
            }
            catch (Exception e)
            {
                string compressFailOutput = System.IO.Path.Combine(item.fullPath, item.title) + "_compressionFailure.txt";
                File.WriteAllText(compressFailOutput, output);
                item.failedCompressTextFile = compressFailOutput;
                item.failedCompression = true;
            }

            return output;
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
            while (isCopyThreadRunning)
            {
                if (!currentlyCopying && waitingToCopy.Count > 0)
                {
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
                        File.Copy(thisItem.pathToCompression, copyToPath.ToString(), true);
                        //File.Delete(thisItem.pathToRip);
                        //File.Delete(thisItem.pathToCompression);
#endif
                        thisItem.copying = false;
                        thisItem.copied = true;
                        //thisItem.removed = true;
                    }
                    catch (Exception)
                    {
                        thisItem.failedCopy = true;
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
    }
}
