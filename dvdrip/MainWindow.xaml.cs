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

namespace dvdrip
{
    

    public partial class MainWindow : Window
    {
        #region Local Variables
        public IntPtr windowHandle;
        public Boolean discIsBlueRay;
        //dependency property declaration
        public Boolean discIsTv
        {
            get { return (Boolean)GetValue(discIsTvProperty); }
            set { SetValue(discIsTvProperty, value); }
        }

        // Using a DependencyProperty as the backing store for discIsTv.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty discIsTvProperty =
            DependencyProperty.Register("discIsTv", typeof(Boolean), typeof(MainWindow), new PropertyMetadata(false));
        //end dependency property declaration

        public string discName;
        const string tmdbApiKey = "1abe04137ccd4fa521cb5f8e337b9418";
        const string tmdbImageApiUrl = "http://image.tmdb.org/t/p/w185"; // /nuUKcfRYjifwjIJPN1J6kIGcSvD.jpg"
        public ObservableCollection<tmdbResult> matchingTitles;
        public tmdbResult selectedTitle;
        public tmdbMovieDetails selectedMovieDetails;
        public track selectedTrack;
        public Disc currentDisc;
        public string fullPathToRippedMkv;
        public string fullPathToCompressedMkv;
        //timers
        private TimeSpan timeDiscSelect;
        private DispatcherTimer timerDiscSelect;
        private TimeSpan timeTrackSelect;
        private DispatcherTimer timerTrackSelect;

        private ObservableCollection<QueuedItem> queuedItems;
        private static List<QueuedItem> waitingToRip;
        private static List<QueuedItem> waitingToCopy;
        private static List<QueuedItem> waitingToCompress;
        private static Boolean currentlyRipping;
        private static Boolean currentlyCompressing;
        private static Boolean currentlyCopying;

        private static bool isRippingThreadRunning;
        static BackgroundWorker rippingWorker;
        private static bool isCompressionThreadRunning;
        static BackgroundWorker compressionWorker;
        private static bool isCopyThreadRunning;
        static BackgroundWorker copyWorker;

        private object lockObj;

        #endregion


        [DllImport("winmm.dll", EntryPoint = "mciSendStringA")]
        public static extern void mciSendString(string lpstrCommand, string lpstrReturnString, long uReturnLength, long hwndCallback);


        #region Constructor

        public MainWindow()
        {
            InitializeComponent();

            StartOver();
            lockObj = new object();
            matchingTitles = new ObservableCollection<tmdbResult>();
            prgLoadingDisc.Visibility = Visibility.Hidden;
            queuedItems = new ObservableCollection<QueuedItem>();
            waitingToRip = new List<QueuedItem>();
            waitingToCopy = new List<QueuedItem>();
            waitingToCompress = new List<QueuedItem>();

            lvInProgress.ItemsSource = queuedItems;

            BindingOperations.EnableCollectionSynchronization(queuedItems, lockObj);

            rippingWorker = new BackgroundWorker();
            isRippingThreadRunning = true;
            rippingWorker.DoWork += ripDiscThread;
            rippingWorker.RunWorkerAsync();

            compressionWorker = new BackgroundWorker();
            isCompressionThreadRunning = true;
            compressionWorker.DoWork += compressionThread;
            compressionWorker.RunWorkerAsync();

            copyWorker = new BackgroundWorker();
            isCopyThreadRunning = true;
            copyWorker.DoWork += copyfileThread;
            copyWorker.RunWorkerAsync();

        }
        #endregion

        #region Asyncronous Method to monitor and rip

        private String RipDiscToMkv(QueuedItem itemToRip)
        {

            ////remove the directory if it exists
            string pathToCreateFiles = System.IO.Path.GetFullPath(itemToRip.fullPath);

            try { Directory.Delete(pathToCreateFiles, true); }
            catch (Exception e) { }

            try { DirectoryInfo di = Directory.CreateDirectory(pathToCreateFiles); }
            catch (Exception e) { }



            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;

            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\MakeMKV\\makemkvcon64.exe";
            p.StartInfo.Arguments = "mkv disc:1 " + itemToRip.selectedTrackIndex + " \"" + itemToRip.fullPath + "\"";
            p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            try
            {
                string rippedTitle = Directory.GetFiles(pathToCreateFiles)[0];
                int indexofslash = rippedTitle.LastIndexOf("\\");
                itemToRip.pathToRip = System.IO.Path.Combine(itemToRip.fullPath, itemToRip.title) + "_rip.mkv";
                itemToRip.pathToCompression = System.IO.Path.Combine(itemToRip.fullPath, itemToRip.title) + "_compressed.mkv";
                System.IO.File.Move(rippedTitle, itemToRip.pathToRip);
            }
            catch (Exception e) { }
            //return output;


            return "";
        }

