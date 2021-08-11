﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;

namespace Wpf_Samples
{
    /// <summary>
    /// Sample how to export frames (to .bmp files) from the video decoder
    /// </summary>
    public partial class Sample5_ExportVideoFrames : Window , INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        private string _UserInput;
        public string   UserInput
        {
            get => _UserInput;
            set {  _UserInput = value; OnPropertyChanged(nameof(UserInput)); }
        }

        static string sampleVideo = (Environment.Is64BitProcess ? "../" : "") + "../../../../Sample.mp4";

        Demuxer demuxer;
        VideoDecoder vDecoder;
        Config config;

        public Sample5_ExportVideoFrames()
        {
            Master.RegisterFFmpeg(":2");
            InitializeComponent();
            DataContext = this;

            OpenVideo   = new RelayCommand(OpenVideoAction);
            UserInput   = sampleVideo;
            
            config = new Config();
            config.demuxer.AllowInterrupts = false; // Enable it only for network protocols?
            config.decoder.MaxVideoFrames = 100;    // How much does your CPU/GPU/RAM handles?

            demuxer = new Demuxer(config.demuxer);
            vDecoder = new VideoDecoder(config);
        }
        public ICommand     OpenVideo   { get ; set; }
        public void OpenVideoAction(object param)
        {
            if (string.IsNullOrEmpty(UserInput)) UserInput = sampleVideo;

            // OPEN
            if (demuxer.Open(UserInput) != 0) { MessageBox.Show($"Cannot open input {UserInput}"); return; }
            if (vDecoder.Open(demuxer.VideoStreams[0]) != 0) { MessageBox.Show($"Cannot open the decoder"); return; }


            // TEST CASES HERE

            Case1_ExportAll();
            //Case2_ExportWithStep(10);
            //Case3_ExportCustom();

            // CLOSE
            vDecoder.Dispose();
            demuxer.Dispose();
        }

        public void Case1_ExportAll()
        {
            demuxer.Start();
            vDecoder.Start();

            int curFrame = 0;
            while (vDecoder.IsRunning || vDecoder.Frames.Count != 0)
            {
                if (vDecoder.Frames.Count == 0) { continue; }

                vDecoder.Frames.TryDequeue(out VideoFrame frame);
                SaveFrame(frame, (++curFrame).ToString());
            }
        }

        public void Case2_ExportWithStep(int step)
        {
            vDecoder.Speed = step;
            demuxer.Start();
            vDecoder.Start();

            int curFrame = step;
            while (vDecoder.IsRunning || vDecoder.Frames.Count != 0)
            {
                if (vDecoder.Frames.Count == 0) { continue; }

                vDecoder.Frames.TryDequeue(out VideoFrame frame);
                SaveFrame(frame, (curFrame).ToString());
                curFrame += step;
            }
        }

        public void Case3_ExportCustom()
        {
            SaveFrame(vDecoder.GetFrame(0), "1");
            SaveFrame(vDecoder.GetFrame(9), "10");
            SaveFrame(vDecoder.GetFrame(99), "100");
        }

        public void SaveFrame(VideoFrame frame, string filename)
        {
            Bitmap bmp = vDecoder.Renderer.GetBitmap(frame);
            bmp.Save($"{filename}.bmp");
            bmp.Dispose();
            VideoDecoder.DisposeFrame(frame);
        }
     
    }
}
