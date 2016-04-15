using HotSOS;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Data.Json;
using Windows.Networking.BackgroundTransfer;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HotSOSApp
{
    public sealed partial class MainPage : Page
    {
        private string fileTag;
        private CoreApplicationView view;

        public MainPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Required;
            (Application.Current as App).ToastReceived += MainPage_ToastReceived;
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.
        /// This parameter is typically used to configure the page.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            bool isNavigationNeeded = true;

            if ((e.Parameter != null) && !string.IsNullOrEmpty(e.Parameter.ToString()))
            {
                if (Utils.IsJson(e.Parameter.ToString()))
                {
                    string payload = e.Parameter.ToString().Replace("\'", "\"");
                    Utils.CallJSFunc(WebViewControl, "window.hotsos.notification", payload);
                }
            }

            if (this.Frame.BackStack.Count > 0)
            {
                var lastPageType = this.Frame.BackStack.Last().SourcePageType;
                if (lastPageType == typeof(BarcodeScannerPreview))
                {
                    var result = (string)e.Parameter;
                    if (result != null)
                        Utils.CallJSFunc(WebViewControl, "barcodeScannerDidScanBarcode", result);

                    isNavigationNeeded = false;
                }
            }

            string fullUri = App.LoginUri;

            if (isNavigationNeeded && Uri.IsWellFormedUriString(fullUri, UriKind.Absolute))
            {
                WebViewControl.Navigate(new Uri(fullUri));
                HardwareButtons.BackPressed += this.MainPage_BackPressed;
            }
        }

        /// <summary>
        /// Invoked when this page is being navigated away.
        /// </summary>
        /// <param name="e">Event data that describes how this page is navigating.</param>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            HardwareButtons.BackPressed -= this.MainPage_BackPressed;
        }

        /// <summary>
        /// Overrides the back button press to navigate in the WebView's back stack instead of the application's.
        /// </summary>
        private void MainPage_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (WebViewControl.CanGoBack)
            {
                WebViewControl.GoBack();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Navigates forward in the WebView's history.
        /// </summary>
        private void ForwardAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (WebViewControl.CanGoForward)
            {
                WebViewControl.GoForward();
            }
        }

        /// <summary>
        /// Navigates to the initial home page.
        /// </summary>
        private void HomeAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            if (Uri.IsWellFormedUriString(App.LoginUri, UriKind.Absolute))
                WebViewControl.Navigate(new Uri(App.LoginUri));
        }

        private void SettingsAppBarButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private async void WebViewControl_ScriptNotify(object sender, NotifyEventArgs e)
        {
            try
            {
                JsonObject jsonObj;
                if (JsonObject.TryParse(e.Value, out jsonObj) && (jsonObj != null))
                {
                    if (jsonObj.ContainsKey("action"))
                    {
                        if (jsonObj.GetNamedString("action") == "uploadAttachment")
                        {
                            //TODO: save tag for uploading
                            view = CoreApplication.GetCurrentView();

                            FileOpenPicker filePicker = new FileOpenPicker();
                            filePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                            filePicker.ViewMode = PickerViewMode.Thumbnail;

                            //Filter to include a sample subset of file types
                            filePicker.FileTypeFilter.Clear();
                            filePicker.FileTypeFilter.Add(".bmp");
                            filePicker.FileTypeFilter.Add(".png");
                            filePicker.FileTypeFilter.Add(".jpeg");
                            filePicker.FileTypeFilter.Add(".jpg");

                            if (jsonObj.ContainsKey("data"))
                                fileTag = jsonObj.GetNamedString("data");

                            filePicker.PickSingleFileAndContinue();
                            view.Activated += view_Activated;
                        }
                        else if (jsonObj.GetNamedString("action") == "downloadAttachment")
                        {
                            if (jsonObj.ContainsKey("data"))
                            {
                                string fileUrl = jsonObj.GetNamedString("data");
                                if (Uri.IsWellFormedUriString(fileUrl, UriKind.Absolute))
                                {
                                    Uri source = new Uri(fileUrl);
                                    StorageFile destinationFile = null;
                                    try
                                    {
                                        string fileName = Path.GetFileName(fileUrl);
                                        if (fileName != null)
                                        {
                                            destinationFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                                                fileName, CreationCollisionOption.ReplaceExisting);
                                        }
                                    }
                                    catch (FileNotFoundException ex)
                                    {
                                        return;
                                    }

                                    if (destinationFile != null)
                                    {
                                        BackgroundDownloader downloader = new BackgroundDownloader();
                                        DownloadOperation download = downloader.CreateDownload(source, destinationFile);
                                        await download.StartAsync();
                                        ResponseInformation response = download.GetResponseInformation();
                                    }
                                }
                            }
                        }
                        else if (jsonObj.GetNamedString("action") == "barcodeScan")
                        {
                            Frame.Navigate(typeof(BarcodeScannerPreview));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ScriptRunning:" + ex.Message);
            }
        }

        private async void view_Activated(CoreApplicationView sender, IActivatedEventArgs args1)
        {
            try
            {
                FileOpenPickerContinuationEventArgs args = args1 as FileOpenPickerContinuationEventArgs;

                if (args != null)
                {
                    if (args.Files.Count == 0) return;

                    view.Activated -= view_Activated;
                    var file = (StorageFile)args.Files[0];

                    var fileToUpload = new FileToUpload();
                    await fileToUpload.Init(file);

                    string jsonString = JsonConvert.SerializeObject(fileToUpload);
                    Utils.CallJSFunc(WebViewControl, "window.hotsos.windowsPhone.addAttachment", jsonString);
                }
            }
            catch
            { }
        }

        private void MainPage_ToastReceived(object sender, ToastNotificationArgs e)
        {
            if (Utils.IsJson(e.Payload))
                Utils.CallJSFunc(WebViewControl, "window.hotsos.notification", e.Payload);
        }
    }
}