﻿using System;
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
        public bool discIsBlueRay;
        //dependency property declaration
        private bool _discIsTv;
        public bool discIsTv
        {
            get { return _discIsTv; }
            set
            {
                _discIsTv = value;
                if (timerDiscSelect != null)
                    timerDiscSelect.Stop();
                switch (value)
                {
                    case true:
                        tabSelectMovieTrack.Visibility = Visibility.Collapsed;
                        tabSelectEpisodes.Visibility = Visibility.Visible;
                        grdMovieSearchResults.Visibility = Visibility.Collapsed;
                        grdTvSearchResults.Visibility = Visibility.Visible;
                        break;
                    default:
                        tabSelectMovieTrack.Visibility = Visibility.Visible;
                        tabSelectEpisodes.Visibility = Visibility.Collapsed;
                        grdMovieSearchResults.Visibility = Visibility.Visible;
                        grdTvSearchResults.Visibility = Visibility.Collapsed;
                        break;
                }
       
               
            }
        }
        public string discName;
        const string tmdbApiKey = "1abe04137ccd4fa521cb5f8e337b9418";
        const string tmdbImageApiUrl = "http://image.tmdb.org/t/p/w185"; // /nuUKcfRYjifwjIJPN1J6kIGcSvD.jpg"
        public ObservableCollection<tmdbMovieResult> matchingMovies;
        public ObservableCollection<tmdbTvShowResult> matchingShows;
        public ObservableCollection<tmdbTvEpisode> matchingEpisodes;
        public tmdbMovieResult selectedMovie;
        public tmdbTvShowResult selectedShow;
        public tmdbTvEpisode selectedEpisode;
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

        private NotifyMQTT notifier;

        private object lockObj;

        #endregion


        [DllImport("winmm.dll", EntryPoint = "mciSendStringA")]
        public static extern void mciSendString(string lpstrCommand, string lpstrReturnString, long uReturnLength, long hwndCallback);


        #region Constructor

        public MainWindow()
        {
            InitializeComponent();

            Debug.WriteLine("app started");

            StartOver();
            notifier = new NotifyMQTT();
            lockObj = new object();
            matchingMovies = new ObservableCollection<tmdbMovieResult>();
            queuedItems = new ObservableCollection<QueuedItem>();
            matchingEpisodes = new ObservableCollection<tmdbTvEpisode>();
            waitingToRip = new List<QueuedItem>();
            waitingToCopy = new List<QueuedItem>();
            waitingToCompress = new List<QueuedItem>();

            lvInProgress.ItemsSource = queuedItems;

            BindingOperations.EnableCollectionSynchronization(queuedItems, lockObj);

            rippingWorker = new BackgroundWorker();
            isRippingThreadRunning = true;
            rippingWorker.DoWork += ripDiscThread;
            //rippingWorker.WorkerReportsProgress = true;
            //rippingWorker.ProgressChanged += rippingProgressChanged;
            rippingWorker.RunWorkerAsync();

            compressionWorker = new BackgroundWorker();
            isCompressionThreadRunning = true;
            compressionWorker.DoWork += compressionThread;
            //compressionWorker.RunWorkerCompleted +=
            compressionWorker.RunWorkerAsync();

            copyWorker = new BackgroundWorker();
            isCopyThreadRunning = true;
            copyWorker.DoWork += copyfileThread;
            copyWorker.RunWorkerAsync();
                       

        }

        #endregion

       
        
        #region View Event Handlers

        #region title screen
        //check for disc then read high level info.
        private void btnTitleSearch_Click(object sender, RoutedEventArgs e)
        {
            if (timerDiscSelect != null)
                timerDiscSelect.Stop();

            discName = txtTitleSearch.Text;
            if (discIsTv)
            {
                getTMDBTVResults();
                grdTvSearchResults.ItemsSource = matchingShows;
                if (matchingShows.Count > 0)
                {
                    grdTvSearchResults.SelectedIndex = 0;
                    setSelectedShow(matchingShows[0]);
                }
            }
            else
            {
                getTMDBMovieResults();
                grdMovieSearchResults.ItemsSource = matchingMovies;
                if (matchingMovies.Count > 0)
                {
                    grdMovieSearchResults.SelectedIndex = 0;
                    setSelectedMovie(matchingMovies[0]);
                }
            }
                
            
        }


        private void btnCheckDriveMovie_Click(object sender, RoutedEventArgs e)
        {
            discIsTv = chkIsTV.IsChecked == true;

            
#if (TESTINGNODISC)
                DiscReady();
#else
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom))
            {
                if (drive.IsReady)
                    DiscReady();
            }
#endif

        }
        private void grdSearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(chkIsTV.IsChecked == true)
                setSelectedShow((tmdbTvShowResult)grdTvSearchResults.SelectedItem);
            else
                setSelectedMovie((tmdbMovieResult)grdMovieSearchResults.SelectedItem);
        }


        private void chkIsTV_Checked(object sender, RoutedEventArgs e)
        {
            discIsTv = (bool)chkIsTV.IsChecked;
        }

        private void txtTitleSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if(timerDiscSelect != null)
                timerDiscSelect.Stop();
        }

#endregion title screen

#region movie track screen

        private void grdTracks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedTrack = (track)grdTracks.SelectedItem;
        }
        private void grdTvTracks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
           
        }
        private void grdEpisodes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedEpisode = (tmdbTvEpisode)grdEpisodes.SelectedItem;
        }

        private void btnStartOver2_Click(object sender, RoutedEventArgs e)
        {
            StartOver();
        }
#endregion movie track screen

