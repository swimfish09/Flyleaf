using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;

using Dragablz;
using WpfColorFontDialog;
using MaterialDesignThemes.Wpf;
using MaterialDesignColors;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using static FlyleafLib.Config;

namespace FlyleafLib.Controls.WPF
{
    public partial class Flyleaf : UserControl, INotifyPropertyChanged, IVideoView
    {
        #region Properties
        private bool            IsDesignMode=> (bool) DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;

        public Player           Player      { get => _Player; set { _Player = value; InitializePlayer(); } }
        Player _Player;

        
        public AudioInfo        AudioInfo       => Player?.Audio;
        public VideoInfo        VideoInfo       => Player?.Video;
        public SubtitlesInfo    SubtitlesInfo   => Player?.Subtitles;
        public Demuxer          VideoDemuxer    => Player?.VideoDemuxer;
        public VideoDecoder     VideoDecoder    => Player?.VideoDecoder;
        public AudioDecoder     AudioDecoder    => Player?.AudioDecoder;

        public AudioMaster      AudioMaster     => Master.AudioMaster;
        public FlyleafWindow    WindowFront     => VideoView?.WindowFront;
        public WindowsFormsHost WinFormsHost    => VideoView?.WinFormsHost;
        public VideoView        VideoView       => Player?.VideoView;
        
        public DecoderContext   DecCtx          => Player?.decoder;
        public Config           Config          => Player?.Config;
        public PlayerConfig     PlayerConfig    => Config.Player;
        public AudioConfig      AudioConfig     => Config?.Audio;
        public SubtitlesConfig  SubtitlesConfig => Config?.Subtitles;
        public VideoConfig      VideoConfig     => Config?.Video;
        public DecoderConfig    DecoderConfig   => Config?.Decoder;
        public DemuxerConfig    DemuxerConfig   => Config?.Demuxer;
        public SerializableDictionary<string, SerializableDictionary<string, string>>
                                PluginsConfig   => Config?.Plugins;

        string _ErrorMsg;
        public string ErrorMsg { get => _ErrorMsg; set => Set(ref _ErrorMsg, value); }

        bool _ShowDebug;
        public bool ShowDebug
        {
            get => _ShowDebug;
            set { Set(ref _ShowDebug, value); Config.Player.Stats = value; }
        }

        bool _IsFullscreen;
        public bool IsFullscreen
        {
            get => _IsFullscreen;
            private set => Set(ref _IsFullscreen, value);
        }
        #endregion

        #region Settings
        public bool EnableKeyBindings { get; set; } = true;
        public bool EnableMouseEvents { get; set; } = true;
        int _IdleTimeout = 6000;
        public int IdleTimeout
        {
            get => _IdleTimeout;
            set { _IdleTimeout = value; IdleTimeoutChanged(); }
        }
        #endregion

        #region Initialize
        public TextBlock Subtitles { get; set; }
        Thickness subsInitialMargin;

        ContextMenu popUpMenu, popUpMenuSubtitles, popUpMenuVideo;
        MenuItem    popUpAspectRatio;
        MenuItem    popUpKeepAspectRatio;
        MenuItem    popUpCustomAspectRatio;
        MenuItem    popUpCustomAspectRatioSet;
        string      dialogSettingsIdentifier;
        bool        playerInitialized;

        int snapshotCounter = 1;
        int recordCounter = 1;

        public Flyleaf()
        {
            // TODO: fix assemblies to load here
            var a1 = new Card();
            var a2 = new Hue("Dummy", Colors.Black, Colors.White);
            var a3 = DragablzColors.WindowBaseColor.ToString();

            InitializeComponent();
            if (IsDesignMode) return;

            DataContext = this;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (IsDesignMode) return;

            Initialize();
        }

