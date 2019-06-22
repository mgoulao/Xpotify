﻿using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Xpotify.Classes;
using Xpotify.Classes.Model;
using Xpotify.Helpers;
using Xpotify.SpotifyApi;
using XpotifyWebAgent;
using XpotifyWebAgent.Model;

namespace Xpotify.Controls
{
    public sealed partial class XpotifyWebApp : UserControl
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public enum XpotifyWebAppActionRequest
        {
            OpenSettingsFlyout,
            OpenAboutFlyout,
            OpenDonateFlyout,
            GoToCompactOverlay,
            GoToNowPlaying,
            ShowSplashScreen,
        }

        public event EventHandler<EventArgs> PageLoaded;
        public event EventHandler<EventArgs> WebAppLoaded;
        public event EventHandler<XpotifyWebAppActionRequest> ActionRequested;

        public AutoPlayAction AutoPlayAction { get; set; } = AutoPlayAction.None;
        public WebViewController Controller { get; }
        public bool BackEnabled { get; private set; } = false;

        #region Custom Properties
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            "IsOpen", typeof(bool), typeof(XpotifyWebApp), new PropertyMetadata(defaultValue: false,
                propertyChangedCallback: new PropertyChangedCallback(OnSplashStatePropertyChanged)));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set
            {
                if (IsOpen != value)
                    SetValue(IsOpenProperty, value);

                this.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                // TODO: Hide all html content via javascript to reduce CPU usage
            }
        }

        private static void OnSplashStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as NowPlayingView).IsOpen = (bool)e.NewValue;
        }
        #endregion

        private Uri loadFailedUrl;
        private string webViewPreviousUri = "";
        private LocalStoragePlayback initialPlaybackState;
        private DispatcherTimer webViewCheckTimer, stuckDetectTimer;
        private string prevCurrentPlaying;
        private int stuckDetectCounter = 0;
        private DateTime lastStuckFixApiCall;
        private XpotifyWebAgent.XpotifyWebAgent xpotifyWebAgent;

        public XpotifyWebApp()
        {
            this.InitializeComponent();

            Controller = new WebViewController(this.mainWebView);
            PlaybackActionHelper.SetController(Controller);

            loadFailedAppVersionText.Text = PackageHelper.GetAppVersionString();

            xpotifyWebAgent = new XpotifyWebAgent.XpotifyWebAgent();
            xpotifyWebAgent.ProgressBarCommandReceived += XpotifyWebAgent_ProgressBarCommandReceived;
            xpotifyWebAgent.StatusReportReceived += XpotifyWebAgent_StatusReportReceived;

            webViewCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            webViewCheckTimer.Tick += WebViewCheckTimer_Tick;
            webViewCheckTimer.Start();

            stuckDetectTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4),
            };
            stuckDetectTimer.Tick += StuckDetectTimer_Tick;
            stuckDetectTimer.Start();

            VisualStateManager.GoToState(this, nameof(DefaultVisualState), false);
        }

        private void XpotifyWebAgent_StatusReportReceived(object sender, StatusReportReceivedEventArgs e)
        {
            logger.Trace("StatusReport Received.");

            BackEnabled = e.Status.BackButtonEnabled;
            PlayStatusTracker.LocalPlaybackDataReceived(e.Status.NowPlaying);
        }

        private void XpotifyWebAgent_ProgressBarCommandReceived(object sender, ProgressBarCommandEventArgs e)
        {
            if (e.Command == ProgressBarCommand.Show)
            {
                playbackBarProgressBar.Margin = new Thickness(e.Left * mainWebView.ActualWidth, e.Top * mainWebView.ActualHeight, 0, 0);
                playbackBarProgressBar.Width = e.Width * mainWebView.ActualWidth;
                playbackBarProgressBar.Visibility = Visibility.Visible;
            }
            else
            {
                playbackBarProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void MainWebView_NavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            if (e.Uri.ToString().StartsWith(Authorization.RedirectUri))
            {
                FinalizeAuthorization(e.Uri.ToString());
                return;
            }

            VisualStateManager.GoToState(this, nameof(LoadFailedVisualState), false);
            PageLoaded?.Invoke(this, new EventArgs());
            loadFailedUrlText.Text = e.Uri.ToString();
            loadFailedUrl = e.Uri;
            errorMessageText.Text = e.WebErrorStatus.ToString();
        }

        private async void MainWebView_LoadCompleted(object sender, NavigationEventArgs e)
        {
            VisualStateManager.GoToState(this, nameof(DefaultVisualState), false);

            if (e.Uri.ToString().StartsWith(Authorization.SpotifyLoginUri) && LocalConfiguration.IsLoggedInByFacebook)
            {
                if (await Controller.TryPushingFacebookLoginButton())
                {
                    logger.Info("Pushed the facebook login button.");
                    return;
                }
            }

            if (e.Uri.ToString().StartsWith("https://open.spotify.com/static/offline.html?redirectUrl="))
            {
                var url = e.Uri.ToString();

                logger.Info("Clearing local storage and redirecting...");
                var result = await Controller.ClearPlaybackLocalStorage();

                try
                {
                    if (result.Length > 0)
                    {
                        initialPlaybackState = JsonConvert.DeserializeObject<LocalStoragePlayback>(result);
                        logger.Info("initial playback volume = " + initialPlaybackState.volume);
                    }
                    else
                    {
                        logger.Info("localStorage.playback was undefined.");
                    }
                }
                catch
                {
                    logger.Warn("Decoding localStorage.playback failed.");
                    logger.Info("localStorage.playback content was: " + result);
                }

                var urlDecoder = new WwwFormUrlDecoder(url.Substring(url.IndexOf('?') + 1));
                Controller.Navigate(new Uri(urlDecoder.GetFirstValueByName("redirectUrl")));

                return;
            }

            if (e.Uri.ToString().ToLower().Contains(WebViewController.SpotifyPwaUrlBeginsWith.ToLower()))
            {
                var justInjected = await Controller.InjectInitScript(ThemeHelper.GetCurrentTheme() == Theme.Light);
                if (justInjected)
                {
                    SetInitialPlaybackState();
                    PlayStatusTracker.StartRegularRefresh();
                }

                if (AutoPlayAction != AutoPlayAction.None)
                {
                    AutoPlayOnStartup(AutoPlayAction);
                    AutoPlayAction = AutoPlayAction.None;
                }
            }

            if (e.Uri.ToString().StartsWith(Authorization.RedirectUri))
                FinalizeAuthorization(e.Uri.ToString());
            else if (e.Uri.ToString().ToLower().Contains(WebViewController.SpotifyPwaUrlBeginsWith.ToLower()))
                WebAppLoaded?.Invoke(this, new EventArgs());
            else
                PageLoaded?.Invoke(this, new EventArgs());

            if (!await Controller.CheckLoggedIn())
            {
                Authorize("https://accounts.spotify.com/login?continue=https%3A%2F%2Fopen.spotify.com%2F", clearExisting: true);
                AnalyticsHelper.Log("mainEvent", "notLoggedIn");
            }
        }

        private async void AutoPlayOnStartup(AutoPlayAction autoPlayAction)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            logger.Info("AutoPlay " + autoPlayAction);
            await Controller.AutoPlay(autoPlayAction);
        }

        private async void MainWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs e)
        {
            logger.Info("Page: " + e.Uri.ToString());

            if (e.Uri.ToString().EndsWith("#xpotifysettings"))
            {
                e.Cancel = true;
                ActionRequested?.Invoke(this, XpotifyWebAppActionRequest.OpenSettingsFlyout);
            }
            else if (e.Uri.ToString().EndsWith("#xpotifyabout"))
            {
                e.Cancel = true;
                ActionRequested?.Invoke(this, XpotifyWebAppActionRequest.OpenAboutFlyout);
            }
            else if (e.Uri.ToString().EndsWith("#xpotifydonate"))
            {
                e.Cancel = true;
                ActionRequested?.Invoke(this, XpotifyWebAppActionRequest.OpenDonateFlyout);
            }
            else if (e.Uri.ToString().EndsWith("#xpotifypintostart"))
            {
                e.Cancel = true;

                await PinPageToStart();
                AnalyticsHelper.Log("mainEvent", "pinToStart");
            }
            else if (e.Uri.ToString().EndsWith("#xpotifyCompactOverlay"))
            {
                e.Cancel = true;

                ActionRequested?.Invoke(this, XpotifyWebAppActionRequest.GoToCompactOverlay);
            }
            else if (e.Uri.ToString().EndsWith("#xpotifyNowPlaying"))
            {
                e.Cancel = true;

                ActionRequested?.Invoke(this, XpotifyWebAppActionRequest.GoToNowPlaying);
            }
            else if (e.Uri.ToString().EndsWith("#xpotifyInitialPage"))
            {
            }
            else if (e.Uri.ToString().ToLower().Contains(WebViewController.SpotifyPwaUrlBeginsWith.ToLower()))
            {
                mainWebView.AddWebAllowedObject("Xpotify", xpotifyWebAgent);
            }
            else
            {
                if (!webViewPreviousUri.ToLower().StartsWith(WebViewController.SpotifyPwaUrlBeginsWith.ToLower())
                    || !e.Uri.ToString().ToLower().StartsWith(WebViewController.SpotifyPwaUrlBeginsWith.ToLower()))
                {
                    // Open splash screen, unless both new and old uris are in open.spotify.com itself.
                    ActionRequested?.Invoke(this, XpotifyWebAppActionRequest.ShowSplashScreen);
                }
            }

            if (e.Uri.ToString().StartsWith(Authorization.FacebookLoginFinishRedirectUri))
            {
                logger.Info("Logged in by Facebook.");
                LocalConfiguration.IsLoggedInByFacebook = true;
            }

            webViewPreviousUri = e.Uri.ToString();
        }

        private async Task PinPageToStart()
        {
            VisualStateManager.GoToState(this, nameof(WaitingVisualState), false);

            var pageUrl = await Controller.GetPageUrl();
            var pageTitle = await Controller.GetPageTitle();

            await TileHelper.PinPageToStart(pageUrl, pageTitle);

            VisualStateManager.GoToState(this, nameof(DefaultVisualState), false);
        }

        private void RetryConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ActionRequested?.Invoke(this, XpotifyWebAppActionRequest.ShowSplashScreen);
            Controller.Navigate(loadFailedUrl);
        }

        private async void FinalizeAuthorization(string url)
        {
            try
            {
                var urlDecoder = new WwwFormUrlDecoder(url.Substring(url.IndexOf('?') + 1));
                await Authorization.RetrieveAndSaveTokensFromAuthCode(urlDecoder.GetFirstValueByName("code"));
                Controller.Navigate(new Uri(urlDecoder.GetFirstValueByName("state")));
            }
            catch (Exception ex)
            {
                logger.Info("Authorization failed. " + ex.ToString());

                Authorize("https://open.spotify.com/", clearExisting: false);
            }
        }

        public void Authorize(string targetUrl, bool clearExisting)
        {
            if (clearExisting)
            {
                Controller.ClearCookies();
                TokenHelper.ClearTokens();
                LocalConfiguration.IsLoggedInByFacebook = false;
            }

            var authorizationUrl = Authorization.GetAuthorizationUrl(targetUrl);
            Controller.Navigate(new Uri(authorizationUrl));
        }

        private void LoadFailedProxySettingsLink_Click(object sender, RoutedEventArgs e)
        {
            loadFailedProxySettingsLink.Visibility = Visibility.Collapsed;
            loadFailedProxySettings.Visibility = Visibility.Visible;
        }

        private async void SetInitialPlaybackState()
        {
            // Restore initial playback state
            // (We removed the localStorage entry, because of PWA's bug with Edge. See clearPlaybackLocalStorage.js for more info)

            if (initialPlaybackState == null)
                return;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3));

                var player = new Player();

                SpotifyApi.Model.Device thisDevice = null;

                for (int i = 0; i < 10; i++)
                {
                    var devices = await player.GetDevices();
                    thisDevice = devices.devices.FirstOrDefault(x => x.name.Contains("Edge") && x.name.Contains("Web"));

                    if (thisDevice != null)
                        break;
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }

                if (thisDevice != null)
                {
                    await player.SetVolume(thisDevice.id, initialPlaybackState.volume);
                }
            }
            catch (Exception ex)
            {
                logger.Warn("SetInitialPlaybackState failed: " + ex.ToString());
            }
        }

        private async void WebViewCheckTimer_Tick(object sender, object e)
        {
            // Ignore if not logged in
            if (!TokenHelper.HasTokens())
                return;

            //try
            //{
            //    var statusReport = await Controller.StatusReport();

            //    BackEnabled = statusReport.BackButtonEnabled;
            //    PlayStatusTracker.LocalPlaybackDataReceived(statusReport.NowPlaying);
            //}
            //catch (Exception ex)
            //{
            //    logger.Warn("statusReport failed: " + ex.ToString());
            //}
        }

        private async void StuckDetectTimer_Tick(object sender, object e)
        {
            // Ignore if not logged in
            if (!TokenHelper.HasTokens())
                return;

            //try
            //{
            //    var isPlayingOnThisApp = await Controller.IsPlayingOnThisApp();
            //    if (isPlayingOnThisApp)
            //    {
            //        var currentPlayTime = await Controller.GetCurrentSongPlayTime();

            //        if (currentPlayTime == "0:00"
            //            && PlayStatusTracker.LastPlayStatus.ProgressedMilliseconds > 5000
            //            && PlayStatusTracker.LastPlayStatus.IsPlaying)
            //        {
            //            if (stuckDetectCounter < 2)
            //            {
            //                stuckDetectCounter++;
            //            }
            //            else
            //            {
            //                stuckDetectCounter = 0;
            //                logger.Warn("Playback seems to have stuck.");

            //                var result = false;

            //                if ((DateTime.UtcNow - lastStuckFixApiCall) > TimeSpan.FromMinutes(1))
            //                {
            //                    lastStuckFixApiCall = DateTime.UtcNow;

            //                    var player = new Player();
            //                    result = await player.PreviousTrack();
            //                }

            //                if (result)
            //                {
            //                    AnalyticsHelper.Log("playbackStuck", "1");
            //                    ToastHelper.SendDebugToast("PlaybackStuck1", "PrevTrack issued.");
            //                    logger.Info("playbackStuck1");
            //                }
            //                else
            //                {
            //                    await Controller.NextTrack();
            //                    await Task.Delay(1500);
            //                    await Controller.PreviousTrack();
            //                    await Task.Delay(1500);
            //                    await Controller.PreviousTrack();

            //                    AnalyticsHelper.Log("playbackStuck", "3");
            //                    ToastHelper.SendDebugToast("PlaybackStuck3", "NextAndPrevAndPrevTrack issued.");
            //                    logger.Info("playbackStuck3");
            //                }
            //            }
            //        }
            //        else
            //        {
            //            stuckDetectCounter = 0;
            //        }
            //    }
            //    else
            //    {
            //        stuckDetectCounter = 0;
            //    }
            //}
            //catch (Exception ex)
            //{
            //    logger.Warn("checkCurrentSongPlayTime failed: " + ex.ToString());
            //}
        }
    }
}