#region Episode Selection

        private async void btnSubmitSeasonEntry_Click(object sender, RoutedEventArgs e)
        {
            

            if (timerDiscSelect != null)
                timerDiscSelect.Stop();
            if (timerTrackSelect != null)
                timerTrackSelect.Stop();
            try
            {
                //get detailed season info.
                tmdbSeasonSearchResult season = GetTvSeasonResults(txtSeasonNumber.Text, selectedShow.id);
                matchingEpisodes.Clear();

                if (season.episodes.Count > 0)
                {
                    foreach (tmdbTvEpisode result in season.episodes)
                        matchingEpisodes.Add(result);
                }
                grdEpisodes.ItemsSource = matchingEpisodes;
                if (matchingEpisodes.Count > 0)
                {
                    grdEpisodes.SelectedIndex = 0;
                    setSelectedEpisode(matchingEpisodes[0]);
                }
            }
            catch (Exception)
            {
                lblSeasonNotFound.Visibility = Visibility.Visible;

            }
            //only read the disc if we havent read it yet...
            if(grdTvTracks.Items.Count == 0)
            {
                //get tracks from disc
                overlay.Visibility = Visibility.Visible;

#if TESTINGNODISC
                currentDisc = new Disc();
                track dummyTrack = new track();
                dummyTrack.length = "1:55:00";
                dummyTrack.size = 1500;
                currentDisc.tracks.Add(dummyTrack);
                System.Threading.Thread.Sleep(7000);
#else
                //get detailed tv show data
                var gettingDiscInfo = Task<string>.Factory.StartNew(() => getDetailedDiscInfo());


                await gettingDiscInfo;
                
                ParseDiscInfo(gettingDiscInfo.Result.ToString());
#endif
                
                overlay.Visibility = Visibility.Collapsed;
                grdTvTracks.ItemsSource = currentDisc.tracks;
            }
           
        }

        private void btnAddEpisodeQueue_Click(object sender, RoutedEventArgs e)
        {
            if(grdEpisodes.SelectedItem != null && grdTvTracks.SelectedItem != null)
            {
                string fullPath = lblTvFullPath.Content.ToString();
                        
                StringBuilder episodeTitle = new StringBuilder();
                episodeTitle.Append(selectedShow.name);
                episodeTitle.Append("S");
                episodeTitle.Append(Int32.Parse(txtSeasonNumber.Text).ToString("D2"));
                episodeTitle.Append("E");
                episodeTitle.Append(selectedEpisode.episode_number.ToString("D2"));
                episodeTitle.Append("-");
                episodeTitle.Append(selectedEpisode.name);
                string selectedTrackIndex = grdTvTracks.SelectedIndex.ToString();
                track selectedTrack = (track)grdTvTracks.SelectedItem;
                QueuedItem toAdd = new QueuedItem(fullPath, episodeTitle.ToString(), selectedTrackIndex);
                toAdd.rippedMKVTitle = selectedTrack.title;
                toAdd.isTV = true;
                toAdd.tvEpisode = selectedEpisode.episode_number;
                toAdd.tvSeason = Int32.Parse(txtSeasonNumber.Text);
                toAdd.tvShowTitle = selectedShow.name;
                queuedItems.Add(toAdd);
                waitingToRip.Add(toAdd);
            }
        }

#endregion Episode Selection