        private String CompressWithHandbrake(QueuedItem item)
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
            p.Start();
            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            return output;



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
                    await Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));
                    System.Diagnostics.Debug.WriteLine("copying " + thisItem.title);

                    StringBuilder copyToPath = new StringBuilder();
                    copyToPath.Append(ConfigurationManager.AppSettings["pathToNetworkMedia"]);
                    copyToPath.Append("Movies\\");
                    copyToPath.Append(thisItem.title);

                    try { Directory.Delete(copyToPath.ToString(), true); }
                    catch (Exception ex) { }

                    try { DirectoryInfo di = Directory.CreateDirectory(copyToPath.ToString()); }
                    catch (Exception ex) { }

                    copyToPath.Append("\\");
                    copyToPath.Append(thisItem.title);
                    copyToPath.Append(".mkv");

                    try
                    {
                        File.Copy(thisItem.pathToCompression, copyToPath.ToString(), true);
                        thisItem.copying = false;
                        thisItem.copied = true;
                        File.Delete(thisItem.pathToRip);
                        File.Delete(thisItem.pathToCompression);
                                               
                        thisItem.removed = true;
                    }
                    catch (Exception)
                    {

                        
                    }

                    

                    
                    //File.Copy(@"D:\From David\Media\Movies\2 Guns (2013)\2 Guns (2013).mp4", @"M:\Media\2 Guns (2013).mp4", true);

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

        private async void ripDiscThread(object sender, DoWorkEventArgs e)
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
                    await Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));
                    System.Diagnostics.Debug.WriteLine("ripping " + thisItem.title);
                    var rippingDisc = Task<string>.Factory.StartNew(() => RipDiscToMkv(thisItem));
                    //System.Threading.Thread.Sleep(15000);
                    await rippingDisc;
                    System.Diagnostics.Debug.WriteLine("rip of " + thisItem.title + " complete");

                    thisItem.ripping = false;
                    thisItem.ripped = true;
                    waitingToCompress.Add(thisItem);
                    //refresn the view
                    await Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));
                    currentlyRipping = false;

                    //eject the disc
                    //EjectMedia.Eject(@"\\.\" + ConfigurationManager.AppSettings["dvdRomDriveLetter"] + ": ");
                    mciSendString("set CDAudio door open", "", 0, 0);
                }
                else
                {
                    System.Threading.Thread.Sleep(10000);
                }
            }

        }

        private async void compressionThread(object sender, DoWorkEventArgs e)
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
                    await Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        lvInProgress.Items.Refresh();
                        lvInProgress.UpdateLayout();
                    }));
                    System.Diagnostics.Debug.WriteLine("compressing " + thisItem.title);

                    var compressingMkv = Task<string>.Factory.StartNew(() => CompressWithHandbrake(thisItem));
                    //System.Threading.Thread.Sleep(15000);

                    await compressingMkv;
                    System.Diagnostics.Debug.WriteLine("compression of " + thisItem.title + " complete");
                    thisItem.compressing = false;
                    thisItem.compressed = true;
                    thisItem.copied = true;
                    waitingToCopy.Add(thisItem);
                    //refresn the view
                    await Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
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
        
        #endregion



        #region High Level App Activities (Add/Remove Disc)

        private void btnCheckDriveMovie_Click(object sender, RoutedEventArgs e)
        {
            //QueuedItem testCopy = new QueuedItem("path", "toCopy", "1");
            //testCopy.compressed = true;
            //testCopy.ripped = true;
            //queuedItems.Add(testCopy);
            //waitingToCopy.Add(testCopy);
            discIsTv = false;

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom))
            {
                if (drive.IsReady)
                    DiscReady();
            }
        }
        
        private void DiscRemoved()
        {
            StartOver();
        }

        private void StartOver()
        {
            btnCheckDriveMovie.Visibility = Visibility.Visible;
            prgLoadingDisc.Visibility = Visibility.Collapsed;
            grdWaitingForDisc.Visibility = Visibility.Visible;
            grdSearchingForTitle.Visibility = Visibility.Collapsed;
            grdSelectTitlesForRip.Visibility = Visibility.Collapsed;

            discIsTv = false;
            discIsBlueRay = false;
            discName = "";
            selectedTitle = null;
            matchingTitles = new ObservableCollection<tmdbResult>();
            selectedMovieDetails = null;
            selectedTrack = null;
            currentDisc = null;
            fullPathToCompressedMkv = "";
            fullPathToCompressedMkv = "";
            if(timerDiscSelect != null)
            {
                timerDiscSelect.Stop();
            }
            if (timerTrackSelect != null)
            {
                timerTrackSelect.Stop();
            }
           

        }

        private async void DiscReady()
        {
            ///auto proceed with movie if nothing happens?



            var gettingHighLevelDiscInfo = Task<string>.Factory.StartNew(() => getHighLevelDiscInfo());
            btnCheckDriveMovie.Visibility = Visibility.Hidden;
            prgLoadingDisc.Visibility = Visibility.Visible;

            await gettingHighLevelDiscInfo;

            discName = gettingHighLevelDiscInfo.Result.ToString();


            goToSelectTitle();

        }

        #endregion
        

        #region   ----------WIZARD 1-----------Disc Confirmation
        private void goToSelectTitle()
        {
            grdWaitingForDisc.Visibility = Visibility.Collapsed;
            grdSearchingForTitle.Visibility = Visibility.Visible;
            getTMDBMovieResults();
            txtTitleSearch.Text = discName;
            grdMovies.ItemsSource = matchingTitles;
            if (matchingTitles.Count > 0)
            {
                grdMovies.SelectedIndex = 0;
                setSelectedTitle(matchingTitles[0]);
            }

            //setup the automatic timer
            timeDiscSelect = TimeSpan.FromSeconds(30);
            timerDiscSelect = new DispatcherTimer(new TimeSpan(0, 0, 1), DispatcherPriority.Normal, delegate
                {
                    lblDiscSelectCountDown.Content = timeDiscSelect.ToString("c");
                    if (timeDiscSelect == TimeSpan.Zero)
                    {
                        //go to next step.
                        goToTrackSelect();
                    }
                    timeDiscSelect = timeDiscSelect.Add(TimeSpan.FromSeconds(-1));
                }, Application.Current.Dispatcher);

            timerDiscSelect.Start();
        }

        private void btnTitleSearch_Click(object sender, RoutedEventArgs e)
        {
            timerDiscSelect.Stop();
            discName = txtTitleSearch.Text;
            getTMDBMovieResults();
            grdMovies.ItemsSource = matchingTitles;
            if (matchingTitles.Count > 0)
            {
                grdMovies.SelectedIndex = 0;
                setSelectedTitle(matchingTitles[0]);
            }
        }

        private string getHighLevelDiscInfo()
        {
            StringBuilder returnMe = new StringBuilder();
            ManagementClass mc = new ManagementClass("Win32_CDROMDrive");
            ManagementObjectCollection moc = mc.GetInstances();
            if (moc.Count != 0)
            {
                foreach (ManagementObject mo in moc)
                {
                    try
                    {
                        returnMe.AppendLine(mo["VolumeName"].ToString());
                    }
                    catch (Exception) { }
                }
            }
            string toReturn = returnMe.ToString().Replace("\r\n", "");
            if (toReturn.EndsWith("D1") || toReturn.EndsWith("D2") || toReturn.EndsWith("D3"))
            {
                toReturn = toReturn.Remove(toReturn.Length - 3, 3);
            }
            return toReturn.Replace('_', ' '); ;
        }

        private void getTMDBMovieResults()
        {
            matchingTitles.Clear();
            tmdbSearchResult movies = GetMovieResults();
            if (movies.total_results > 0)
            {
                foreach (tmdbResult result in movies.results)
                    matchingTitles.Add(result);
            }

        }

        private tmdbSearchResult GetMovieResults()
        {
            StringBuilder requestUrl = new StringBuilder();
            //https://api.themoviedb.org/3/search/movie?api_key=1abe04137ccd4fa521cb5f8e337b9418&language=en-US&query=Havana%20Nights&page=1&include_adult=false
            requestUrl.Append("https://api.themoviedb.org/3/search/movie?api_key=");
            requestUrl.Append(tmdbApiKey);
            requestUrl.Append("&language=en-US&query=");
            requestUrl.Append(discName);
            requestUrl.Append("&page=1&include_adult=false");
            Uri requestUri = new Uri(requestUrl.ToString());
            WebRequest request = WebRequest.Create(requestUri.ToString());

            WebResponse response = request.GetResponse();
            Stream dataStream = response.GetResponseStream();
            StreamReader reader = new StreamReader(dataStream);
            string text = reader.ReadToEnd();
            tmdbSearchResult searchResult = JsonConvert.DeserializeObject<tmdbSearchResult>(text);
            return searchResult;
        }

        private void grdMovies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            setSelectedTitle((tmdbResult)grdMovies.SelectedItem);
        }

        private void setSelectedTitle(tmdbResult _selectedTitle)
        {
            if(_selectedTitle != null)
            {       
                selectedTitle = _selectedTitle;
                lblSelectedDescription.Text = selectedTitle.overview;
                lblSelectedTitle.Content = selectedTitle.title;

                //imgSelectedItemPoster

                var fullFilePath = @"http://image.tmdb.org/t/p/w185" + _selectedTitle.poster_path;

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(fullFilePath, UriKind.Absolute);
                bitmap.EndInit();

                imgSelectedItemPoster.Source = bitmap;
            }
        }

        private void getDetailedMovieInfo(tmdbResult _selectedTitle)
        {
            StringBuilder requestUrl = new StringBuilder();
            //https://api.themoviedb.org/3/movie/348089?api_key=1abe04137ccd4fa521cb5f8e337b9418&language=en-US
            requestUrl.Append("https://api.themoviedb.org/3/movie/");
            requestUrl.Append(_selectedTitle.id);
            requestUrl.Append("?api_key=");
            requestUrl.Append(tmdbApiKey);
            requestUrl.Append("&language=en-US");
            Uri requestUri = new Uri(requestUrl.ToString());
            WebRequest request = WebRequest.Create(requestUri.ToString());

            WebResponse response = request.GetResponse();
            Stream dataStream = response.GetResponseStream();
            StreamReader reader = new StreamReader(dataStream);
            string text = reader.ReadToEnd();
            selectedMovieDetails = JsonConvert.DeserializeObject<tmdbMovieDetails>(text);
            
        }
        #endregion

        #region   ----------WIZARD 2-----------Track Selection

        private void btnGoToTrackSelect_Click(object sender, RoutedEventArgs e)
        {
            goToTrackSelect();
        }

        private async void goToTrackSelect()
        {
            grdSearchingForTitle.Visibility = Visibility.Collapsed;
            grdSelectTitlesForRip.Visibility = Visibility.Visible;

            timerDiscSelect.Stop();
            getDetailedMovieInfo(selectedTitle);
            lblSelectedMovieTitle.Content = selectedMovieDetails.title;

            int hours = selectedMovieDetails.runtime / 60;
            int minutes = selectedMovieDetails.runtime % 60;
            lblSelectedMovieLength.Content = hours.ToString() + ":" + minutes.ToString();
                        
            spReadingTrackInfo.Visibility = Visibility.Visible;
            dockTrackInfo.Visibility = Visibility.Collapsed;

            
            string suggestedMovieBasePath = ConfigurationManager.AppSettings["pathToMediaForRip"];
            StringBuilder suggestedMovieTitle = new StringBuilder();
            suggestedMovieTitle.Append(selectedMovieDetails.title);
            suggestedMovieTitle.Append(" (");
            suggestedMovieTitle.Append(selectedMovieDetails.release_date.Split('-')[0]);
            suggestedMovieTitle.Append(")");

            txtRipLocation.Text = suggestedMovieBasePath;
            lblFullPath.Content = txtRipLocation.Text + getDirectorySafeString(suggestedMovieTitle.ToString());
            var gettingDiscInfo = Task<string>.Factory.StartNew(() => getDetailedDiscInfo());


            await gettingDiscInfo;
            spReadingTrackInfo.Visibility = Visibility.Collapsed;
            dockTrackInfo.Visibility = Visibility.Visible;
            ParseDiscInfo(gettingDiscInfo.Result.ToString());
            GetProbableTrack();
            grdTracks.ItemsSource = currentDisc.tracks;
            grdTracks.SelectedItem = selectedTrack;

            

            //setup the automatic timer
            timeTrackSelect = TimeSpan.FromSeconds(30);
            timerTrackSelect = new DispatcherTimer(new TimeSpan(0, 0, 1), DispatcherPriority.Normal, delegate
            {
                lblTrackSelectCountDown.Content = timeTrackSelect.ToString("c");
                if (timeTrackSelect == TimeSpan.Zero)
                {
                    //go to next step.
                    addDiscToQueue();
                }
                timeTrackSelect = timeTrackSelect.Add(TimeSpan.FromSeconds(-1));
            }, Application.Current.Dispatcher);

            timerTrackSelect.Start();

        }

        private void btnBrowseRipLocation_Click(object sender, RoutedEventArgs e)
        {
            timerTrackSelect.Stop();

            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            CommonFileDialogResult result = dialog.ShowDialog();
                       

            if(result == CommonFileDialogResult.Ok)
            {
                StringBuilder suggestedMovieTitle = new StringBuilder();
                suggestedMovieTitle.Append(selectedMovieDetails.title);
                suggestedMovieTitle.Append(" (");
                suggestedMovieTitle.Append(selectedMovieDetails.release_date.Split('-')[0]);
                suggestedMovieTitle.Append(")");
                txtRipLocation.Text = dialog.FileName;
                lblFullPath.Content = dialog.FileName + "\\" + getDirectorySafeString(suggestedMovieTitle.ToString());

                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                config.AppSettings.Settings.Remove("pathToMediaForRip");

                config.AppSettings.Settings.Add("pathToMediaForRip", dialog.FileName);
                config.Save(ConfigurationSaveMode.Modified);
            }
        }

        private void GetProbableTrack()
        {
            track probableTrack = null;
            foreach(track tr in currentDisc.tracks)
            {
                if(probableTrack == null)
                {
                    probableTrack = tr;

                } else if(probableTrack.size < tr.size)
                {
                    probableTrack = tr;
                }

            }
            selectedTrack = probableTrack;

        }

        private void grdTracks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedTrack = (track)grdTracks.SelectedItem;
        }

        private String getDetailedDiscInfo()
        {
            //////hardcoded return disc info
            //return discInfoHardCode;

            // Start the child process.
            Process p = new Process();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;

            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\MakeMKV\\makemkvcon64.exe";
            p.StartInfo.Arguments = "-r info disc:1";
            p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }

        private void ParseDiscInfo(string discInfo)
        {
            currentDisc = new Disc();

            string[] lines = discInfo.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None);

            int currentTrackNumber = -1;
            track currentTrack = new track();
            foreach (string line in lines)
            {
                if (line.StartsWith("CINFO:1"))
                {
                    if (line.EndsWith("\"DVD disc\""))
                        discIsBlueRay = false;
                    else
                        discIsBlueRay = true;
                }
                if (line.StartsWith("CINFO:2") || line.StartsWith("CINFO:30") || line.StartsWith("CINFO:32"))
                {
                    string origTitle = getValueFromLine(line, 3);
                    string actualTitle = origTitle.Replace('_', ' ');

                    currentDisc.foundTitles.Add(actualTitle);
                }

                if (line.StartsWith("TINFO"))
                {
                    int trackNumber = Convert.ToInt16(getValueFromLine(line, 1).Split(':')[1]);
                    string partNumber = getValueFromLine(line, 2);
                    string value = getValueFromLine(line, 4);
                    if (trackNumber != currentTrackNumber)
                    {
                        if (currentTrackNumber != -1)
                        {
                            currentDisc.tracks.Add(currentTrack);
                        }
                        currentTrack = new track();
                        currentTrackNumber = trackNumber;
                    }
                    switch (partNumber)
                    {
                        case "9":
                            currentTrack.length = value;
                            break;
                        case "10":
                            currentTrack.size = float.Parse(value.Remove(value.Length - 3));
                            if (value.EndsWith("GB"))
                                currentTrack.size = currentTrack.size * 1000;
                            break;
                        default:
                            break;
                    }

                }


            }

        }

        private string getValueFromLine(string line, int whichParamIsValue)
        {
            //eg. 
            //CINFO:2,0,"HAVANA_NIGHTS"
            //TINFO:0,9,0,"0:02:33"
            string[] columns = line.Split(new[] { "," }, StringSplitOptions.None);
            return columns[whichParamIsValue - 1].Replace("\"", "");
        }

        #endregion

        #region   ----------WIZARD 3-----------ripping Disc
        private void btnAddDiscToQueue_Click(object sender, RoutedEventArgs e)
        {
            addDiscToQueue();
        }
        
        private async void addDiscToQueue()
        {
            timerTrackSelect.Stop();
           
            string fullPath = lblFullPath.Content.ToString();

            StringBuilder suggestedMovieTitle = new StringBuilder();
            suggestedMovieTitle.Append(selectedMovieDetails.title);
            suggestedMovieTitle.Append(" (");
            suggestedMovieTitle.Append(selectedMovieDetails.release_date.Split('-')[0]);
            suggestedMovieTitle.Append(")");
            string title = getDirectorySafeString(suggestedMovieTitle.ToString());

           
            string selectedTrackIndex = grdTracks.SelectedIndex.ToString();
            QueuedItem toAdd = new QueuedItem(fullPath, title, selectedTrackIndex);
            queuedItems.Add(toAdd);
            waitingToRip.Add(toAdd);
            StartOver();
            /////////Start Ripping
            //var rippingDisc = Task<string>.Factory.StartNew(() => RipDiscToMkv(fullPath, title, selectedTrackIndex));

            //await rippingDisc;
            /////////ripping complete
            
            ////check for errors
            //string ripResult = rippingDisc.Result.ToString();

            ///////////start compressing.
            //var compressingMkv = Task<string>.Factory.StartNew(() => CompressWithHandbrake());


            //await compressingMkv;


            ////////compressing complete
            //string compressResult = compressingMkv.Result.ToString();
        }


        

       
        private string getDirectorySafeString(string original)
        {
            foreach (char item in System.IO.Path.GetInvalidFileNameChars())
            {
                original = original.Replace(item, '_');
            }
            return original;
        }

        #endregion

      

        #region Handlers for Disc Detection
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Adds the windows message processing hook and registers USB device add/removal notification.
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null)
            {
                windowHandle = source.Handle;
                source.AddHook(HwndHandler);
                deviceDetector.MediaInsertedNotification.RegisterCDROMDeviceNotification(windowHandle);
            }
        }

        /// <summary>
        /// Method that receives window messages.
        /// </summary>
        private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (msg == deviceDetector.MediaInsertedNotification.WmDevicechange)
            {
                switch ((int)wparam)
                {
                    case deviceDetector.MediaInsertedNotification.DbtDeviceremovecomplete:
                        DiscRemoved(); // this is where you do your magic
                        break;
                    case deviceDetector.MediaInsertedNotification.DbtDevicearrival:
                        DiscReady(); // this is where you do your magic
                        break;
                }
            }

            handled = false;
            return IntPtr.Zero;
        }

        private string discInfoHardCode = "MSG:1005,0,1,\"MakeMKV v1.10.7 win(x86-release) started\",\"%1 started\",\"MakeMKV v1.10.7 win(x86-release)\"\r\nDRV:0,2,999,1,\"BD-ROM HL-DT-ST BDDVDRW UH12LS29 1.00\",\"HAVANA_NIGHTS\",\"J:\"\r\nDRV:1,256,999,0,\"\",\"\",\"\"\r\nDRV:2,256,999,0,\"\",\"\",\"\"\r\nDRV:3,256,999,0,\"\",\"\",\"\"\r\nDRV:4,256,999,0,\"\",\"\",\"\"\r\nDRV:5,256,999,0,\"\",\"\",\"\"\r\nDRV:6,256,999,0,\"\",\"\",\"\"\r\nDRV:7,256,999,0,\"\",\"\",\"\"\r\nDRV:8,256,999,0,\"\",\"\",\"\"\r\nDRV:9,256,999,0,\"\",\"\",\"\"\r\nDRV:10,256,999,0,\"\",\"\",\"\"\r\nDRV:11,256,999,0,\"\",\"\",\"\"\r\nDRV:12,256,999,0,\"\",\"\",\"\"\r\nDRV:13,256,999,0,\"\",\"\",\"\"\r\nDRV:14,256,999,0,\"\",\"\",\"\"\r\nDRV:15,256,999,0,\"\",\"\",\"\"\r\nMSG:3007,0,0,\"Using direct disc access mode\",\"Using direct disc access mode\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,0,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #5 was added (1 cell(s), 0:02:33)\",\"Title #%1 was added (%2 cell(s), %3)\",\"5\",\"1\",\"0:02:33\"\r\nMSG:3025,0,3,\"Title #6 has length of 28 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"6\",\"28\",\"120\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,0,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,16777216,3,\"Title #7 was added (1 cell(s), 0:02:07)\",\"Title #%1 was added (%2 cell(s), %3)\",\"7\",\"1\",\"0:02:07\"\r\nMSG:3025,0,3,\"Title #8 has length of 72 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"8\",\"72\",\"120\"\r\nMSG:3025,0,3,\"Title #9 has length of 68 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"9\",\"68\",\"120\"\r\nMSG:3025,0,3,\"Title #10 has length of 30 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"10\",\"30\",\"120\"\r\nMSG:3025,0,3,\"Title #11 has length of 72 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"11\",\"72\",\"120\"\r\nMSG:3025,0,3,\"Title #12 has length of 45 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"12\",\"45\",\"120\"\r\nMSG:3025,0,3,\"Title #13 has length of 89 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"13\",\"89\",\"120\"\r\nMSG:3025,0,3,\"Title #14 has length of 76 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"14\",\"76\",\"120\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,0,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #15 was added (1 cell(s), 0:23:47)\",\"Title #%1 was added (%2 cell(s), %3)\",\"15\",\"1\",\"0:23:47\"\r\nMSG:3028,0,3,\"Title #16 was added (10 cell(s), 0:12:27)\",\"Title #%1 was added (%2 cell(s), %3)\",\"16\",\"10\",\"0:12:27\"\r\nMSG:3025,0,3,\"Title #17 has length of 16 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"17\",\"16\",\"120\"\r\nMSG:3028,0,3,\"Title #18 was added (22 cell(s), 1:25:57)\",\"Title #%1 was added (%2 cell(s), %3)\",\"18\",\"22\",\"1:25:57\"\r\nMSG:3025,0,3,\"Title #19 has length of 87 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"19\",\"87\",\"120\"\r\nMSG:3025,0,3,\"Title #20 has length of 88 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"20\",\"88\",\"120\"\r\nMSG:3025,0,3,\"Title #21 has length of 89 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"21\",\"89\",\"120\"\r\nMSG:3025,0,3,\"Title #22 has length of 90 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"22\",\"90\",\"120\"\r\nMSG:3028,0,3,\"Title #23 was added (2 cell(s), 0:04:25)\",\"Title #%1 was added (%2 cell(s), %3)\",\"23\",\"2\",\"0:04:25\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,16777216,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #24 was added (1 cell(s), 0:10:59)\",\"Title #%1 was added (%2 cell(s), %3)\",\"24\",\"1\",\"0:10:59\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,16777216,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #25 was added (1 cell(s), 0:03:18)\",\"Title #%1 was added (%2 cell(s), %3)\",\"25\",\"1\",\"0:03:18\"\r\nMSG:3025,0,3,\"Title #26 has length of 111 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"26\",\"111\",\"120\"\r\nMSG:3025,0,3,\"Title #27 has length of 16 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"27\",\"16\",\"120\"\r\nMSG:5011,0,0,\"Operation successfully completed\",\"Operation successfully completed\"\r\nTCOUNT:8\r\nCINFO:1,6206,\"DVD disc\"\r\nCINFO:2,0,\"HAVANA_NIGHTS\"\r\nCINFO:30,0,\"HAVANA_NIGHTS\"\r\nCINFO:31,6119,\"<b>Source information</b><br>\"\r\nCINFO:32,0,\"HAVANA_NIGHTS\"\r\nCINFO:33,0,\"0\"\r\nTINFO:0,9,0,\"0:02:33\"\r\nTINFO:0,10,0,\"95.6 MB\"\r\nTINFO:0,11,0,\"100253696\"\r\nTINFO:0,24,0,\"5\"\r\nTINFO:0,25,0,\"1\"\r\nTINFO:0,26,0,\"2\"\r\nTINFO:0,27,0,\"title00.mkv\"\r\nTINFO:0,30,0,\"95.6 MB\"\r\nTINFO:0,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:0,33,0,\"0\"\r\nSINFO:0,0,1,6201,\"Video\"\r\nSINFO:0,0,5,0,\"V_MPEG2\"\r\nSINFO:0,0,6,0,\"Mpeg2\"\r\nSINFO:0,0,7,0,\"Mpeg2\"\r\nSINFO:0,0,13,0,\"7.3 Mb/s\"\r\nSINFO:0,0,19,0,\"720x480\"\r\nSINFO:0,0,20,0,\"4:3\"\r\nSINFO:0,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:0,0,22,0,\"0\"\r\nSINFO:0,0,30,0,\"Mpeg2\"\r\nSINFO:0,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:0,0,33,0,\"0\"\r\nSINFO:0,0,38,0,\"\"\r\nSINFO:0,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:0,1,1,6202,\"Audio\"\r\nSINFO:0,1,2,5091,\"Stereo\"\r\nSINFO:0,1,3,0,\"eng\"\r\nSINFO:0,1,4,0,\"English\"\r\nSINFO:0,1,5,0,\"A_AC3\"\r\nSINFO:0,1,6,0,\"DD\"\r\nSINFO:0,1,7,0,\"Dolby Digital\"\r\nSINFO:0,1,13,0,\"192 Kb/s\"\r\nSINFO:0,1,14,0,\"2\"\r\nSINFO:0,1,17,0,\"48000\"\r\nSINFO:0,1,22,0,\"0\"\r\nSINFO:0,1,30,0,\"DD Stereo English\"\r\nSINFO:0,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:0,1,33,0,\"90\"\r\nSINFO:0,1,38,0,\"d\"\r\nSINFO:0,1,39,0,\"Default\"\r\nSINFO:0,1,40,0,\"stereo\"\r\nSINFO:0,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:1,9,0,\"0:02:07\"\r\nTINFO:1,10,0,\"78.5 MB\"\r\nTINFO:1,11,0,\"82407424\"\r\nTINFO:1,24,0,\"7\"\r\nTINFO:1,25,0,\"1\"\r\nTINFO:1,26,0,\"2\"\r\nTINFO:1,27,0,\"title01.mkv\"\r\nTINFO:1,30,0,\"78.5 MB\"\r\nTINFO:1,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:1,33,0,\"0\"\r\nSINFO:1,0,1,6201,\"Video\"\r\nSINFO:1,0,5,0,\"V_MPEG2\"\r\nSINFO:1,0,6,0,\"Mpeg2\"\r\nSINFO:1,0,7,0,\"Mpeg2\"\r\nSINFO:1,0,13,0,\"7.3 Mb/s\"\r\nSINFO:1,0,19,0,\"720x480\"\r\nSINFO:1,0,20,0,\"4:3\"\r\nSINFO:1,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:1,0,22,0,\"0\"\r\nSINFO:1,0,30,0,\"Mpeg2\"\r\nSINFO:1,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:1,0,33,0,\"0\"\r\nSINFO:1,0,38,0,\"\"\r\nSINFO:1,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:1,1,1,6202,\"Audio\"\r\nSINFO:1,1,2,5091,\"Stereo\"\r\nSINFO:1,1,3,0,\"eng\"\r\nSINFO:1,1,4,0,\"English\"\r\nSINFO:1,1,5,0,\"A_AC3\"\r\nSINFO:1,1,6,0,\"DD\"\r\nSINFO:1,1,7,0,\"Dolby Digital\"\r\nSINFO:1,1,13,0,\"192 Kb/s\"\r\nSINFO:1,1,14,0,\"2\"\r\nSINFO:1,1,17,0,\"48000\"\r\nSINFO:1,1,22,0,\"0\"\r\nSINFO:1,1,30,0,\"DD Stereo English\"\r\nSINFO:1,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:1,1,33,0,\"90\"\r\nSINFO:1,1,38,0,\"d\"\r\nSINFO:1,1,39,0,\"Default\"\r\nSINFO:1,1,40,0,\"stereo\"\r\nSINFO:1,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:2,9,0,\"0:23:47\"\r\nTINFO:2,10,0,\"909.4 MB\"\r\nTINFO:2,11,0,\"953585664\"\r\nTINFO:2,24,0,\"15\"\r\nTINFO:2,25,0,\"1\"\r\nTINFO:2,26,0,\"2\"\r\nTINFO:2,27,0,\"title02.mkv\"\r\nTINFO:2,30,0,\"909.4 MB\"\r\nTINFO:2,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:2,33,0,\"0\"\r\nSINFO:2,0,1,6201,\"Video\"\r\nSINFO:2,0,5,0,\"V_MPEG2\"\r\nSINFO:2,0,6,0,\"Mpeg2\"\r\nSINFO:2,0,7,0,\"Mpeg2\"\r\nSINFO:2,0,13,0,\"7.3 Mb/s\"\r\nSINFO:2,0,19,0,\"720x480\"\r\nSINFO:2,0,20,0,\"4:3\"\r\nSINFO:2,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:2,0,22,0,\"0\"\r\nSINFO:2,0,30,0,\"Mpeg2\"\r\nSINFO:2,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:2,0,33,0,\"0\"\r\nSINFO:2,0,38,0,\"\"\r\nSINFO:2,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:2,1,1,6202,\"Audio\"\r\nSINFO:2,1,2,5091,\"Stereo\"\r\nSINFO:2,1,3,0,\"eng\"\r\nSINFO:2,1,4,0,\"English\"\r\nSINFO:2,1,5,0,\"A_AC3\"\r\nSINFO:2,1,6,0,\"DD\"\r\nSINFO:2,1,7,0,\"Dolby Digital\"\r\nSINFO:2,1,13,0,\"192 Kb/s\"\r\nSINFO:2,1,14,0,\"2\"\r\nSINFO:2,1,17,0,\"48000\"\r\nSINFO:2,1,22,0,\"0\"\r\nSINFO:2,1,30,0,\"DD Stereo English\"\r\nSINFO:2,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:2,1,33,0,\"90\"\r\nSINFO:2,1,38,0,\"d\"\r\nSINFO:2,1,39,0,\"Default\"\r\nSINFO:2,1,40,0,\"stereo\"\r\nSINFO:2,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:3,8,0,\"10\"\r\nTINFO:3,9,0,\"0:12:27\"\r\nTINFO:3,10,0,\"455.8 MB\"\r\nTINFO:3,11,0,\"477952000\"\r\nTINFO:3,24,0,\"16\"\r\nTINFO:3,25,0,\"10\"\r\nTINFO:3,26,0,\"2,3,4,5,6,7,8,9,10,11\"\r\nTINFO:3,27,0,\"title03.mkv\"\r\nTINFO:3,30,0,\"10 chapter(s) , 455.8 MB\"\r\nTINFO:3,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:3,33,0,\"0\"\r\nSINFO:3,0,1,6201,\"Video\"\r\nSINFO:3,0,5,0,\"V_MPEG2\"\r\nSINFO:3,0,6,0,\"Mpeg2\"\r\nSINFO:3,0,7,0,\"Mpeg2\"\r\nSINFO:3,0,13,0,\"7.3 Mb/s\"\r\nSINFO:3,0,19,0,\"720x480\"\r\nSINFO:3,0,20,0,\"4:3\"\r\nSINFO:3,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:3,0,22,0,\"0\"\r\nSINFO:3,0,30,0,\"Mpeg2\"\r\nSINFO:3,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:3,0,33,0,\"0\"\r\nSINFO:3,0,38,0,\"\"\r\nSINFO:3,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:3,1,1,6202,\"Audio\"\r\nSINFO:3,1,2,5091,\"Stereo\"\r\nSINFO:3,1,3,0,\"eng\"\r\nSINFO:3,1,4,0,\"English\"\r\nSINFO:3,1,5,0,\"A_AC3\"\r\nSINFO:3,1,6,0,\"DD\"\r\nSINFO:3,1,7,0,\"Dolby Digital\"\r\nSINFO:3,1,13,0,\"192 Kb/s\"\r\nSINFO:3,1,14,0,\"2\"\r\nSINFO:3,1,17,0,\"48000\"\r\nSINFO:3,1,22,0,\"0\"\r\nSINFO:3,1,30,0,\"DD Stereo English\"\r\nSINFO:3,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:3,1,33,0,\"90\"\r\nSINFO:3,1,38,0,\"d\"\r\nSINFO:3,1,39,0,\"Default\"\r\nSINFO:3,1,40,0,\"stereo\"\r\nSINFO:3,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:4,8,0,\"20\"\r\nTINFO:4,9,0,\"1:25:57\"\r\nTINFO:4,10,0,\"3.8 GB\"\r\nTINFO:4,11,0,\"4159121408\"\r\nTINFO:4,24,0,\"18\"\r\nTINFO:4,25,0,\"2\"\r\nTINFO:4,26,0,\"2-14,15-23\"\r\nTINFO:4,27,0,\"title04.mkv\"\r\nTINFO:4,30,0,\"20 chapter(s) , 3.8 GB\"\r\nTINFO:4,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:4,33,0,\"0\"\r\nSINFO:4,0,1,6201,\"Video\"\r\nSINFO:4,0,5,0,\"V_MPEG2\"\r\nSINFO:4,0,6,0,\"Mpeg2\"\r\nSINFO:4,0,7,0,\"Mpeg2\"\r\nSINFO:4,0,13,0,\"7.3 Mb/s\"\r\nSINFO:4,0,19,0,\"720x480\"\r\nSINFO:4,0,20,0,\"16:9\"\r\nSINFO:4,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:4,0,22,0,\"0\"\r\nSINFO:4,0,30,0,\"Mpeg2\"\r\nSINFO:4,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,0,33,0,\"0\"\r\nSINFO:4,0,38,0,\"\"\r\nSINFO:4,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,1,1,6202,\"Audio\"\r\nSINFO:4,1,2,0,\"Surround 5.1\"\r\nSINFO:4,1,3,0,\"eng\"\r\nSINFO:4,1,4,0,\"English\"\r\nSINFO:4,1,5,0,\"A_AC3\"\r\nSINFO:4,1,6,0,\"DD\"\r\nSINFO:4,1,7,0,\"Dolby Digital\"\r\nSINFO:4,1,13,0,\"448 Kb/s\"\r\nSINFO:4,1,14,0,\"6\"\r\nSINFO:4,1,17,0,\"48000\"\r\nSINFO:4,1,22,0,\"0\"\r\nSINFO:4,1,30,0,\"DD Surround 5.1 English\"\r\nSINFO:4,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,1,33,0,\"90\"\r\nSINFO:4,1,38,0,\"d\"\r\nSINFO:4,1,39,0,\"Default\"\r\nSINFO:4,1,40,0,\"5.1(side)\"\r\nSINFO:4,1,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,2,1,6202,\"Audio\"\r\nSINFO:4,2,2,5091,\"Stereo\"\r\nSINFO:4,2,3,0,\"eng\"\r\nSINFO:4,2,4,0,\"English\"\r\nSINFO:4,2,5,0,\"A_AC3\"\r\nSINFO:4,2,6,0,\"DD\"\r\nSINFO:4,2,7,0,\"Dolby Digital\"\r\nSINFO:4,2,13,0,\"192 Kb/s\"\r\nSINFO:4,2,14,0,\"2\"\r\nSINFO:4,2,17,0,\"48000\"\r\nSINFO:4,2,22,0,\"0\"\r\nSINFO:4,2,30,0,\"DD Stereo English\"\r\nSINFO:4,2,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,2,33,0,\"90\"\r\nSINFO:4,2,38,0,\"\"\r\nSINFO:4,2,40,0,\"stereo\"\r\nSINFO:4,2,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,3,1,6202,\"Audio\"\r\nSINFO:4,3,2,5091,\"Stereo\"\r\nSINFO:4,3,3,0,\"eng\"\r\nSINFO:4,3,4,0,\"English\"\r\nSINFO:4,3,5,0,\"A_AC3\"\r\nSINFO:4,3,6,0,\"DD\"\r\nSINFO:4,3,7,0,\"Dolby Digital\"\r\nSINFO:4,3,13,0,\"192 Kb/s\"\r\nSINFO:4,3,14,0,\"2\"\r\nSINFO:4,3,17,0,\"48000\"\r\nSINFO:4,3,22,0,\"1\"\r\nSINFO:4,3,30,0,\"DD Stereo English\"\r\nSINFO:4,3,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,3,33,0,\"90\"\r\nSINFO:4,3,38,0,\"\"\r\nSINFO:4,3,40,0,\"stereo\"\r\nSINFO:4,3,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,4,1,6203,\"Subtitles\"\r\nSINFO:4,4,3,0,\"eng\"\r\nSINFO:4,4,4,0,\"English\"\r\nSINFO:4,4,5,0,\"S_VOBSUB\"\r\nSINFO:4,4,6,0,\"\"\r\nSINFO:4,4,7,0,\"Dvd Subtitles\"\r\nSINFO:4,4,22,0,\"0\"\r\nSINFO:4,4,30,0,\" English\"\r\nSINFO:4,4,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,4,33,0,\"90\"\r\nSINFO:4,4,38,0,\"d\"\r\nSINFO:4,4,39,0,\"Default\"\r\nSINFO:4,4,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,5,1,6203,\"Subtitles\"\r\nSINFO:4,5,3,0,\"spa\"\r\nSINFO:4,5,4,0,\"Spanish\"\r\nSINFO:4,5,5,0,\"S_VOBSUB\"\r\nSINFO:4,5,6,0,\"\"\r\nSINFO:4,5,7,0,\"Dvd Subtitles\"\r\nSINFO:4,5,22,0,\"0\"\r\nSINFO:4,5,30,0,\" Spanish\"\r\nSINFO:4,5,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,5,33,0,\"90\"\r\nSINFO:4,5,38,0,\"\"\r\nSINFO:4,5,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,6,1,6203,\"Subtitles\"\r\nSINFO:4,6,3,0,\"eng\"\r\nSINFO:4,6,4,0,\"English\"\r\nSINFO:4,6,5,0,\"S_VOBSUB\"\r\nSINFO:4,6,6,0,\"\"\r\nSINFO:4,6,7,0,\"Dvd Subtitles\"\r\nSINFO:4,6,22,0,\"0\"\r\nSINFO:4,6,30,0,\" English\"\r\nSINFO:4,6,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,6,33,0,\"90\"\r\nSINFO:4,6,38,0,\"\"\r\nSINFO:4,6,42,5088,\"( Lossless conversion )\"\r\nTINFO:5,8,0,\"2\"\r\nTINFO:5,9,0,\"0:04:25\"\r\nTINFO:5,10,0,\"163.2 MB\"\r\nTINFO:5,11,0,\"171163648\"\r\nTINFO:5,24,0,\"23\"\r\nTINFO:5,25,0,\"2\"\r\nTINFO:5,26,0,\"2,3\"\r\nTINFO:5,27,0,\"title05.mkv\"\r\nTINFO:5,30,0,\"2 chapter(s) , 163.2 MB\"\r\nTINFO:5,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:5,33,0,\"0\"\r\nSINFO:5,0,1,6201,\"Video\"\r\nSINFO:5,0,5,0,\"V_MPEG2\"\r\nSINFO:5,0,6,0,\"Mpeg2\"\r\nSINFO:5,0,7,0,\"Mpeg2\"\r\nSINFO:5,0,13,0,\"7.5 Mb/s\"\r\nSINFO:5,0,19,0,\"720x480\"\r\nSINFO:5,0,20,0,\"16:9\"\r\nSINFO:5,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:5,0,22,0,\"0\"\r\nSINFO:5,0,30,0,\"Mpeg2\"\r\nSINFO:5,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:5,0,33,0,\"0\"\r\nSINFO:5,0,38,0,\"\"\r\nSINFO:5,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:5,1,1,6202,\"Audio\"\r\nSINFO:5,1,2,5091,\"Stereo\"\r\nSINFO:5,1,3,0,\"eng\"\r\nSINFO:5,1,4,0,\"English\"\r\nSINFO:5,1,5,0,\"A_AC3\"\r\nSINFO:5,1,6,0,\"DD\"\r\nSINFO:5,1,7,0,\"Dolby Digital\"\r\nSINFO:5,1,13,0,\"192 Kb/s\"\r\nSINFO:5,1,14,0,\"2\"\r\nSINFO:5,1,17,0,\"48000\"\r\nSINFO:5,1,22,0,\"0\"\r\nSINFO:5,1,30,0,\"DD Stereo English\"\r\nSINFO:5,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:5,1,33,0,\"90\"\r\nSINFO:5,1,38,0,\"d\"\r\nSINFO:5,1,39,0,\"Default\"\r\nSINFO:5,1,40,0,\"stereo\"\r\nSINFO:5,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:6,9,0,\"0:10:59\"\r\nTINFO:6,10,0,\"424.0 MB\"\r\nTINFO:6,11,0,\"444620800\"\r\nTINFO:6,24,0,\"24\"\r\nTINFO:6,25,0,\"1\"\r\nTINFO:6,26,0,\"2\"\r\nTINFO:6,27,0,\"title06.mkv\"\r\nTINFO:6,30,0,\"424.0 MB\"\r\nTINFO:6,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:6,33,0,\"0\"\r\nSINFO:6,0,1,6201,\"Video\"\r\nSINFO:6,0,5,0,\"V_MPEG2\"\r\nSINFO:6,0,6,0,\"Mpeg2\"\r\nSINFO:6,0,7,0,\"Mpeg2\"\r\nSINFO:6,0,13,0,\"7.3 Mb/s\"\r\nSINFO:6,0,19,0,\"720x480\"\r\nSINFO:6,0,20,0,\"4:3\"\r\nSINFO:6,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:6,0,22,0,\"0\"\r\nSINFO:6,0,30,0,\"Mpeg2\"\r\nSINFO:6,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:6,0,33,0,\"0\"\r\nSINFO:6,0,38,0,\"\"\r\nSINFO:6,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:6,1,1,6202,\"Audio\"\r\nSINFO:6,1,2,5091,\"Stereo\"\r\nSINFO:6,1,3,0,\"eng\"\r\nSINFO:6,1,4,0,\"English\"\r\nSINFO:6,1,5,0,\"A_AC3\"\r\nSINFO:6,1,6,0,\"DD\"\r\nSINFO:6,1,7,0,\"Dolby Digital\"\r\nSINFO:6,1,13,0,\"192 Kb/s\"\r\nSINFO:6,1,14,0,\"2\"\r\nSINFO:6,1,17,0,\"48000\"\r\nSINFO:6,1,22,0,\"0\"\r\nSINFO:6,1,30,0,\"DD Stereo English\"\r\nSINFO:6,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:6,1,33,0,\"90\"\r\nSINFO:6,1,38,0,\"d\"\r\nSINFO:6,1,39,0,\"Default\"\r\nSINFO:6,1,40,0,\"stereo\"\r\nSINFO:6,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:7,9,0,\"0:03:18\"\r\nTINFO:7,10,0,\"141.3 MB\"\r\nTINFO:7,11,0,\"148174848\"\r\nTINFO:7,24,0,\"25\"\r\nTINFO:7,25,0,\"1\"\r\nTINFO:7,26,0,\"2\"\r\nTINFO:7,27,0,\"title07.mkv\"\r\nTINFO:7,30,0,\"141.3 MB\"\r\nTINFO:7,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:7,33,0,\"0\"\r\nSINFO:7,0,1,6201,\"Video\"\r\nSINFO:7,0,5,0,\"V_MPEG2\"\r\nSINFO:7,0,6,0,\"Mpeg2\"\r\nSINFO:7,0,7,0,\"Mpeg2\"\r\nSINFO:7,0,13,0,\"7.9 Mb/s\"\r\nSINFO:7,0,19,0,\"720x480\"\r\nSINFO:7,0,20,0,\"4:3\"\r\nSINFO:7,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:7,0,22,0,\"0\"\r\nSINFO:7,0,30,0,\"Mpeg2\"\r\nSINFO:7,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:7,0,33,0,\"0\"\r\nSINFO:7,0,38,0,\"\"\r\nSINFO:7,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:7,1,1,6202,\"Audio\"\r\nSINFO:7,1,2,5091,\"Stereo\"\r\nSINFO:7,1,3,0,\"eng\"\r\nSINFO:7,1,4,0,\"English\"\r\nSINFO:7,1,5,0,\"A_AC3\"\r\nSINFO:7,1,6,0,\"DD\"\r\nSINFO:7,1,7,0,\"Dolby Digital\"\r\nSINFO:7,1,13,0,\"192 Kb/s\"\r\nSINFO:7,1,14,0,\"2\"\r\nSINFO:7,1,17,0,\"48000\"\r\nSINFO:7,1,22,0,\"0\"\r\nSINFO:7,1,30,0,\"DD Stereo English\"\r\nSINFO:7,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:7,1,33,0,\"90\"\r\nSINFO:7,1,38,0,\"d\"\r\nSINFO:7,1,39,0,\"Default\"\r\nSINFO:7,1,40,0,\"stereo\"\r\nSINFO:7,1,42,5088,\"( Lossless conversion )\"\r\n";






        #endregion


        //handlers for stopping timers when user interaction occurs on the active wizard step.
        private void txtTitleSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            timerDiscSelect.Stop();
        }


        
        
    }

    public class ColumnViewportConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double columnHeight = System.Convert.ToDouble(value);
            return new Rect(0, 0, 1, columnHeight * 2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Source shouldn't be updated");
        }

        #endregion
    }

    public class rippingDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate progressBarDataTemplate { get; set; }
        public DataTemplate completeDataTemplate { get; set; }
        public DataTemplate waitingTextDataTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            QueuedItem thisItem = (QueuedItem)item;
            if (thisItem.ripping)
            {
                return progressBarDataTemplate;
            }
            else
            {
                if(thisItem.ripped == true)
                {
                    return completeDataTemplate;
                }
                else
                {
                    return waitingTextDataTemplate;
                }
            }
            

        }
    }

    public class copyingDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate progressBarDataTemplate { get; set; }
        public DataTemplate completeDataTemplate { get; set; }
        public DataTemplate waitingTextDataTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            QueuedItem thisItem = (QueuedItem)item;
            if (thisItem.copying)
            {
                return progressBarDataTemplate;
            }
            else
            {
                if (thisItem.copied == true)
                {
                    return completeDataTemplate;
                }
                else
                {
                    return waitingTextDataTemplate;
                }
            }


        }
    }

    public class compressingDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate progressBarDataTemplate { get; set; }
        public DataTemplate completeDataTemplate { get; set; }
        public DataTemplate waitingTextDataTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            QueuedItem thisItem = (QueuedItem)item;
            if (thisItem.compressing)
            {
                return progressBarDataTemplate;
            }
            else
            {
                if (thisItem.compressed == true)
                {
                    return completeDataTemplate;
                }
                else
                {
                    return waitingTextDataTemplate;
                }
            }


        }
    }

    public class QueuedItem
    {
        public QueuedItem(String _fullPath, String _title, String _selectedTrackIndex)
        {
            title = _title;
            fullPath = _fullPath;
            selectedTrackIndex = _selectedTrackIndex;
        }

        public String title { get; set; }
        public Boolean ripping { get; set; }
        public Boolean ripped { get; set; }
        public Boolean compressing { get; set; }
        public Boolean compressed { get; set; }
        public Boolean copying { get; set; }
        public Boolean copied { get; set; }
        public Boolean removed { get; set; }
        public String fullPath { get; set; }
        public String selectedTrackIndex { get; set; }
        public String pathToRip { get; set; }
        public String pathToCompression { get; set; }
        public Boolean isTV { get; set; }
        public int tvSeason { get; set; }
        public int tvEpisode { get; set; }
        public string tvShowTitle { get; set; }
    }

    public class Disc
    {
        public Disc()
        {
            foundTitles = new List<string>();
            tracks = new List<track>();
        }

        public List<string> foundTitles { get; set; }
        public List<track> tracks { get; set; }
    }
    public class track
    {
        public string length { get; set; }
        public float size { get; set; }
    }

    public class tmdbSearchResult
    {
        public int page { get; set; }
        public int total_results { get; set; }
        public int total_pages { get; set; }
        public IList<tmdbResult> results { get; set; }
    }
    public class tmdbResult
    {
        public int vote_count { get; set; }
        public int id { get; set; }
        public bool video { get; set; }
        public float vote_average { get; set; }
        private string _title;
        public string title {
            get { return _title; }
            set
            {
                string temp = value;
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    temp = temp.Replace(c, '-');
                }
                _title = temp;
            }
        }
        public float popularity { get; set; }
        public string poster_path { get; set; }
        public string original_title { get; set; }
        public IList<int> genre_ids { get; set; }
        public string backdrop_path { get; set; }
        public bool adult { get; set; }
        public string overview { get; set; }
        public string release_date { get; set; }
    }

    public class tmdbMovieDetails
    {
        public bool adult { get; set; }
        public string backdrop_path { get; set; }
        public int budget { get; set; }
        public string homepage { get; set; }
        public int id { get; set; }
        public string imdb_id { get; set; }
        public string original_language { get; set; }
        public string origingl_title { get; set; }
        public string overview { get; set; }
        public string poster_path { get; set; }
        public string release_date { get; set; }
        public int runtime { get; set; }
        public string tagline { get; set; }
        public string title { get; set; }
    }

    class EjectMedia
    {
        // Constants used in DLL methods
        const uint GENERICREAD = 0x80000000;
        const uint OPENEXISTING = 3;
        const uint IOCTL_STORAGE_EJECT_MEDIA = 2967560;
        const int INVALID_HANDLE = -1;

        // File Handle
        private static IntPtr fileHandle;
        private static uint returnedBytes;
        // Use Kernel32 via interop to access required methods
        // Get a File Handle
        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr CreateFile(string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr attributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
        [DllImport("kernel32", SetLastError = true)]
        static extern int CloseHandle(IntPtr driveHandle);
        [DllImport("kernel32", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr driveHandle,
        uint IoControlCode,
        IntPtr lpInBuffer,
        uint inBufferSize,
        IntPtr lpOutBuffer,
        uint outBufferSize,
        ref uint lpBytesReturned,
                 IntPtr lpOverlapped);

        public static void Eject(string driveLetter)
        {
            try
            {
                // Create an handle to the drive
                fileHandle = CreateFile(driveLetter,
                 GENERICREAD,
                 0,
                 IntPtr.Zero,
                 OPENEXISTING,
                 0,
                  IntPtr.Zero);
                if ((int)fileHandle != INVALID_HANDLE)
                {
                    // Eject the disk
                    DeviceIoControl(fileHandle,
                     IOCTL_STORAGE_EJECT_MEDIA,
                     IntPtr.Zero, 0,
                     IntPtr.Zero, 0,
                     ref returnedBytes,
                            IntPtr.Zero);
                }
            }
            catch
            {
                throw new Exception(Marshal.GetLastWin32Error().ToString());
            }
            finally
            {
                // Close Drive Handle
                CloseHandle(fileHandle);
                fileHandle = IntPtr.Zero;
            }
        }
    }
}


