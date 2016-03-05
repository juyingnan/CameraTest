using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.System.Display;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CameraTest
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            DispatcherTimerSetup();
        }

        private readonly DisplayRequest _displayRequest = new DisplayRequest();
        private MediaCapture _mediaCapture;
        private bool _isInitialized;
        private bool _isPreviewing;
        private bool _isRecording;
        private bool _externalCamera;
        private bool _mirroringPreview;
        private readonly DisplayInformation _displayInformation = DisplayInformation.GetForCurrentView();
        private DisplayOrientations _displayOrientation = DisplayOrientations.Portrait;
        private readonly SimpleOrientationSensor _orientationSensor = SimpleOrientationSensor.GetDefault();
        private SimpleOrientation _deviceOrientation = SimpleOrientation.NotRotated;
        private static readonly Guid RotationKey = new Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1");
        Random random = new Random();
        DispatcherTimer dispatcherTimer;

        public void DispatcherTimerSetup()
        {
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += dispatcherTimer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 2);
            DebugTips("dispatcherTimer.IsEnabled = " + dispatcherTimer.IsEnabled + "\n");
        }

        private async void dispatcherTimer_Tick(object sender, object e)
        {
            await CaptureWhiteLine();
        }

        private async Task InitializeCameraAsync()
        {

            if (_mediaCapture == null)
            {

                // Get available devices for capturing pictures
                var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

                // Get the desired camera by panel
                DeviceInformation cameraDevice =
                    allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null &&
                    x.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Back);

                // If there is no camera on the specified panel, get any camera
                cameraDevice = cameraDevice ?? allVideoDevices.FirstOrDefault();

                if (cameraDevice == null)
                {
                    DebugTips("No camera device found.");
                    return;
                }

                // Create MediaCapture and its settings
                _mediaCapture = new MediaCapture();

                // Register for a notification when video recording has reached the maximum time and when something goes wrong
                _mediaCapture.RecordLimitationExceeded += MediaCapture_RecordLimitationExceeded;

                var mediaInitSettings = new MediaCaptureInitializationSettings { VideoDeviceId = cameraDevice.Id };

                // Initialize MediaCapture
                try
                {
                    await _mediaCapture.InitializeAsync(mediaInitSettings);
                    _isInitialized = true;
                }
                catch (UnauthorizedAccessException)
                {
                    DebugTips("The app was denied access to the camera");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception when initializing MediaCapture with {0}: {1}", cameraDevice.Id, ex.ToString());
                }

                // If initialization succeeded, start the preview
                if (_isInitialized)
                {
                    // Figure out where the camera is located
                    if (cameraDevice.EnclosureLocation == null || cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Unknown)
                    {
                        // No information on the location of the camera, assume it's an external camera, not integrated on the device
                        _externalCamera = true;
                    }
                    else
                    {
                        // Camera is fixed on the device
                        _externalCamera = false;

                        // Only mirror the preview if the camera is on the front panel
                        _mirroringPreview = (cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front);
                    }

                    _mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);
                    await StartPreviewAsync();

                    UpdateCaptureControls();
                }
            }
        }

        private void DebugTips(string tip)
        {
            TipsTextBlock.Text = tip;
        }

        private async Task StartPreviewAsync()
        {
            // Prevent the device from sleeping while the preview is running
            _displayRequest.RequestActive();

            // Set the preview source in the UI and mirror it if necessary
            PreviewControl.Source = _mediaCapture;
            PreviewControl.FlowDirection = _mirroringPreview ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            // Start the preview
            try
            {
                await _mediaCapture.StartPreviewAsync();
                _isPreviewing = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception when starting the preview: {0}", ex.ToString());
            }

            // Initialize the preview to the current orientation
            if (_isPreviewing)
            {
                await SetPreviewRotationAsync();
            }

            //try predefine rectangles

        }

        private async void MediaCapture_RecordLimitationExceeded(MediaCapture sender)
        {
            await StopRecordingAsync();
        }

        private async Task StopRecordingAsync()
        {
            try
            {
                DebugTips("Stopping recording...");

                _isRecording = false;
                await _mediaCapture.StopRecordAsync();

                DebugTips("Stopped recording!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception when stopping video recording: {0}", ex.ToString());
            }
        }

        private void UpdateCaptureControls()
        {
            // The buttons should only be enabled if the preview started sucessfully
            CaptureButton.IsEnabled = _isPreviewing;
            ScreenshotButton.IsEnabled = _isPreviewing;

            // Update recording button to show "Stop" icon instead of red "Record" icon
            //StartRecordingIcon.Visibility = _isRecording ? Visibility.Collapsed : Visibility.Visible;
            //StopRecordingIcon.Visibility = _isRecording ? Visibility.Visible : Visibility.Collapsed;

            // If the camera doesn't support simultaneosly taking pictures and recording video, disable the photo button on record
            if (_isInitialized && !_mediaCapture.MediaCaptureSettings.ConcurrentRecordAndPhotoSupported)
            {
                CaptureButton.IsEnabled = !_isRecording;

                // Make the button invisible if it's disabled, so it's obvious it cannot be interacted with
                CaptureButton.Opacity = CaptureButton.IsEnabled ? 1 : 0;
            }
        }

        private async Task SetPreviewRotationAsync()
        {
            // Only need to update the orientation if the camera is mounted on the device
            if (_externalCamera) return;

            // Populate orientation variables with the current state
            _displayOrientation = DisplayOrientations.Portrait;

            // Calculate which way and how far to rotate the preview
            int rotationDegrees = ConvertDisplayOrientationToDegrees(_displayOrientation);

            // The rotation direction needs to be inverted if the preview is being mirrored
            if (_mirroringPreview)
            {
                rotationDegrees = (360 - rotationDegrees) % 360;
            }

            // Add rotation metadata to the preview stream to make sure the aspect ratio / dimensions match when rendering and getting preview frames
            var props = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
            props.Properties.Add(RotationKey, rotationDegrees);
            await _mediaCapture.SetEncodingPropertiesAsync(MediaStreamType.VideoPreview, props, null);

        }

        private static int ConvertDisplayOrientationToDegrees(DisplayOrientations orientation)
        {
            switch (orientation)
            {
                case DisplayOrientations.Portrait:
                    return 90;
                case DisplayOrientations.LandscapeFlipped:
                    return 180;
                case DisplayOrientations.PortraitFlipped:
                    return 270;
                case DisplayOrientations.Landscape:
                default:
                    return 0;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await InitializeCameraAsync();
        }

        private async Task StopPreviewAsync()
        {
            dispatcherTimer.Stop();
            await System.Threading.Tasks.Task.Delay(1000);
            WhiteLineCanvas.Children.Clear();
            // Stop the preview
            try
            {
                _isPreviewing = false;
                await _mediaCapture.StopPreviewAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception when stopping the preview: {0}", ex.ToString());
            }

            // Use the dispatcher because this method is sometimes called from non-UI threads
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Cleanup the UI
                PreviewControl.Source = null;

                // Allow the device screen to sleep now that the preview is stopped
                _displayRequest.RequestRelease();
            });
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await StopPreviewAsync();
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            dispatcherTimer.Start();
        }

        private async Task CaptureWhiteLine()
        {
            //start timer
            DateTime startTime = DateTime.Now;

            //Get screenshot bitmap
            SoftwareBitmap previewBitmap = await GetPreviewBitmap();
            WriteableBitmap wb = new WriteableBitmap(previewBitmap.PixelWidth, previewBitmap.PixelHeight);
            previewBitmap.CopyToBuffer(wb.PixelBuffer);
            wb = wb.Resize(wb.PixelWidth / 2, wb.PixelHeight / 2, WriteableBitmapExtensions.Interpolation.Bilinear);

            //divide into blocks
            int horizontalBlock = 18;
            int VerticalBlock = 32;
            int blockHeight = wb.PixelHeight / VerticalBlock;
            int blockWidth = wb.PixelWidth / horizontalBlock;

            //set canvas
            WhiteLineCanvas.Height = PreviewControl.ActualHeight;
            WhiteLineCanvas.Width = WhiteLineCanvas.Height * previewBitmap.PixelWidth / previewBitmap.PixelHeight;
            WhiteLineCanvas.Children.Clear();

            //test
            //WriteableBitmap wbt = BitmapFactory.New(3, 3);
            //wbt.SetPixel(0, 0, Colors.Red);
            //wbt.SetPixel(1, 0, Colors.Blue);
            //wbt.SetPixel(2, 0, Colors.Green);
            //wbt.SetPixel(0, 1, Colors.White);
            //wbt.SetPixel(1, 1, Colors.Black);
            //wbt.SetPixel(2, 1, Colors.Gray);
            //wbt.SetPixel(0, 2, Colors.WhiteSmoke);
            //wbt.SetPixel(1, 2, Colors.Brown);
            //wbt.SetPixel(2, 2, Colors.Yellow);
            //imageControl.Source = wbt;
            //byte[] datat = wbt.ToByteArray();

            //try faster, no GetPixel()
            byte[] data = wb.ToByteArray();
            int bitsPerPixel = 4;
            int judgePoints = 9;
            int whiteThreshold = 150;


            for (int i = 0; i < horizontalBlock; i++)
                for (int j = 0; j < VerticalBlock; j++)
                {
                    int xStart = i * blockWidth;
                    int xEnd = (i + 1) * blockWidth - 1;
                    int yStart = j * blockHeight;
                    int yEnd = (j + 1) * blockHeight - 1;
                    int hitPoints = 0;
                    for (int k = 0; k < judgePoints; k++)
                    {
                        int x = random.Next(xStart, xEnd);
                        int y = random.Next(yStart, yEnd);
                        int index = Coordinate2Index(wb.PixelWidth, bitsPerPixel, x, y);
                        if (data[index + 0] > whiteThreshold && data[index + 1] > whiteThreshold && data[index + 2] > whiteThreshold)
                        {
                            hitPoints++;
                        }
                    }
                    if (hitPoints >= judgePoints / 3 * 2)
                    {
                        Rectangle rec = new Rectangle();
                        double widthRatio = WhiteLineCanvas.Width / wb.PixelWidth;
                        double heightRatio = WhiteLineCanvas.Height / wb.PixelHeight;
                        rec.Width = blockWidth * widthRatio;
                        rec.Height = blockHeight * heightRatio;
                        rec.Fill = new SolidColorBrush(Colors.Green);
                        Canvas.SetLeft(rec, xStart * widthRatio);
                        Canvas.SetTop(rec, yStart * heightRatio);
                        WhiteLineCanvas.Children.Add(rec);
                    }

                    //debug: stroke reference
                    //if (true)
                    //{
                    //    Rectangle rec = new Rectangle();
                    //    double widthRatio = WhiteLineCanvas.Width / wb.PixelWidth;
                    //    double heightRatio = WhiteLineCanvas.Height / wb.PixelHeight;
                    //    rec.Width = blockWidth * widthRatio;
                    //    rec.Height = blockHeight * heightRatio;
                    //    rec.Stroke = new SolidColorBrush(Color.FromArgb(data[index + 3], data[index + 2], data[index + 1], data[index]));
                    //    rec.StrokeThickness = 2;
                    //    Canvas.SetLeft(rec, xStart * widthRatio);
                    //    Canvas.SetTop(rec, yStart * heightRatio);
                    //    WhiteLineCanvas.Children.Add(rec);
                    //}

                    //if (wb.GetPixel(xStart, yStart).B > 200 && wb.GetPixel(xStart, yStart).B > 200 && wb.GetPixel(xStart, yStart).B > 200)
                }
            //imageControl.Source = wb;
            //DebugTips(wb.GetPixel(i, j).R.ToString() + "," + wb.GetPixel(i, j).G.ToString() + "," + wb.GetPixel(i, j).B.ToString());

            //end timer
            DateTime endTime = DateTime.Now;
            var elapsedTime = endTime - startTime;
            DebugTips(elapsedTime.TotalMilliseconds.ToString());
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, (int)(elapsedTime.TotalMilliseconds * 2.0));
        }

        private async Task<SoftwareBitmap> GetPreviewBitmap()
        {
            var previewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;
            //VideoFrame videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Width, (int)previewProperties.Height);
            VideoFrame videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Height, (int)previewProperties.Width);
            VideoFrame previewFrame = await _mediaCapture.GetPreviewFrameAsync(videoFrame);

            SoftwareBitmap previewBitmap = previewFrame.SoftwareBitmap;
            //SoftwareBitmapSource bitmapSource = new SoftwareBitmapSource();
            //await bitmapSource.SetBitmapAsync(previewBitmap);

            //imageControl.Source = bitmapSource;

            //await StopPreviewAsync();
            previewFrame.Dispose();
            previewFrame = null;
            return previewBitmap;
        }

        private int Coordinate2Index(int pixelWidth, int bitsPerPixel, int xStart, int yStart)
        {
            return bitsPerPixel * (pixelWidth * yStart + xStart);
        }
    }
}