#endregion

        private void goToSelectTitle()
        {
            //grdWaitingForDisc.Visibility = Visibility.Collapsed;
            //grdSearchingForTitle.Visibility = Visibility.Visible;
            if (discIsTv)
            {
                getTMDBTVResults();
                grdTvSearchResults.ItemsSource = matchingShows;
                if(matchingShows.Count > 0)
                {
                    grdTvSearchResults.SelectedIndex = 0;
                    setSelectedShow(matchingShows[0]);
                }
            }
                
            else
            {
                getTMDBMovieResults();
                grdMovieSearchResults.ItemsSource = matchingMovies;
                if (matchingMovies.Count > 0)
                {
                    grdMovieSearchResults.SelectedIndex = 0;
                    setSelectedMovie(matchingMovies[0]);
                }
            }
                

                txtTitleSearch.Text = discName;
            
           

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
                       
        private void getTMDBMovieResults()
        {
            matchingMovies.Clear();
            tmdbMovieSearchResult movies = GetMovieSearchResults();
            if (movies.total_results > 0)
            {
                foreach (tmdbMovieResult result in movies.results)
                    matchingMovies.Add(result);
            }

        }

        private void getTMDBTVResults()
        {
            matchingShows.Clear();
            tmdbTVSearchResult shows = GetTvSearchResults();
            if(shows.total_results > 0)
            {
                foreach (tmdbTvShowResult result in shows.results)
                    matchingShows.Add(result);
            }
        }
        
        private void setSelectedEpisode(tmdbTvEpisode _selectedEpisode)
        {
            selectedEpisode = _selectedEpisode;
        }

        private void setSelectedMovie(tmdbMovieResult _selectedTitle)
        {
            if(_selectedTitle != null)
            {       
                selectedMovie = _selectedTitle;
                lblSelectedDescription.Text = selectedMovie.overview;
                lblSelectedTitle.Content = selectedMovie.title;

                //imgSelectedItemPoster

                var fullFilePath = @"http://image.tmdb.org/t/p/w185" + _selectedTitle.poster_path;

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(fullFilePath, UriKind.Absolute);
                bitmap.EndInit();

                imgSelectedItemPoster.Source = bitmap;
            }
        }

        private void setSelectedShow(tmdbTvShowResult _selectedShow)
        {
            if (_selectedShow != null)
            {
                selectedShow = _selectedShow;
                lblSelectedDescription.Text = selectedShow.overview;
                lblSelectedTitle.Content = selectedShow.name;

                //imgSelectedItemPoster

                var fullFilePath = @"http://image.tmdb.org/t/p/w185" + _selectedShow.poster_path;

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(fullFilePath, UriKind.Absolute);
                bitmap.EndInit();

                imgSelectedItemPoster.Source = bitmap;
            }
        }
        
        private void btnGoToTrackSelect_Click(object sender, RoutedEventArgs e)
        {
            if (chkIsTV.IsChecked == true)
                GoToEpisodeSelection();
            else
                goToTrackSelect();
        }

        private async void goToTrackSelect()
        {
            overlay.Visibility = Visibility.Visible;
            tabControlMain.SelectedIndex = 1;
            if(timerDiscSelect != null)
                timerDiscSelect.Stop();
            getDetailedMovieInfo(selectedMovie);
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
            lblFullPath.Content = txtRipLocation.Text + "\\" + getDirectorySafeString(suggestedMovieTitle.ToString());
            

#if TESTINGNODISC
            currentDisc = new Disc();
            track dummyTrack = new track();
            dummyTrack.length = "1:55:00";
            dummyTrack.size = 1500;
            currentDisc.tracks.Add(dummyTrack);
            System.Threading.Thread.Sleep(7000);
#else
            var gettingDiscInfo = Task<string>.Factory.StartNew(() => getDetailedDiscInfo());
            await gettingDiscInfo;
                        
            ParseDiscInfo(gettingDiscInfo.Result.ToString());
            GetProbableTrack();
#endif



            grdTracks.ItemsSource = currentDisc.tracks;
            grdTracks.SelectedItem = selectedTrack;
            spReadingTrackInfo.Visibility = Visibility.Collapsed;
            dockTrackInfo.Visibility = Visibility.Visible;
            overlay.Visibility = Visibility.Collapsed;
            
            //setup the automatic timer
            timeTrackSelect = TimeSpan.FromSeconds(30);
            timerTrackSelect = new DispatcherTimer(new TimeSpan(0, 0, 1), DispatcherPriority.Normal, delegate
            {
                lblTrackSelectCountDown.Content = timeTrackSelect.ToString("c");
                if (timeTrackSelect == TimeSpan.Zero)
                {
                    //go to next step.
                    //addMovieToQueue();
                }
                timeTrackSelect = timeTrackSelect.Add(TimeSpan.FromSeconds(-1));
            }, Application.Current.Dispatcher);

            //timerTrackSelect.Start();

        }

        private void btnBrowseRipLocation_Click(object sender, RoutedEventArgs e)
        {
            if(timerTrackSelect != null)
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
        private void btnBrowseTvRipLocation_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            CommonFileDialogResult result = dialog.ShowDialog();


            if (result == CommonFileDialogResult.Ok)
            {
                txtTvRipLocation.Text = dialog.FileName;
                lblTvFullPath.Content = dialog.FileName + "\\" + getDirectorySafeString(selectedShow.name);
                                
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
        
        private void GoToEpisodeSelection()
        {
            tabControlMain.SelectedIndex = 2;
            lblSeasonNotFound.Visibility = Visibility.Collapsed;
            grdTvTracks.ItemsSource = null;
            string suggestedMovieBasePath = ConfigurationManager.AppSettings["pathToMediaForRip"];
            StringBuilder suggestedRipLocationTitle = new StringBuilder();
            suggestedRipLocationTitle.Append(selectedShow.name);
            
            txtTvRipLocation.Text = suggestedMovieBasePath;
            lblTvFullPath.Content = txtTvRipLocation.Text + "\\" + getDirectorySafeString(selectedShow.name);
        }

       

#region  ripping Disc
        private void btnAddDiscToQueue_Click(object sender, RoutedEventArgs e)
        {
            addMovieToQueue();
        }
        
        private void addMovieToQueue()
        {
            timerTrackSelect.Stop();

            if (grdTracks.SelectedItem != null)
            {
                string fullPath = lblFullPath.Content.ToString();

                StringBuilder suggestedMovieTitle = new StringBuilder();
                suggestedMovieTitle.Append(selectedMovieDetails.title);
                suggestedMovieTitle.Append(" (");
                suggestedMovieTitle.Append(selectedMovieDetails.release_date.Split('-')[0]);
                suggestedMovieTitle.Append(")");
                string title = getDirectorySafeString(suggestedMovieTitle.ToString());

                string selectedTrackIndex = grdTracks.SelectedIndex.ToString();
                track selectedTrack = (track)grdTracks.SelectedItem;
                QueuedItem toAdd = new QueuedItem(fullPath, title, selectedTrackIndex);
                toAdd.rippedMKVTitle = selectedTrack.title;
                queuedItems.Add(toAdd);
                waitingToRip.Add(toAdd);
                StartOver();
            }
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
                        //DiscRemoved(); // this is where you do your magic
                        break;
                    case deviceDetector.MediaInsertedNotification.DbtDevicearrival:
                        //DiscReady(); // this is where you do your magic
                        break;
                }
            }

            handled = false;
            return IntPtr.Zero;
        }

        private string discInfoHardCode = "MSG:1005,0,1,\"MakeMKV v1.10.7 win(x86-release) started\",\"%1 started\",\"MakeMKV v1.10.7 win(x86-release)\"\r\nDRV:0,2,999,1,\"BD-ROM HL-DT-ST BDDVDRW UH12LS29 1.00\",\"HAVANA_NIGHTS\",\"J:\"\r\nDRV:1,256,999,0,\"\",\"\",\"\"\r\nDRV:2,256,999,0,\"\",\"\",\"\"\r\nDRV:3,256,999,0,\"\",\"\",\"\"\r\nDRV:4,256,999,0,\"\",\"\",\"\"\r\nDRV:5,256,999,0,\"\",\"\",\"\"\r\nDRV:6,256,999,0,\"\",\"\",\"\"\r\nDRV:7,256,999,0,\"\",\"\",\"\"\r\nDRV:8,256,999,0,\"\",\"\",\"\"\r\nDRV:9,256,999,0,\"\",\"\",\"\"\r\nDRV:10,256,999,0,\"\",\"\",\"\"\r\nDRV:11,256,999,0,\"\",\"\",\"\"\r\nDRV:12,256,999,0,\"\",\"\",\"\"\r\nDRV:13,256,999,0,\"\",\"\",\"\"\r\nDRV:14,256,999,0,\"\",\"\",\"\"\r\nDRV:15,256,999,0,\"\",\"\",\"\"\r\nMSG:3007,0,0,\"Using direct disc access mode\",\"Using direct disc access mode\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,0,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #5 was added (1 cell(s), 0:02:33)\",\"Title #%1 was added (%2 cell(s), %3)\",\"5\",\"1\",\"0:02:33\"\r\nMSG:3025,0,3,\"Title #6 has length of 28 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"6\",\"28\",\"120\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,0,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,16777216,3,\"Title #7 was added (1 cell(s), 0:02:07)\",\"Title #%1 was added (%2 cell(s), %3)\",\"7\",\"1\",\"0:02:07\"\r\nMSG:3025,0,3,\"Title #8 has length of 72 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"8\",\"72\",\"120\"\r\nMSG:3025,0,3,\"Title #9 has length of 68 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"9\",\"68\",\"120\"\r\nMSG:3025,0,3,\"Title #10 has length of 30 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"10\",\"30\",\"120\"\r\nMSG:3025,0,3,\"Title #11 has length of 72 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"11\",\"72\",\"120\"\r\nMSG:3025,0,3,\"Title #12 has length of 45 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"12\",\"45\",\"120\"\r\nMSG:3025,0,3,\"Title #13 has length of 89 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"13\",\"89\",\"120\"\r\nMSG:3025,0,3,\"Title #14 has length of 76 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"14\",\"76\",\"120\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,0,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #15 was added (1 cell(s), 0:23:47)\",\"Title #%1 was added (%2 cell(s), %3)\",\"15\",\"1\",\"0:23:47\"\r\nMSG:3028,0,3,\"Title #16 was added (10 cell(s), 0:12:27)\",\"Title #%1 was added (%2 cell(s), %3)\",\"16\",\"10\",\"0:12:27\"\r\nMSG:3025,0,3,\"Title #17 has length of 16 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"17\",\"16\",\"120\"\r\nMSG:3028,0,3,\"Title #18 was added (22 cell(s), 1:25:57)\",\"Title #%1 was added (%2 cell(s), %3)\",\"18\",\"22\",\"1:25:57\"\r\nMSG:3025,0,3,\"Title #19 has length of 87 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"19\",\"87\",\"120\"\r\nMSG:3025,0,3,\"Title #20 has length of 88 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"20\",\"88\",\"120\"\r\nMSG:3025,0,3,\"Title #21 has length of 89 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"21\",\"89\",\"120\"\r\nMSG:3025,0,3,\"Title #22 has length of 90 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"22\",\"90\",\"120\"\r\nMSG:3028,0,3,\"Title #23 was added (2 cell(s), 0:04:25)\",\"Title #%1 was added (%2 cell(s), %3)\",\"23\",\"2\",\"0:04:25\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,16777216,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #24 was added (1 cell(s), 0:10:59)\",\"Title #%1 was added (%2 cell(s), %3)\",\"24\",\"1\",\"0:10:59\"\r\nMSG:3037,16777216,1,\"Cells 1-1 were removed from title start\",\"Cells 1-%1 were removed from title start\",\"1\"\r\nMSG:3038,16777216,2,\"Cells 3-3 were removed from title end\",\"Cells %1-%2 were removed from title end\",\"3\",\"3\"\r\nMSG:3028,0,3,\"Title #25 was added (1 cell(s), 0:03:18)\",\"Title #%1 was added (%2 cell(s), %3)\",\"25\",\"1\",\"0:03:18\"\r\nMSG:3025,0,3,\"Title #26 has length of 111 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"26\",\"111\",\"120\"\r\nMSG:3025,0,3,\"Title #27 has length of 16 seconds which is less than minimum title length of 120 seconds and was therefore skipped\",\"Title #%1 has length of %2 seconds which is less than minimum title length of %3 seconds and was therefore skipped\",\"27\",\"16\",\"120\"\r\nMSG:5011,0,0,\"Operation successfully completed\",\"Operation successfully completed\"\r\nTCOUNT:8\r\nCINFO:1,6206,\"DVD disc\"\r\nCINFO:2,0,\"HAVANA_NIGHTS\"\r\nCINFO:30,0,\"HAVANA_NIGHTS\"\r\nCINFO:31,6119,\"<b>Source information</b><br>\"\r\nCINFO:32,0,\"HAVANA_NIGHTS\"\r\nCINFO:33,0,\"0\"\r\nTINFO:0,9,0,\"0:02:33\"\r\nTINFO:0,10,0,\"95.6 MB\"\r\nTINFO:0,11,0,\"100253696\"\r\nTINFO:0,24,0,\"5\"\r\nTINFO:0,25,0,\"1\"\r\nTINFO:0,26,0,\"2\"\r\nTINFO:0,27,0,\"title00.mkv\"\r\nTINFO:0,30,0,\"95.6 MB\"\r\nTINFO:0,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:0,33,0,\"0\"\r\nSINFO:0,0,1,6201,\"Video\"\r\nSINFO:0,0,5,0,\"V_MPEG2\"\r\nSINFO:0,0,6,0,\"Mpeg2\"\r\nSINFO:0,0,7,0,\"Mpeg2\"\r\nSINFO:0,0,13,0,\"7.3 Mb/s\"\r\nSINFO:0,0,19,0,\"720x480\"\r\nSINFO:0,0,20,0,\"4:3\"\r\nSINFO:0,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:0,0,22,0,\"0\"\r\nSINFO:0,0,30,0,\"Mpeg2\"\r\nSINFO:0,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:0,0,33,0,\"0\"\r\nSINFO:0,0,38,0,\"\"\r\nSINFO:0,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:0,1,1,6202,\"Audio\"\r\nSINFO:0,1,2,5091,\"Stereo\"\r\nSINFO:0,1,3,0,\"eng\"\r\nSINFO:0,1,4,0,\"English\"\r\nSINFO:0,1,5,0,\"A_AC3\"\r\nSINFO:0,1,6,0,\"DD\"\r\nSINFO:0,1,7,0,\"Dolby Digital\"\r\nSINFO:0,1,13,0,\"192 Kb/s\"\r\nSINFO:0,1,14,0,\"2\"\r\nSINFO:0,1,17,0,\"48000\"\r\nSINFO:0,1,22,0,\"0\"\r\nSINFO:0,1,30,0,\"DD Stereo English\"\r\nSINFO:0,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:0,1,33,0,\"90\"\r\nSINFO:0,1,38,0,\"d\"\r\nSINFO:0,1,39,0,\"Default\"\r\nSINFO:0,1,40,0,\"stereo\"\r\nSINFO:0,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:1,9,0,\"0:02:07\"\r\nTINFO:1,10,0,\"78.5 MB\"\r\nTINFO:1,11,0,\"82407424\"\r\nTINFO:1,24,0,\"7\"\r\nTINFO:1,25,0,\"1\"\r\nTINFO:1,26,0,\"2\"\r\nTINFO:1,27,0,\"title01.mkv\"\r\nTINFO:1,30,0,\"78.5 MB\"\r\nTINFO:1,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:1,33,0,\"0\"\r\nSINFO:1,0,1,6201,\"Video\"\r\nSINFO:1,0,5,0,\"V_MPEG2\"\r\nSINFO:1,0,6,0,\"Mpeg2\"\r\nSINFO:1,0,7,0,\"Mpeg2\"\r\nSINFO:1,0,13,0,\"7.3 Mb/s\"\r\nSINFO:1,0,19,0,\"720x480\"\r\nSINFO:1,0,20,0,\"4:3\"\r\nSINFO:1,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:1,0,22,0,\"0\"\r\nSINFO:1,0,30,0,\"Mpeg2\"\r\nSINFO:1,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:1,0,33,0,\"0\"\r\nSINFO:1,0,38,0,\"\"\r\nSINFO:1,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:1,1,1,6202,\"Audio\"\r\nSINFO:1,1,2,5091,\"Stereo\"\r\nSINFO:1,1,3,0,\"eng\"\r\nSINFO:1,1,4,0,\"English\"\r\nSINFO:1,1,5,0,\"A_AC3\"\r\nSINFO:1,1,6,0,\"DD\"\r\nSINFO:1,1,7,0,\"Dolby Digital\"\r\nSINFO:1,1,13,0,\"192 Kb/s\"\r\nSINFO:1,1,14,0,\"2\"\r\nSINFO:1,1,17,0,\"48000\"\r\nSINFO:1,1,22,0,\"0\"\r\nSINFO:1,1,30,0,\"DD Stereo English\"\r\nSINFO:1,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:1,1,33,0,\"90\"\r\nSINFO:1,1,38,0,\"d\"\r\nSINFO:1,1,39,0,\"Default\"\r\nSINFO:1,1,40,0,\"stereo\"\r\nSINFO:1,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:2,9,0,\"0:23:47\"\r\nTINFO:2,10,0,\"909.4 MB\"\r\nTINFO:2,11,0,\"953585664\"\r\nTINFO:2,24,0,\"15\"\r\nTINFO:2,25,0,\"1\"\r\nTINFO:2,26,0,\"2\"\r\nTINFO:2,27,0,\"title02.mkv\"\r\nTINFO:2,30,0,\"909.4 MB\"\r\nTINFO:2,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:2,33,0,\"0\"\r\nSINFO:2,0,1,6201,\"Video\"\r\nSINFO:2,0,5,0,\"V_MPEG2\"\r\nSINFO:2,0,6,0,\"Mpeg2\"\r\nSINFO:2,0,7,0,\"Mpeg2\"\r\nSINFO:2,0,13,0,\"7.3 Mb/s\"\r\nSINFO:2,0,19,0,\"720x480\"\r\nSINFO:2,0,20,0,\"4:3\"\r\nSINFO:2,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:2,0,22,0,\"0\"\r\nSINFO:2,0,30,0,\"Mpeg2\"\r\nSINFO:2,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:2,0,33,0,\"0\"\r\nSINFO:2,0,38,0,\"\"\r\nSINFO:2,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:2,1,1,6202,\"Audio\"\r\nSINFO:2,1,2,5091,\"Stereo\"\r\nSINFO:2,1,3,0,\"eng\"\r\nSINFO:2,1,4,0,\"English\"\r\nSINFO:2,1,5,0,\"A_AC3\"\r\nSINFO:2,1,6,0,\"DD\"\r\nSINFO:2,1,7,0,\"Dolby Digital\"\r\nSINFO:2,1,13,0,\"192 Kb/s\"\r\nSINFO:2,1,14,0,\"2\"\r\nSINFO:2,1,17,0,\"48000\"\r\nSINFO:2,1,22,0,\"0\"\r\nSINFO:2,1,30,0,\"DD Stereo English\"\r\nSINFO:2,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:2,1,33,0,\"90\"\r\nSINFO:2,1,38,0,\"d\"\r\nSINFO:2,1,39,0,\"Default\"\r\nSINFO:2,1,40,0,\"stereo\"\r\nSINFO:2,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:3,8,0,\"10\"\r\nTINFO:3,9,0,\"0:12:27\"\r\nTINFO:3,10,0,\"455.8 MB\"\r\nTINFO:3,11,0,\"477952000\"\r\nTINFO:3,24,0,\"16\"\r\nTINFO:3,25,0,\"10\"\r\nTINFO:3,26,0,\"2,3,4,5,6,7,8,9,10,11\"\r\nTINFO:3,27,0,\"title03.mkv\"\r\nTINFO:3,30,0,\"10 chapter(s) , 455.8 MB\"\r\nTINFO:3,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:3,33,0,\"0\"\r\nSINFO:3,0,1,6201,\"Video\"\r\nSINFO:3,0,5,0,\"V_MPEG2\"\r\nSINFO:3,0,6,0,\"Mpeg2\"\r\nSINFO:3,0,7,0,\"Mpeg2\"\r\nSINFO:3,0,13,0,\"7.3 Mb/s\"\r\nSINFO:3,0,19,0,\"720x480\"\r\nSINFO:3,0,20,0,\"4:3\"\r\nSINFO:3,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:3,0,22,0,\"0\"\r\nSINFO:3,0,30,0,\"Mpeg2\"\r\nSINFO:3,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:3,0,33,0,\"0\"\r\nSINFO:3,0,38,0,\"\"\r\nSINFO:3,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:3,1,1,6202,\"Audio\"\r\nSINFO:3,1,2,5091,\"Stereo\"\r\nSINFO:3,1,3,0,\"eng\"\r\nSINFO:3,1,4,0,\"English\"\r\nSINFO:3,1,5,0,\"A_AC3\"\r\nSINFO:3,1,6,0,\"DD\"\r\nSINFO:3,1,7,0,\"Dolby Digital\"\r\nSINFO:3,1,13,0,\"192 Kb/s\"\r\nSINFO:3,1,14,0,\"2\"\r\nSINFO:3,1,17,0,\"48000\"\r\nSINFO:3,1,22,0,\"0\"\r\nSINFO:3,1,30,0,\"DD Stereo English\"\r\nSINFO:3,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:3,1,33,0,\"90\"\r\nSINFO:3,1,38,0,\"d\"\r\nSINFO:3,1,39,0,\"Default\"\r\nSINFO:3,1,40,0,\"stereo\"\r\nSINFO:3,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:4,8,0,\"20\"\r\nTINFO:4,9,0,\"1:25:57\"\r\nTINFO:4,10,0,\"3.8 GB\"\r\nTINFO:4,11,0,\"4159121408\"\r\nTINFO:4,24,0,\"18\"\r\nTINFO:4,25,0,\"2\"\r\nTINFO:4,26,0,\"2-14,15-23\"\r\nTINFO:4,27,0,\"title04.mkv\"\r\nTINFO:4,30,0,\"20 chapter(s) , 3.8 GB\"\r\nTINFO:4,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:4,33,0,\"0\"\r\nSINFO:4,0,1,6201,\"Video\"\r\nSINFO:4,0,5,0,\"V_MPEG2\"\r\nSINFO:4,0,6,0,\"Mpeg2\"\r\nSINFO:4,0,7,0,\"Mpeg2\"\r\nSINFO:4,0,13,0,\"7.3 Mb/s\"\r\nSINFO:4,0,19,0,\"720x480\"\r\nSINFO:4,0,20,0,\"16:9\"\r\nSINFO:4,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:4,0,22,0,\"0\"\r\nSINFO:4,0,30,0,\"Mpeg2\"\r\nSINFO:4,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,0,33,0,\"0\"\r\nSINFO:4,0,38,0,\"\"\r\nSINFO:4,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,1,1,6202,\"Audio\"\r\nSINFO:4,1,2,0,\"Surround 5.1\"\r\nSINFO:4,1,3,0,\"eng\"\r\nSINFO:4,1,4,0,\"English\"\r\nSINFO:4,1,5,0,\"A_AC3\"\r\nSINFO:4,1,6,0,\"DD\"\r\nSINFO:4,1,7,0,\"Dolby Digital\"\r\nSINFO:4,1,13,0,\"448 Kb/s\"\r\nSINFO:4,1,14,0,\"6\"\r\nSINFO:4,1,17,0,\"48000\"\r\nSINFO:4,1,22,0,\"0\"\r\nSINFO:4,1,30,0,\"DD Surround 5.1 English\"\r\nSINFO:4,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,1,33,0,\"90\"\r\nSINFO:4,1,38,0,\"d\"\r\nSINFO:4,1,39,0,\"Default\"\r\nSINFO:4,1,40,0,\"5.1(side)\"\r\nSINFO:4,1,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,2,1,6202,\"Audio\"\r\nSINFO:4,2,2,5091,\"Stereo\"\r\nSINFO:4,2,3,0,\"eng\"\r\nSINFO:4,2,4,0,\"English\"\r\nSINFO:4,2,5,0,\"A_AC3\"\r\nSINFO:4,2,6,0,\"DD\"\r\nSINFO:4,2,7,0,\"Dolby Digital\"\r\nSINFO:4,2,13,0,\"192 Kb/s\"\r\nSINFO:4,2,14,0,\"2\"\r\nSINFO:4,2,17,0,\"48000\"\r\nSINFO:4,2,22,0,\"0\"\r\nSINFO:4,2,30,0,\"DD Stereo English\"\r\nSINFO:4,2,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,2,33,0,\"90\"\r\nSINFO:4,2,38,0,\"\"\r\nSINFO:4,2,40,0,\"stereo\"\r\nSINFO:4,2,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,3,1,6202,\"Audio\"\r\nSINFO:4,3,2,5091,\"Stereo\"\r\nSINFO:4,3,3,0,\"eng\"\r\nSINFO:4,3,4,0,\"English\"\r\nSINFO:4,3,5,0,\"A_AC3\"\r\nSINFO:4,3,6,0,\"DD\"\r\nSINFO:4,3,7,0,\"Dolby Digital\"\r\nSINFO:4,3,13,0,\"192 Kb/s\"\r\nSINFO:4,3,14,0,\"2\"\r\nSINFO:4,3,17,0,\"48000\"\r\nSINFO:4,3,22,0,\"1\"\r\nSINFO:4,3,30,0,\"DD Stereo English\"\r\nSINFO:4,3,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,3,33,0,\"90\"\r\nSINFO:4,3,38,0,\"\"\r\nSINFO:4,3,40,0,\"stereo\"\r\nSINFO:4,3,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,4,1,6203,\"Subtitles\"\r\nSINFO:4,4,3,0,\"eng\"\r\nSINFO:4,4,4,0,\"English\"\r\nSINFO:4,4,5,0,\"S_VOBSUB\"\r\nSINFO:4,4,6,0,\"\"\r\nSINFO:4,4,7,0,\"Dvd Subtitles\"\r\nSINFO:4,4,22,0,\"0\"\r\nSINFO:4,4,30,0,\" English\"\r\nSINFO:4,4,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,4,33,0,\"90\"\r\nSINFO:4,4,38,0,\"d\"\r\nSINFO:4,4,39,0,\"Default\"\r\nSINFO:4,4,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,5,1,6203,\"Subtitles\"\r\nSINFO:4,5,3,0,\"spa\"\r\nSINFO:4,5,4,0,\"Spanish\"\r\nSINFO:4,5,5,0,\"S_VOBSUB\"\r\nSINFO:4,5,6,0,\"\"\r\nSINFO:4,5,7,0,\"Dvd Subtitles\"\r\nSINFO:4,5,22,0,\"0\"\r\nSINFO:4,5,30,0,\" Spanish\"\r\nSINFO:4,5,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,5,33,0,\"90\"\r\nSINFO:4,5,38,0,\"\"\r\nSINFO:4,5,42,5088,\"( Lossless conversion )\"\r\nSINFO:4,6,1,6203,\"Subtitles\"\r\nSINFO:4,6,3,0,\"eng\"\r\nSINFO:4,6,4,0,\"English\"\r\nSINFO:4,6,5,0,\"S_VOBSUB\"\r\nSINFO:4,6,6,0,\"\"\r\nSINFO:4,6,7,0,\"Dvd Subtitles\"\r\nSINFO:4,6,22,0,\"0\"\r\nSINFO:4,6,30,0,\" English\"\r\nSINFO:4,6,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:4,6,33,0,\"90\"\r\nSINFO:4,6,38,0,\"\"\r\nSINFO:4,6,42,5088,\"( Lossless conversion )\"\r\nTINFO:5,8,0,\"2\"\r\nTINFO:5,9,0,\"0:04:25\"\r\nTINFO:5,10,0,\"163.2 MB\"\r\nTINFO:5,11,0,\"171163648\"\r\nTINFO:5,24,0,\"23\"\r\nTINFO:5,25,0,\"2\"\r\nTINFO:5,26,0,\"2,3\"\r\nTINFO:5,27,0,\"title05.mkv\"\r\nTINFO:5,30,0,\"2 chapter(s) , 163.2 MB\"\r\nTINFO:5,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:5,33,0,\"0\"\r\nSINFO:5,0,1,6201,\"Video\"\r\nSINFO:5,0,5,0,\"V_MPEG2\"\r\nSINFO:5,0,6,0,\"Mpeg2\"\r\nSINFO:5,0,7,0,\"Mpeg2\"\r\nSINFO:5,0,13,0,\"7.5 Mb/s\"\r\nSINFO:5,0,19,0,\"720x480\"\r\nSINFO:5,0,20,0,\"16:9\"\r\nSINFO:5,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:5,0,22,0,\"0\"\r\nSINFO:5,0,30,0,\"Mpeg2\"\r\nSINFO:5,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:5,0,33,0,\"0\"\r\nSINFO:5,0,38,0,\"\"\r\nSINFO:5,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:5,1,1,6202,\"Audio\"\r\nSINFO:5,1,2,5091,\"Stereo\"\r\nSINFO:5,1,3,0,\"eng\"\r\nSINFO:5,1,4,0,\"English\"\r\nSINFO:5,1,5,0,\"A_AC3\"\r\nSINFO:5,1,6,0,\"DD\"\r\nSINFO:5,1,7,0,\"Dolby Digital\"\r\nSINFO:5,1,13,0,\"192 Kb/s\"\r\nSINFO:5,1,14,0,\"2\"\r\nSINFO:5,1,17,0,\"48000\"\r\nSINFO:5,1,22,0,\"0\"\r\nSINFO:5,1,30,0,\"DD Stereo English\"\r\nSINFO:5,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:5,1,33,0,\"90\"\r\nSINFO:5,1,38,0,\"d\"\r\nSINFO:5,1,39,0,\"Default\"\r\nSINFO:5,1,40,0,\"stereo\"\r\nSINFO:5,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:6,9,0,\"0:10:59\"\r\nTINFO:6,10,0,\"424.0 MB\"\r\nTINFO:6,11,0,\"444620800\"\r\nTINFO:6,24,0,\"24\"\r\nTINFO:6,25,0,\"1\"\r\nTINFO:6,26,0,\"2\"\r\nTINFO:6,27,0,\"title06.mkv\"\r\nTINFO:6,30,0,\"424.0 MB\"\r\nTINFO:6,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:6,33,0,\"0\"\r\nSINFO:6,0,1,6201,\"Video\"\r\nSINFO:6,0,5,0,\"V_MPEG2\"\r\nSINFO:6,0,6,0,\"Mpeg2\"\r\nSINFO:6,0,7,0,\"Mpeg2\"\r\nSINFO:6,0,13,0,\"7.3 Mb/s\"\r\nSINFO:6,0,19,0,\"720x480\"\r\nSINFO:6,0,20,0,\"4:3\"\r\nSINFO:6,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:6,0,22,0,\"0\"\r\nSINFO:6,0,30,0,\"Mpeg2\"\r\nSINFO:6,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:6,0,33,0,\"0\"\r\nSINFO:6,0,38,0,\"\"\r\nSINFO:6,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:6,1,1,6202,\"Audio\"\r\nSINFO:6,1,2,5091,\"Stereo\"\r\nSINFO:6,1,3,0,\"eng\"\r\nSINFO:6,1,4,0,\"English\"\r\nSINFO:6,1,5,0,\"A_AC3\"\r\nSINFO:6,1,6,0,\"DD\"\r\nSINFO:6,1,7,0,\"Dolby Digital\"\r\nSINFO:6,1,13,0,\"192 Kb/s\"\r\nSINFO:6,1,14,0,\"2\"\r\nSINFO:6,1,17,0,\"48000\"\r\nSINFO:6,1,22,0,\"0\"\r\nSINFO:6,1,30,0,\"DD Stereo English\"\r\nSINFO:6,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:6,1,33,0,\"90\"\r\nSINFO:6,1,38,0,\"d\"\r\nSINFO:6,1,39,0,\"Default\"\r\nSINFO:6,1,40,0,\"stereo\"\r\nSINFO:6,1,42,5088,\"( Lossless conversion )\"\r\nTINFO:7,9,0,\"0:03:18\"\r\nTINFO:7,10,0,\"141.3 MB\"\r\nTINFO:7,11,0,\"148174848\"\r\nTINFO:7,24,0,\"25\"\r\nTINFO:7,25,0,\"1\"\r\nTINFO:7,26,0,\"2\"\r\nTINFO:7,27,0,\"title07.mkv\"\r\nTINFO:7,30,0,\"141.3 MB\"\r\nTINFO:7,31,6120,\"<b>Title information</b><br>\"\r\nTINFO:7,33,0,\"0\"\r\nSINFO:7,0,1,6201,\"Video\"\r\nSINFO:7,0,5,0,\"V_MPEG2\"\r\nSINFO:7,0,6,0,\"Mpeg2\"\r\nSINFO:7,0,7,0,\"Mpeg2\"\r\nSINFO:7,0,13,0,\"7.9 Mb/s\"\r\nSINFO:7,0,19,0,\"720x480\"\r\nSINFO:7,0,20,0,\"4:3\"\r\nSINFO:7,0,21,0,\"29.97 (30000/1001)\"\r\nSINFO:7,0,22,0,\"0\"\r\nSINFO:7,0,30,0,\"Mpeg2\"\r\nSINFO:7,0,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:7,0,33,0,\"0\"\r\nSINFO:7,0,38,0,\"\"\r\nSINFO:7,0,42,5088,\"( Lossless conversion )\"\r\nSINFO:7,1,1,6202,\"Audio\"\r\nSINFO:7,1,2,5091,\"Stereo\"\r\nSINFO:7,1,3,0,\"eng\"\r\nSINFO:7,1,4,0,\"English\"\r\nSINFO:7,1,5,0,\"A_AC3\"\r\nSINFO:7,1,6,0,\"DD\"\r\nSINFO:7,1,7,0,\"Dolby Digital\"\r\nSINFO:7,1,13,0,\"192 Kb/s\"\r\nSINFO:7,1,14,0,\"2\"\r\nSINFO:7,1,17,0,\"48000\"\r\nSINFO:7,1,22,0,\"0\"\r\nSINFO:7,1,30,0,\"DD Stereo English\"\r\nSINFO:7,1,31,6121,\"<b>Track information</b><br>\"\r\nSINFO:7,1,33,0,\"90\"\r\nSINFO:7,1,38,0,\"d\"\r\nSINFO:7,1,39,0,\"Default\"\r\nSINFO:7,1,40,0,\"stereo\"\r\nSINFO:7,1,42,5088,\"( Lossless conversion )\"\r\n";






#endregion

#region Methods for Disc reading/parsing
        private string getHighLevelDiscInfo()
        {
#if TESTINGNODISC
            System.Threading.Thread.Sleep(5000);
            return "The Big Lebowski";
#else
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
#endif
        }

        private String getDetailedDiscInfo()
        {
            StringBuilder detailedDiscInfoOutput;
            //////hardcoded return disc info
            //return discInfoHardCode;
            try
            {
                // Start the child process.
                Process p = new Process();
                detailedDiscInfoOutput = new StringBuilder();
                // Redirect the output stream of the child process.
                p.StartInfo.UseShellExecute = false;
                p.OutputDataReceived += new DataReceivedEventHandler
                    (
                        delegate (object sender, DataReceivedEventArgs e)
                        {
                            // append the new data to the data already read-in
                            detailedDiscInfoOutput.Append(e.Data);
                        }
                    );
                //p.OutputDataReceived += (sender, args) => Debug.WriteLine("received output: {0}", args.Data);
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.FileName = System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + "\\MakeMKV\\makemkvcon64.exe";
                p.StartInfo.Arguments = "-r info disc:0";
                p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                //p.BeginOutputReadLine();
                string output =  p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                //p.CancelOutputRead();
                //string output = detailedDiscInfoOutput.ToString();
                if(output.Contains("This application version is too old"))
                {
                    throw new MakeMKVUpdateAvailableException("updateAvailable", new Exception());
                }
                //TODO: Zach
                //if output contains - then notify but dont fail
                //"available for download"
                return output;
            } catch (MakeMKVUpdateAvailableException ex)
            {

                MessageBox.Show("Please download the new version of MakeMKV and apply an updated license key. The application will launch these sites, please shut down and reinstall MakeMKV", "Can not continue",
                    MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
                
                System.Diagnostics.Process.Start("https://www.makemkv.com/download/");
                System.Diagnostics.Process.Start("https://www.makemkv.com/forum2/viewtopic.php?f=5&t=1053");

                notifier.Notify(mqttNotifyState.updateMKV, "Update Make MKV to latest version");

            }
            catch (Exception ex)
            {
                notifier.Notify(mqttNotifyState.generic, ex.InnerException.ToString());
            }
            return "";
            
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
                        case "27":
                            currentTrack.title = value;
                            break;
                        default:
                            break;
                    }

                }


            }
            //add the final track to the disc
            currentDisc.tracks.Add(currentTrack);

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

