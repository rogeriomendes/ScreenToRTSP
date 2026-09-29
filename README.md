# Screen to RTSP

Lightweight, practical, and automated server to capture your computer screen (Windows) and stream it in real-time via the **RTSP** protocol.
Ideal for integrating PC screen viewing directly into your DVR/NVR (Network Video Recorder), camera monitoring software, or VLC.

*🌍 Other languages: [Português](README.pt-BR.md) | [Español](README.es-ES.md)*

## 🚀 Features

* **Multi-Monitor Capture**: Choose a specific monitor or capture all of them.
* **Dual Stream**: Simultaneous transmission of a main channel (High Resolution) and an extra lightweight channel (Sub-stream), ideal for mosaic viewing.
* **Hardware Acceleration**: Support for video encoding via GPU using **NVIDIA (NVENC)**, **Intel (QSV)**, or via Processor (CPU).
* **Remote Web Panel**: Control the server state (Start/Stop) and access settings from your phone or another computer on the network via port `8889`.
* **Automatic Scheduling**: Set a start and end time. The application starts and stops the RTSP camera automatically without manual intervention.
* **Silent Mode (System Tray)**: Can automatically start minimized with Windows, without cluttering your desktop.
* **Security**: Authentication with login and password on the RTSP IP camera, restricting unauthorized access.
* **Multi-Language Interface**: Available in English, Portuguese, and Spanish, dynamically loaded.

## 🛠️ How to Build (For Developers)

The application automatically downloads two third-party executables (`ffmpeg.exe` and `mediamtx.exe`) on its first run directly from their official GitHub releases. This keeps the main executable extremely lightweight.

If you are curious about the binaries or want to know their source, they are dynamically fetched from:
* **FFmpeg**: [Gyan.dev FFmpeg Builds](https://github.com/GyanD/codexffmpeg/releases/latest)
* **MediaMTX** (Lightweight RTSP server): [bluenviron/mediamtx](https://github.com/bluenviron/mediamtx/releases/latest)

### Generating the Executable
To generate your own `.exe` file from the source code, simply double-click the script:

`build.bat`

This will compile the C# code into a single, self-contained `.exe`.
The resulting executable will be saved in the **`build`** folder, which is created automatically.

## 📄 License

This project is open-source and licensed under the MIT License. Feel free to modify and contribute.