        public void Dispose()
        {
            lock (this)
            {
                if (disposed) return;

                WindowFront?.Close();
                Player?.Dispose();

                _IdleTimeout = -1;
                Resources.MergedDictionaries.Clear();
                Resources.Clear();
                Template.Resources.MergedDictionaries.Clear();
                Content = null;
                DataContext = null;
                disposed = true;
            }
        }
        bool disposed;

        private void Initialize()
        {
            popUpMenu           = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner", this))?.ContextMenu;
            popUpMenuSubtitles  = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Subtitles", this))?.ContextMenu;
            popUpMenuVideo      = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Video", this))?.ContextMenu;
            Subtitles           = (TextBlock) Template.FindName("PART_Subtitles", this);

            var dialogSettings  = ((DialogHost)Template.FindName("PART_DialogSettings", this));
            if (dialogSettings != null)
            {
                dialogSettingsIdentifier = $"DialogSettings_{Guid.NewGuid()}";
                dialogSettings.Identifier = dialogSettingsIdentifier;
            }

            if (popUpMenu           != null) popUpMenu.PlacementTarget          = this;
            if (popUpMenuSubtitles  != null) popUpMenuSubtitles.PlacementTarget = this;
            if (popUpMenuVideo      != null) popUpMenuVideo.PlacementTarget     = this;

            if (popUpMenu != null)
            {
                var videoItem = from object item in popUpMenu.Items where item is MenuItem && ((MenuItem)item).Header != null && ((MenuItem)item).Header.ToString() == "Video" select item;
                var aspectRatioItem = from object item in ((MenuItem)videoItem.ToArray()[0]).Items where ((MenuItem)item).Header != null && ((MenuItem)item).Header.ToString() == "Aspect Ratio" select item;
                popUpAspectRatio = (MenuItem)aspectRatioItem.ToArray()[0];
                popUpMenu.MouseMove += (o, e) => { lastMouseActivity = DateTime.UtcNow.Ticks; };
            }

            if (popUpAspectRatio != null)
            {
                foreach (var aspectRatio in AspectRatio.AspectRatios)
                {
                    if (aspectRatio == AspectRatio.Custom) continue;
                    popUpAspectRatio.Items.Add(new MenuItem() { Header = aspectRatio, IsCheckable = true });
                    if (aspectRatio == AspectRatio.Keep) popUpKeepAspectRatio = (MenuItem)popUpAspectRatio.Items[popUpAspectRatio.Items.Count - 1];
                }

                popUpCustomAspectRatio = new MenuItem() { IsCheckable = true };
                popUpCustomAspectRatioSet = new MenuItem() { Header = "Set Custom..." };
                popUpCustomAspectRatioSet.Click += (n1, n2) => { DialogAspectRatio(); };

                popUpAspectRatio.Items.Add(popUpCustomAspectRatio);
                popUpAspectRatio.Items.Add(popUpCustomAspectRatioSet);

                popUpMenu.Opened += (o, e) =>
                {
                    CanPaste = String.IsNullOrEmpty(Clipboard.GetText()) ? false : true;
                    popUpCustomAspectRatio.Header = $"Custom ({VideoConfig.CustomAspectRatio})";
                    //popUpKeepAspectRatio.Header = $"Keep ({Player.VideoInfo.AspectRatio})";

                    FixMenuSingleCheck(popUpAspectRatio, VideoConfig.AspectRatio.ToString());
                    if (VideoConfig.AspectRatio == AspectRatio.Custom)
                        popUpCustomAspectRatio.IsChecked = true;
                    else if (VideoConfig.AspectRatio == AspectRatio.Keep)
                        popUpKeepAspectRatio.IsChecked = true;
                };
            }

            RegisterCommands();
            if (Subtitles != null)
            {
                subsInitialMargin   = Subtitles.Margin;
                SubtitlesFontDesc   = $"{Subtitles.FontFamily} ({Subtitles.FontWeight}), {Subtitles.FontSize}";
                SubtitlesFontColor  = Subtitles.Foreground;
            }
            Raise(null);

            lastMouseActivity = lastKeyboardActivity = DateTime.UtcNow.Ticks;

            if (_IdleTimeout > 0 && (idleThread == null || !idleThread.IsAlive))
            {
                idleThread = new Thread(() => { IdleThread(); } );
                idleThread.IsBackground = true;
                idleThread.Start();
            }
            
        }
        private void InitializePlayer()
        {
            if (playerInitialized) return;
            playerInitialized = true;

            VideoView.Resources   = Resources;
            VideoView.FontFamily  = FontFamily;
            VideoView.FontSize    = FontSize;

            Unloaded += (o, e) => { Dispose(); };

            // Keys (WFH will work for backwindow's key events) | both WPF
            if (EnableKeyBindings)
            {
                WindowFront.KeyDown += Flyleaf_KeyDown;
                WinFormsHost.KeyDown+= Flyleaf_KeyDown;
                WindowFront.KeyUp   += Flyleaf_KeyUp;
                WinFormsHost.KeyUp  += Flyleaf_KeyUp;
            }

            // Mouse (For back window we can only use Player.Control to catch mouse events) | WinFroms + WPF :(
            if (EnableMouseEvents)
            {
                Player.Control.DoubleClick  += (o, e) => { ToggleFullscreenAction(); };
                Player.Control.MouseClick   += (o, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Right & popUpMenu != null) popUpMenu.IsOpen = true; };

                Player.Control.MouseWheel   += (o, e) => { Flyleaf_MouseWheel(e.Delta); };
                WindowFront.MouseWheel      += (o, e) => { Flyleaf_MouseWheel(e.Delta); };

                WindowFront.MouseMove       += (o, e) => { lastMouseActivity = DateTime.UtcNow.Ticks; };

                // Pan Drag Move
                Player.Control.MouseMove    += (o, e) => 
                {
                    lastMouseActivity = DateTime.UtcNow.Ticks;

                    if (panClickX != -1 && e.Button == System.Windows.Forms.MouseButtons.Left && 
                       (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
                    {
                        Player.PanXOffset = panPrevX + e.X - panClickX;
                        Player.PanYOffset = panPrevY + e.Y - panClickY;
                    }
                };

                Player.Control.MouseUp      += (o, e) => { panClickX = -1; panClickY = -1; };
                Player.Control.MouseDown    += (o, e) =>
                {
                    if (!Player.CanPlay || e.Button != System.Windows.Forms.MouseButtons.Left) return;

                    if (panClickX == -1)
                    {
                        panClickX = e.X;
                        panClickY = e.Y;
                        panPrevX = Player.PanXOffset;
                        panPrevY = Player.PanYOffset;
                    }
                };
            }

            // Drag & Drop
            Player.Control.AllowDrop    = true;
            Player.Control.DragEnter    += Flyleaf_DragEnter;
            Player.Control.DragDrop     += Flyleaf_DragDrop;

            Player.OpenCompleted        += Player_OpenCompleted;
            Player.OpenInputCompleted   += Player_OpenInputCompleted;
        }

        int panClickX = -1, panClickY = -1, panPrevX = -1, panPrevY = -1;
        #endregion

        #region ICommands
        void RegisterCommands()
        {
            TogglePlayPause     = new RelayCommand(TogglePlayPauseAction); //, p => Session.CanPlay);
            ToggleFullscreen    = new RelayCommand(ToggleFullscreenAction);
            ToggleMute          = new RelayCommand(ToggleMuteAction);
            OpenSettings        = new RelayCommand(OpenSettingsAction);
            ChangeAspectRatio   = new RelayCommand(ChangeAspectRatioAction);
            SetSubtitlesFont    = new RelayCommand(SetSubtitlesFontAction);

            OpenFromFileDialog  = new RelayCommand(OpenFromFileDialogAction);
            OpenFromPaste       = new RelayCommand(OpenFromPasteAction);
            ExitApplication     = new RelayCommand(ExitApplicationAction);
            SetSubsDelayMs      = new RelayCommand(SetSubsDelayMsAction);
            SetAudioDelayMs     = new RelayCommand(SetAudioDelayMsAction);
            SetSubsPositionY    = new RelayCommand(SetSubsPositionYAction);
            SetPlaybackSpeed    = new RelayCommand(SetPlaybackSpeedAction);

            ResetSubsPositionY  = new RelayCommand(ResetSubsPositionYAction);
            ResetSubsDelayMs    = new RelayCommand(ResetSubsDelayMsAction);
            ResetAudioDelayMs   = new RelayCommand(ResetAudioDelayMsAction);
            OpenStream          = new RelayCommand(OpenStreamAction);
            OpenInput           = new RelayCommand(OpenInputAction);

            ShowSubtitlesMenu   = new RelayCommand(ShowSubtitlesMenuAction);
            ShowVideoMenu       = new RelayCommand(ShowVideoMenuAction);
            ToggleRecord        = new RelayCommand(ToggleRecordAction);
            TakeSnapshot        = new RelayCommand(TakeSnapshotAction);
            ZoomReset           = new RelayCommand(ZoomResetAction);
            Zoom                = new RelayCommand(ZoomAction);
        }

        public ICommand SetPlaybackSpeed { get; set; }
        public void SetPlaybackSpeedAction(object speed) { Config.Player.Speed = int.Parse(speed.ToString()); }

        public ICommand ZoomReset { get; set; }
        public void ZoomResetAction(object obj = null) { Player.Zoom = 0; Player.renderer.PanXOffset = 0; Player.renderer.PanYOffset = 0; }

        public ICommand Zoom { get; set; }
        public void ZoomAction(object offset) { Player.Zoom += int.Parse(offset.ToString()); }

        public ICommand TakeSnapshot { get; set; }
        public void TakeSnapshotAction(object obj = null)
        {
            if (!Player.CanPlay) return;

            Player.TakeSnapshot(System.IO.Path.Combine(Environment.CurrentDirectory, $"Snapshot{snapshotCounter}.bmp"));
            snapshotCounter++;
        }

        public ICommand ShowSubtitlesMenu { get; set; }
        public void ShowSubtitlesMenuAction(object obj = null) { popUpMenuSubtitles.IsOpen = true; }

        public ICommand ShowVideoMenu { get; set; }
        public void ShowVideoMenuAction(object obj = null) { popUpMenuVideo.IsOpen = true; }

        public ICommand OpenStream { get; set; }
        public void OpenStreamAction(object stream) { Player.OpenAsync((StreamBase)stream); }

        public ICommand OpenInput { get; set; }
        public void OpenInputAction(object input) { Player.OpenAsync((InputBase)input); }

        public ICommand ResetSubsPositionY { get; set; }
        public void ResetSubsPositionYAction(object obj = null) { Subtitles.Margin = subsInitialMargin; }

        public ICommand ResetSubsDelayMs { get; set; }
        public void ResetSubsDelayMsAction(object obj = null) { SubtitlesConfig.Delay = 0; }

        public ICommand ResetAudioDelayMs { get; set; }
        public void ResetAudioDelayMsAction(object obj = null) { AudioConfig.Delay = 0; }

        public ICommand SetSubsPositionY { get; set; }
        public void SetSubsPositionYAction(object y) { Thickness t = Subtitles.Margin; t.Bottom += int.Parse(y.ToString()); Subtitles.Margin = t; Raise(nameof(Subtitles)); }

        public ICommand SetAudioDelayMs { get; set; }
        public void SetAudioDelayMsAction(object delay) { AudioConfig.Delay += (int.Parse(delay.ToString())) * (long)10000; }

        public ICommand SetSubsDelayMs { get; set; }
        public void SetSubsDelayMsAction(object delay) { SubtitlesConfig.Delay += (int.Parse(delay.ToString())) * (long)10000; }

        public ICommand OpenFromFileDialog  { get; set; }
        public void OpenFromFileDialogAction(object obj = null)
        {
            System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
            if(openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) Open(openFileDialog.FileName);
        }

        public ICommand OpenFromPaste  { get; set; }
        public void OpenFromPasteAction(object obj = null) { Open(Clipboard.GetText()); }

        public ICommand ExitApplication { get ; set; }
        public void ExitApplicationAction(object obj = null) { Application.Current.Shutdown(); }


        bool _CanPaste;
        public bool         CanPaste            {
            get => _CanPaste;
            set => Set(ref _CanPaste, value);
        }
        public ICommand ChangeAspectRatio { get; set; }
        public void ChangeAspectRatioAction(object obj = null)
        {
            MenuItem mi = ((MenuItem)obj);

            if (Regex.IsMatch(mi.Header.ToString(), "Custom"))
            {
                if (Regex.IsMatch(mi.Header.ToString(), "Set")) return;
                VideoConfig.AspectRatio = AspectRatio.Custom;
                return;
            }
            else if (Regex.IsMatch(mi.Header.ToString(), "Keep"))
                VideoConfig.AspectRatio = AspectRatio.Keep;
            else
                VideoConfig.AspectRatio = mi.Header.ToString();
        }

        string _SubtitlesFontDesc;
        public string       SubtitlesFontDesc   {
            get => _SubtitlesFontDesc;
            set => Set(ref _SubtitlesFontDesc, value);
        }

        Brush _SubtitlesFontColor;
        public Brush        SubtitlesFontColor  {
            get => _SubtitlesFontColor;
            set => Set(ref _SubtitlesFontColor, value);
        }

        public ICommand SetSubtitlesFont    { get; set; }
        public void SetSubtitlesFontAction(object obj = null)
        {
            ColorFontDialog dialog  = new ColorFontDialog();
            dialog.Font = new FontInfo(Subtitles.FontFamily, Subtitles.FontSize, Subtitles.FontStyle, Subtitles.FontStretch, Subtitles.FontWeight, (SolidColorBrush) Subtitles.Foreground);

            if (dialog.ShowDialog() == true && dialog.Font != null)
            {
                Subtitles.FontFamily    = dialog.Font.Family;
                Subtitles.FontSize      = dialog.Font.Size;
                Subtitles.FontWeight    = dialog.Font.Weight;
                Subtitles.FontStretch   = dialog.Font.Stretch;
                Subtitles.FontStyle     = dialog.Font.Style;
                Subtitles.Foreground    = dialog.Font.BrushColor;

                SubtitlesFontDesc       = $"{Subtitles.FontFamily} ({Subtitles.FontWeight}), {Subtitles.FontSize}";
                SubtitlesFontColor      = Subtitles.Foreground;
            }
        }

        public ICommand OpenSettings        { get; set; }
        public async void OpenSettingsAction(object obj = null)
        {
            if (dialogSettingsIdentifier == null) return;
            
            if (DialogHost.IsDialogOpen(dialogSettingsIdentifier))
            {
                DialogHost.Close(dialogSettingsIdentifier, "cancel");
                return;
            }

            var prevVideoConfig = VideoConfig.Clone();

            var view = new Settings(this);//Session);
            view.DataContext = this;
            var result = await DialogHost.Show(view, dialogSettingsIdentifier, view.Closing);

            if (result != null && result.ToString() == "cancel")
            {
                VideoConfig.HDRtoSDRMethod  = prevVideoConfig.HDRtoSDRMethod;
                VideoConfig.HDRtoSDRTone    = prevVideoConfig.HDRtoSDRTone;
                VideoConfig.Contrast        = prevVideoConfig.Contrast;
                VideoConfig.Brightness      = prevVideoConfig.Brightness;
            }

            view.Closed(result);
        }

        public ICommand ToggleMute          { get; set; }
        public void ToggleMuteAction(object obj = null)
        {
            Player.Mute = !Player.Mute;
        }

        public ICommand TogglePlayPause     { get; set; }
        public void TogglePlayPauseAction(object obj = null)
        {
            if (Player.IsPlaying)
                Player.Pause();
            else
                Player.Play();
        }
        
        public ICommand ToggleFullscreen    { get; set; }
        public void ToggleFullscreenAction(object obj = null)
        {
            if (VideoView.IsFullScreen)
                VideoView.NormalScreen();
            else
                VideoView.FullScreen();

            IsFullscreen = VideoView.IsFullScreen;
        }

        public ICommand ToggleRecord { get; set; }
        private void ToggleRecordAction(object obj = null)
        {
            if (!Player.CanPlay) return;

            if (Player.decoder.VideoDemuxer.IsRecording)
                Player.StopRecording();
            else
            {
                string filename = $"Record{recordCounter++}";
                Player.StartRecording(ref filename);
            }
        }
        #endregion

        #region TODO
        public void Open(string url)
        {
            if (String.IsNullOrEmpty(url)) return;

            Player.OpenAsync(url);
        }
        public void Player_OpenCompleted(object sender, Player.OpenCompletedArgs e)
        {
            if (e.Type == MediaType.Video || (!Player.Video.IsOpened && e.Type == MediaType.Audio))
            {
                ErrorMsg = e.Success ? "" : e.Error;
                Player.Play();
                Raise(null); // Ensures fast UI update
            }
        }
        private void Player_OpenInputCompleted(object sender, Player.OpenInputCompletedArgs e)
        {
            if (e.Type == MediaType.Video || (!Player.Video.IsOpened && e.Type == MediaType.Audio))
            {
                ErrorMsg = e.Success ? "" : e.Error;
                if (Player.IsPlaylist) Player.Play();
                Raise(null); // Ensures fast UI update
            }
        }

        private async void DialogAspectRatio()
        {
            if (dialogSettingsIdentifier == null) return;

            var stackVertical    = new StackPanel() { Height=100, Orientation = Orientation.Vertical };
            var stackHorizontal1 = new StackPanel() { Margin = new Thickness(10), Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center};
            var stackHorizontal2 = new StackPanel() { Margin = new Thickness(10), Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };

            var textBox = new TextBox() { VerticalAlignment = VerticalAlignment.Center, Width = 70, Margin = new Thickness(10, 0, 0, 0), Text=VideoConfig.CustomAspectRatio.ToString()};
            textBox.PreviewTextInput += (n1, n2) => { n2.Handled = !Regex.IsMatch(n2.Text, @"^[0-9\.\,\/\:]+$"); };

            var buttonOK = new Button() { Content = "OK" };
            var buttonCancel = new Button() { Margin = new Thickness(10, 0, 0, 0), Content = "Cancel" };

            buttonOK.Click +=       (n1, n2) => { if (textBox.Text != AspectRatio.Invalid) VideoConfig.CustomAspectRatio = textBox.Text; DialogHost.Close(dialogSettingsIdentifier); };
            buttonCancel.Click +=   (n1, n2) => { DialogHost.Close(dialogSettingsIdentifier); };

            stackHorizontal1.Children.Add(new TextBlock() { VerticalAlignment = VerticalAlignment.Center, Text="Set Custom Ratio: "});
            stackHorizontal1.Children.Add(textBox);
            stackHorizontal2.Children.Add(buttonOK);
            stackHorizontal2.Children.Add(buttonCancel);

            stackVertical.Children.Add(stackHorizontal1);
            stackVertical.Children.Add(stackHorizontal2);

            stackVertical.Orientation = Orientation.Vertical;

            var result = await DialogHost.Show(stackVertical, dialogSettingsIdentifier);
        }
        private void FixMenuSingleCheck(MenuItem mi, string checkedItem = null)
        {
            foreach (var item in mi.Items)
            {
                if (checkedItem != null && ((MenuItem)item).Header.ToString() == checkedItem)
                    ((MenuItem)item).IsChecked = true;
                else
                    ((MenuItem)item).IsChecked = false;
            }
        }
        #endregion

        #region Activity Mode
        Thread idleThread;
        long lastKeyboardActivity, lastMouseActivity;
        public enum ActivityMode
        {
            Idle,
            Active,
            FullActive
        }

        public void IdleTimeoutChanged()
        {
            if (_IdleTimeout > 0 && (idleThread == null || !idleThread.IsAlive))
            {
                idleThread = new Thread(() => { IdleThread(); } );
                idleThread.IsBackground = true;
                idleThread.Start();
            }
                
            CurrentMode = ActivityMode.FullActive;
        }

        ActivityMode _CurrentMode = ActivityMode.FullActive;
        public ActivityMode         CurrentMode        {
            get => _CurrentMode;
            set => Set(ref _CurrentMode, value);
        }

        private ActivityMode GetCurrentActivityMode()
        {
            if ((DateTime.UtcNow.Ticks - lastMouseActivity   ) / 10000 < _IdleTimeout) return ActivityMode.FullActive;
            if ((DateTime.UtcNow.Ticks - lastKeyboardActivity) / 10000 < _IdleTimeout) return ActivityMode.Active;

            return ActivityMode.Idle;
        }

        bool _ShowGPUUsage;
        public bool ShowGPUUsage
        {   get => _ShowGPUUsage;
            set { Set(ref _ShowGPUUsage, value); if (value) StartGPUUsage(); else GPUUsage = ""; }
        }
        
        string _GPUUsage;
        public string GPUUsage
        {   get => _GPUUsage;
            set => Set(ref _GPUUsage, value);
        }
        Thread gpuThread;
        private void StartGPUUsage()
        {
            if (gpuThread != null && gpuThread.IsAlive) { ShowGPUUsage = false; return; }
            gpuThread = new Thread(() =>
            {
                while (_ShowGPUUsage) GPUUsage = Utils.GetGPUUsage().ToString("0.00") + "%";
                ShowGPUUsage = false;
            });
            gpuThread.IsBackground = true; gpuThread.Start();
        }

        private void IdleThread()
        {
            while (_IdleTimeout > 0)
            {
                Thread.Sleep(500);

                var newMode = GetCurrentActivityMode();
                if (newMode != CurrentMode)
                {
                    if (newMode == ActivityMode.Idle && IsFullscreen)
                        Dispatcher.BeginInvoke(new Action(() => { if (popUpMenu.IsOpen || popUpMenuVideo.IsOpen || popUpMenuSubtitles.IsOpen) return; while (ShowCursor(false) >= 0) { } isCursorHidden = true;}));

                    if (isCursorHidden && newMode == ActivityMode.FullActive)
                        Dispatcher.BeginInvoke(new Action(() => { while (ShowCursor(true)   < 0) { } }));

                    CurrentMode = newMode;
                }
            }
        }

        bool isCursorHidden;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);
        #endregion

