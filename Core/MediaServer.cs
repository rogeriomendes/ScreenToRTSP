using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ScreenToRTSP.Core
{
    public class MediaServer
    {
        private Process ffmpegProcess;
        private Process mediamtxProcess;
        private StreamConfig currentConfig;
        private JobObject jobObject = new JobObject();
        private string _cachedLocalIp;
        private long _cachedIpTick;

        public bool IsFfmpegRunning => ffmpegProcess != null && !ffmpegProcess.HasExited;
        public bool IsMediaMtxRunning => mediamtxProcess != null && !mediamtxProcess.HasExited;

        public MediaServer()
        {
        }

        public string GetLocalIPAddress()
        {
            long now = Environment.TickCount64;
            if (_cachedLocalIp != null && (now - _cachedIpTick) < 30000)
            {
                return _cachedLocalIp;
            }

            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        _cachedLocalIp = endPoint.Address.ToString();
                        _cachedIpTick = now;
                        return _cachedLocalIp;
                    }
                }
            }
            catch { }

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        _cachedLocalIp = ip.ToString();
                        _cachedIpTick = now;
                        return _cachedLocalIp;
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public void GenerateMediaMtxConfig(StreamConfig config)
        {
            currentConfig = config;
            string configPath = Path.Combine(config.AppDataPath, "mediamtx.yml");
            string yaml = $"logLevel: warn\nrtspAddress: :{config.Port}\nwebrtc: yes\nwebrtcAddress: :8880\nrtmp: no\nhls: no\nsrt: no\npaths:\n  all:\n";
            if (config.UseAuth && !string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
            {
                yaml += $"    publishUser: {config.Username}\n    publishPass: {config.Password}\n    readUser: {config.Username}\n    readPass: {config.Password}\n";
            }
            File.WriteAllText(configPath, yaml);
        }

        private string GetFfmpegPath()
        {
            string local = Path.Combine(MonitorHelper.AppDir, "ffmpeg.exe");
            return File.Exists(local) ? local : Path.Combine(currentConfig.AppDataPath, "ffmpeg.exe");
        }

        private string GetMediamtxPath()
        {
            string local = Path.Combine(MonitorHelper.AppDir, "mediamtx.exe");
            return File.Exists(local) ? local : Path.Combine(currentConfig.AppDataPath, "mediamtx.exe");
        }

        public void LaunchMediaMtx(StreamConfig config)
        {
            currentConfig = config;
            string mediamtxExe = GetMediamtxPath();
            if (mediamtxProcess == null || mediamtxProcess.HasExited)
            {
                mediamtxProcess = new Process();
                mediamtxProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = mediamtxExe,
                    WorkingDirectory = config.AppDataPath,
                    Arguments = "mediamtx.yml",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                mediamtxProcess.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[MediaMTX] " + e.Data); };
                mediamtxProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[MediaMTX] " + e.Data); };
                mediamtxProcess.Start();
                jobObject.AddProcess(mediamtxProcess);
                mediamtxProcess.BeginOutputReadLine();
                mediamtxProcess.BeginErrorReadLine();
            }
        }

        public bool forceGdigrab = false;

        public void LaunchFFmpeg(StreamConfig config)
        {
            currentConfig = config;
            string ffmpegExe = GetFfmpegPath();
            if (ffmpegProcess != null && !ffmpegProcess.HasExited) return;

            string auth = "";
            if (config.UseAuth && !string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
                auth = $"{config.Username}:{config.Password}@";

            string streamName = string.IsNullOrWhiteSpace(config.StreamName) ? "camera1" : config.StreamName.Trim().Trim('/');
            string rtspUrl = $"rtsp://{auth}127.0.0.1:{config.Port}/{streamName}";
            string rtspUrlLow = $"rtsp://{auth}127.0.0.1:{config.Port}/{streamName}_low";

            string bitrate = config.QualityIndex == 0 ? "1M" : (config.QualityIndex == 2 ? "6M" : "3M");
            string encoderArgs = $"-c:v libx264 -preset ultrafast -tune zerolatency -threads 2 -g {config.Fps}";
            if (config.EncoderIndex == 1) encoderArgs = $"-c:v h264_nvenc -preset p1 -tune ull -g {config.Fps}";
            else if (config.EncoderIndex == 2) encoderArgs = $"-c:v h264_qsv -preset veryfast -g {config.Fps}";

            // ddagrab captures a specific DirectX output via Desktop Duplication API (lavfi).
            // For multi-monitor "All Monitors" (MonitorIndex == 0 when >1 monitor exists), gdigrab virtual desktop is required.
            bool useDdaGrab = !forceGdigrab && (config.MonitorIndex > 0 || MonitorHelper.GetMonitors().Count <= 1);
            string drawMouse = config.ShowCursor ? "1" : "0";
            string ffmpegArgs;

            if (useDdaGrab && config.EncoderIndex == 1 && !config.DualStream)
            {
                // Zero-copy GPU pipeline: D3D11va -> ddagrab -> h264_nvenc directly in VRAM
                int outputIdx = Math.Max(0, config.MonitorIndex - 1);
                ffmpegArgs = $"-loglevel warning -stats -stats_period 2.0 -fflags nobuffer -flags low_delay -init_hw_device d3d11va -filter_complex \"ddagrab=output_idx={outputIdx}:framerate={config.Fps}:draw_mouse={drawMouse}\" {encoderArgs} -b:v {bitrate} -rtsp_transport tcp -f rtsp {rtspUrl}";
            }
            else
            {
                string inputArgs;
                if (useDdaGrab)
                {
                    int outputIdx = Math.Max(0, config.MonitorIndex - 1);
                    inputArgs = $"-f lavfi -i \"ddagrab=output_idx={outputIdx}:framerate={config.Fps}:draw_mouse={drawMouse},hwdownload,format=bgra\"";
                }
                else
                {
                    string monitorArgs = $"-draw_mouse {drawMouse} -i desktop";
                    if (config.MonitorIndex > 0)
                    {
                        monitorArgs = $"-offset_x {config.MonitorBounds.X} -offset_y {config.MonitorBounds.Y} -video_size {config.MonitorBounds.Width}x{config.MonitorBounds.Height} " + monitorArgs;
                    }
                    inputArgs = $"-f gdigrab -framerate {config.Fps} {monitorArgs}";
                }

                if (config.DualStream)
                {
                    // Single color-space conversion (BGRA -> YUV420P) shared across both streams + fast_bilinear downscale
                    ffmpegArgs = $"-loglevel warning -stats -stats_period 2.0 -fflags nobuffer -flags low_delay {inputArgs} -filter_complex \"[0:v]format=yuv420p,split=2[vmain][vlow];[vlow]scale=854:480:flags=fast_bilinear[vlowout]\" -map \"[vmain]\" {encoderArgs} -b:v {bitrate} -rtsp_transport tcp -f rtsp {rtspUrl} -map \"[vlowout]\" {encoderArgs} -b:v 500k -rtsp_transport tcp -f rtsp {rtspUrlLow}";
                }
                else
                {
                    ffmpegArgs = $"-loglevel warning -stats -stats_period 2.0 -fflags nobuffer -flags low_delay {inputArgs} {encoderArgs} -b:v {bitrate} -pix_fmt yuv420p -rtsp_transport tcp -f rtsp {rtspUrl}";
                }
            }

            ffmpegProcess = new Process();
            ffmpegProcess.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            ffmpegProcess.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[FFMPEG] " + e.Data); };
            ffmpegProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("[FFMPEG] " + e.Data); };
            ffmpegProcess.Start();
            jobObject.AddProcess(ffmpegProcess);
            ffmpegProcess.BeginOutputReadLine();
            ffmpegProcess.BeginErrorReadLine();

            if (!forceGdigrab)
            {
                var startedProc = ffmpegProcess;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    if (ffmpegProcess == null || !ReferenceEquals(ffmpegProcess, startedProc)) return;
                    if (startedProc.HasExited)
                    {
                        forceGdigrab = true;
                        LaunchFFmpeg(currentConfig);
                    }
                });
            }
        }

        public void StopProcesses()
        {
            var ff = ffmpegProcess;
            ffmpegProcess = null;
            if (ff != null)
            {
                try { if (!ff.HasExited) ff.Kill(); } catch { }
                try { ff.Dispose(); } catch { }
            }

            var mtx = mediamtxProcess;
            mediamtxProcess = null;
            if (mtx != null)
            {
                try { if (!mtx.HasExited) mtx.Kill(); } catch { }
                try { mtx.Dispose(); } catch { }
            }
        }
    }
}