#region support service queries

        private tmdbSeasonSearchResult GetTvSeasonResults(string seasonNumber, int showId)
        {
            StringBuilder requestUrl = new StringBuilder();
            //https://api.themoviedb.org/3/tv/688/season/1?api_key=1abe04137ccd4fa521cb5f8e337b9418&language=en-US
            requestUrl.Append("https://api.themoviedb.org/3/tv/");
            requestUrl.Append(showId.ToString());
            requestUrl.Append("/season/");
            requestUrl.Append(seasonNumber);
            requestUrl.Append("?api_key=");
            requestUrl.Append(tmdbApiKey);
            requestUrl.Append("&language=en-US");
            Uri requestUri = new Uri(requestUrl.ToString());
            WebRequest request = WebRequest.Create(requestUri.ToString());
            try
            {
                WebResponse response = request.GetResponse();
                Stream dataStream = response.GetResponseStream();
                StreamReader reader = new StreamReader(dataStream);
                string text = reader.ReadToEnd();
                tmdbSeasonSearchResult searchResult = JsonConvert.DeserializeObject<tmdbSeasonSearchResult>(text);
                return searchResult;
            }
            catch (Exception e)
            {

                throw e;
            }
            
        }

        private tmdbTVSearchResult GetTvSearchResults()
        {
            if (discName != null && discName.Length > 0)
            {
                StringBuilder requestUrl = new StringBuilder();
                //https://api.themoviedb.org/3/search/tv?api_key=1abe04137ccd4fa521cb5f8e337b9418&language=en-US&query=The%20West%20Wing&page=1
                requestUrl.Append("https://api.themoviedb.org/3/search/tv?api_key=");
                requestUrl.Append(tmdbApiKey);
                requestUrl.Append("&language=en-US&query=");
                requestUrl.Append(discName);
                requestUrl.Append("&page=1");
                Uri requestUri = new Uri(requestUrl.ToString());
                WebRequest request = WebRequest.Create(requestUri.ToString());

                WebResponse response = request.GetResponse();
                Stream dataStream = response.GetResponseStream();
                StreamReader reader = new StreamReader(dataStream);
                string text = reader.ReadToEnd();
                tmdbTVSearchResult searchResult = JsonConvert.DeserializeObject<tmdbTVSearchResult>(text);
                return searchResult;
            }
            else return null;
        }

        private tmdbMovieSearchResult GetMovieSearchResults()
        {
            if (discName != null && discName.Length > 0)
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
                tmdbMovieSearchResult searchResult = JsonConvert.DeserializeObject<tmdbMovieSearchResult>(text);
                return searchResult;
            }
            else return null;
        }

        private void getDetailedMovieInfo(tmdbMovieResult _selectedTitle)
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

