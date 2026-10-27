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

        public bool IsFfmpegRunning => ffmpegProcess != null && !ffmpegProcess.HasExited;
        public bool IsMediaMtxRunning => mediamtxProcess != null && !mediamtxProcess.HasExited;

        public MediaServer()
        {
        }

        public string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public void GenerateMediaMtxConfig(StreamConfig config)
        {
            currentConfig = config;
            string configPath = Path.Combine(config.AppDataPath, "mediamtx.yml");
            string yaml = $"rtspAddress: :{config.Port}\nwebrtc: yes\nwebrtcAddress: :8880\nhls: yes\nhlsAddress: :8888\npaths:\n  all:\n";
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
            string encoderArgs = $"-c:v libx264 -preset ultrafast -tune zerolatency -g {config.Fps}";
            if (config.EncoderIndex == 1) encoderArgs = $"-c:v h264_nvenc -preset p1 -tune ull -g {config.Fps}";
            else if (config.EncoderIndex == 2) encoderArgs = $"-c:v h264_qsv -preset veryfast -g {config.Fps}";

            string monitorArgs = $"-draw_mouse {(config.ShowCursor ? "1" : "0")} -i desktop";
            if (config.MonitorIndex > 0)
            {
                monitorArgs = $"-offset_x {config.MonitorBounds.X} -offset_y {config.MonitorBounds.Y} -video_size {config.MonitorBounds.Width}x{config.MonitorBounds.Height} " + monitorArgs;
            }

            string dualStreamArgs = "";
            if (config.DualStream) dualStreamArgs = $" {encoderArgs} -b:v 500k -s 854x480 -pix_fmt yuv420p -f rtsp {rtspUrlLow}";

            string captureFormat = forceGdigrab ? "gdigrab" : "ddagrab";
            string ffmpegArgs = $"-f {captureFormat} -framerate {config.Fps} {monitorArgs} {encoderArgs} -b:v {bitrate} -pix_fmt yuv420p -f rtsp {rtspUrl}{dualStreamArgs}";

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
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    if (ffmpegProcess == null) return;
                    if (ffmpegProcess.HasExited)
                    {
                        forceGdigrab = true;
                        LaunchFFmpeg(currentConfig);
                    }
                });
            }
        }

        public void StopProcesses()
        {
            try { if (ffmpegProcess != null && !ffmpegProcess.HasExited) ffmpegProcess.Kill(); } catch { } finally { ffmpegProcess = null; }
            try { if (mediamtxProcess != null && !mediamtxProcess.HasExited) mediamtxProcess.Kill(); } catch { } finally { mediamtxProcess = null; }
            if (currentConfig != null)
            {
                foreach (var p in Process.GetProcessesByName("ffmpeg"))
                    try { if (p.MainModule.FileName == Path.Combine(currentConfig.AppDataPath, "ffmpeg.exe")) p.Kill(); } catch { }
                foreach (var p in Process.GetProcessesByName("mediamtx"))
                    try { if (p.MainModule.FileName == Path.Combine(currentConfig.AppDataPath, "mediamtx.exe")) p.Kill(); } catch { }
            }
        }
    }
}
