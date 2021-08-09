﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

namespace FlyleafLib.MediaFramework.MediaRemuxer
{
    public unsafe class Remuxer
    {
        public int          UniqueId        { get; set; }
        public bool         Disposed        { get; private set; } = true;
        public string       Filename        { get; private set; }
        public bool         HasStreams      => mapInOutStreams.Count > 0;
        public bool         HeaderWritten   { get; private set; }


        AVFormatContext*        fmtCtx;
        AVOutputFormat*         fmt;

        Dictionary<IntPtr, IntPtr>
                                mapInOutStreams = new Dictionary<IntPtr, IntPtr>();
        Dictionary<int, IntPtr>
                                mapInInStream = new Dictionary<int, IntPtr>();
        

        public Remuxer(int uniqueId = -1)
        {
            UniqueId= uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
        }

        public int Open(string filename)
        {
            int ret;
            Filename = filename;

            fixed (AVFormatContext** ptr = &fmtCtx)
                ret = avformat_alloc_output_context2(ptr, null, null, Filename);

            if (ret < 0) return ret;

            fmt = fmtCtx->oformat;
            Disposed = false;

            return 0;
        }

        public int AddStream(AVStream* in_stream)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            int ret = -1;

            if (in_stream == null || (in_stream->codec->codec_type != AVMEDIA_TYPE_VIDEO && in_stream->codec->codec_type != AVMEDIA_TYPE_AUDIO)) return ret;
            
            AVStream *out_stream;
            AVCodecParameters *in_codecpar = in_stream->codecpar;

            out_stream = avformat_new_stream(fmtCtx, null);
            if (out_stream == null) return -1;

            ret = avcodec_parameters_copy(out_stream->codecpar, in_codecpar);
            if (ret < 0) return ret;

            if ((fmt->flags & AVFMT_GLOBALHEADER) != 0)
                out_stream->codec->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

            out_stream->codecpar->codec_tag = 0;

            mapInOutStreams.Add((IntPtr)in_stream, (IntPtr)out_stream);
            mapInInStream.Add(in_stream->index, (IntPtr)in_stream);

            return 0;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public int WriteHeader()
        {
            if (mapInOutStreams.Count == 0) throw new Exception("No streams have been configured for the remuxer");

            int ret;

            ret = avio_open(&fmtCtx->pb, Filename, AVIO_FLAG_WRITE);
            if (ret < 0) { avformat_free_context(fmtCtx); return ret; }

            ret = avformat_write_header(fmtCtx, null);
            if (ret < 0) { avformat_free_context(fmtCtx); return ret; }

            HeaderWritten = true;

            return 0;
        }

        public int Write(AVPacket* packet)
        {
            AVStream* in_stream     =  (AVStream*) mapInInStream[packet->stream_index];
            AVStream* out_stream    =  (AVStream*) mapInOutStreams[(IntPtr)in_stream];

            packet->pts             = av_rescale_q_rnd(packet->pts, in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
            packet->dts             = av_rescale_q_rnd(packet->dts, in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
            packet->duration        = av_rescale_q(packet->duration,in_stream->time_base, out_stream->time_base);
            packet->stream_index    = out_stream->index;
            packet->pos             = -1;

            int ret = av_interleaved_write_frame(fmtCtx, packet);
            av_packet_free(&packet);
            return ret;
        }

        public int WriteTrailer() { return Dispose(); }
        public int Dispose()
        {
            if (Disposed) return -1;

            int ret = 0;

            if (HeaderWritten)
            {
                ret = av_write_trailer(fmtCtx);
                avio_closep(&fmtCtx->pb);
            }

            avformat_free_context(fmtCtx);

            fmtCtx = null;
            Filename = null;
            Disposed = true;
            HeaderWritten = false;
            mapInOutStreams.Clear();
            mapInInStream.Clear();

            return ret;
        }
        
        private void Log (string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Remuxer] {msg}"); }
    }
}