        #region Events
        private void Flyleaf_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4) { return; }

            Thickness t;

            if (dialogSettingsIdentifier != null && DialogHost.IsDialogOpen(dialogSettingsIdentifier)) return;

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                switch (e.Key)
                {
                    case Key.OemOpenBrackets:
                        AudioConfig.Delay -= 1000 * 10000;
                        break;

                    case Key.OemCloseBrackets:
                        AudioConfig.Delay += 1000 * 10000;
                        break;

                    case Key.OemSemicolon:
                        SubtitlesConfig.Delay -= 1000 * 10000;
                        break;

                    case Key.OemQuotes:
                        SubtitlesConfig.Delay += 1000 * 10000;
                        break;

                    case Key.Up:
                        t = Subtitles.Margin; t.Bottom += 2; Subtitles.Margin = t; Raise(nameof(Subtitles));
                        break;

                    case Key.Down:
                        t = Subtitles.Margin; t.Bottom -= 2; Subtitles.Margin = t; Raise(nameof(Subtitles));
                        break;

                    case Key.C:
                        if (DecCtx == null | DecCtx.UserInputUrl == null) break;

                        Clipboard.SetText(DecCtx.UserInputUrl);
                        break;

                    case Key.R:
                        ToggleRecordAction();
                        
                        break;

                    case Key.S:
                        TakeSnapshotAction();
                        break;

                    case Key.V:
                        Open(Clipboard.GetText());
                        break;

                    case Key.X:
                        Player.decoder.Flush();
                        break;

                    case Key.D0:
                        Player.Zoom = 0; Player.renderer.PanXOffset = 0; Player.renderer.PanYOffset = 0;
                        break;
                }