#region High Level App Activities (Add/Remove Disc)
        
        private void DiscRemoved()
        {
            StartOver();
        }

        private void StartOver()
        {
            chkIsTV.IsChecked = false;
            txtTitleSearch.Text = "";
            grdTvSearchResults.ItemsSource = null;
            grdMovieSearchResults.ItemsSource = null;
            imgSelectedItemPoster.Source = null;
            lblSelectedTitle.Content = "";
            lblSelectedDescription.Text = "";
            tabControlMain.SelectedIndex = 0;
            discIsTv = false;
            discIsBlueRay = false;
            discName = "";
            selectedMovie = null;
            selectedShow = null;
            selectedEpisode = null;
            matchingMovies = new ObservableCollection<tmdbMovieResult>();
            matchingShows = new ObservableCollection<tmdbTvShowResult>();
            selectedMovieDetails = null;
            selectedTrack = null;
            currentDisc = null;
            fullPathToCompressedMkv = "";
            fullPathToCompressedMkv = "";
            if (timerDiscSelect != null)
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
            var gettingHighLevelDiscInfo = Task<string>.Factory.StartNew(() => getHighLevelDiscInfo());
            overlay.Visibility = Visibility.Visible;
            await gettingHighLevelDiscInfo;

            discName = gettingHighLevelDiscInfo.Result.ToString();
            overlay.Visibility = Visibility.Collapsed;

            goToSelectTitle();

        }
        #endregion

        private void btnClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            List<QueuedItem> toRemove = new List<QueuedItem>();
            foreach (QueuedItem item in queuedItems)
            {
                if (item.removed)
                {
                    toRemove.Add(item);
                }
            }

            foreach(QueuedItem removeMe in toRemove)
            {
                queuedItems.Remove(removeMe);
            }
        }

        private void RipHyperlink_Click(object sender, RoutedEventArgs e)
        {
           QueuedItem thisItem = (QueuedItem)((Hyperlink)sender).DataContext;
            //System.Diagnostics.Process.Start(thisItem.failedRipTextFile);
        }

        private void CompressHyperlink_Click(object sender, RoutedEventArgs e)
        {
            QueuedItem thisItem = (QueuedItem)((Hyperlink)sender).DataContext;
            //System.Diagnostics.Process.Start(thisItem.failedCompressTextFile);
            string windowTitle = "Compression failed: " + thisItem.title;
            var errorWindow = new MessageDisplay(windowTitle, thisItem.failedCompressText.ToString());
            errorWindow.ShowDialog();
        }

    }


  


}