                e.Handled = true;
                return;
            }
            
            switch (e.Key)
            {
                case Key.Up:
                    if (Player.Volume == 150) return;
                    Player.Volume += Player.Volume + 5 > 150 ? 0 : 5;

                    break;

                case Key.Down:
                    if (Player.Volume == 0) return;
                    Player.Volume -= Player.Volume - 5 < 0 ? 0 : 5;
                    
                    break;

                case Key.Right:
                    int ms = (int) ((Player.CurTime + (5 * 1000 * (long)10000)) / 10000);
                    if (ms <= Player.Duration/10000) Player.Seek(ms, true);
                    break;

                case Key.Left:
                    ms = (int) ((Player.CurTime - (5 * 1000 * (long)10000)) / 10000);
                    Player.Seek(ms > 0 ? ms : 0);
                    break;

                case Key.OemPlus:
                    Config.Player.Speed = Config.Player.Speed == 4 ? 1 : Config.Player.Speed + 1;
                    break;

                case Key.OemMinus:
                    Config.Player.Speed = Config.Player.Speed == 1 ? 4 : Config.Player.Speed - 1;
                    break;

                case Key.OemOpenBrackets:
                    AudioConfig.Delay -= 100 * 10000;
                    break;

                case Key.OemCloseBrackets:
                    AudioConfig.Delay += 100 * 10000;
                    break;

                case Key.OemSemicolon:
                    SubtitlesConfig.Delay -= 100 * 10000;
                    break;

                case Key.OemQuotes:
                    SubtitlesConfig.Delay += 100 * 10000;
                    break;
            }

            e.Handled = true;
        }
        private void Flyleaf_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4) { return; }

            lastKeyboardActivity = DateTime.UtcNow.Ticks;

            if (dialogSettingsIdentifier != null && DialogHost.IsDialogOpen(dialogSettingsIdentifier) && e.Key == Key.Escape) { DialogHost.Close(dialogSettingsIdentifier, "cancel"); return; }

            switch (e.Key)
            {
                case Key.I:
                    lastKeyboardActivity = 0; lastMouseActivity = 0;
                    return;

                case Key.F:
                    ToggleFullscreenAction();
                    break;

                case Key.Space:
                    TogglePlayPauseAction(null);
                    break;

                case Key.Escape:
                    if (IsFullscreen) ToggleFullscreenAction();
                    break;
            }

            e.Handled = true;
        }
        private void Flyleaf_DragEnter(object sender, System.Windows.Forms.DragEventArgs e) { e.Effect = System.Windows.Forms.DragDropEffects.All; }
        private void Flyleaf_DragDrop(object sender, System.Windows.Forms.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string filename = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
                Open(filename);
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string text = e.Data.GetData(DataFormats.Text, false).ToString();
                if (text.Length > 0) Open(text);
            }
        }
        private void Flyleaf_MouseWheel(int delta)
        {
            if (delta == 0 || (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))) return;

            Player.Zoom += delta > 0 ? 50 : -50;
        }
        #endregion

        #region Property Change
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected void Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "")
        {
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value)))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion
    }
}