# Screen to RTSP

Servidor ligero, práctico y automatizado para capturar la pantalla de tu computadora (Windows) y transmitirla en tiempo real mediante el protocolo **RTSP**.
Ideal para integrar la visualización de la pantalla de tu PC directamente en tu DVR/NVR (Grabador de Video en Red), software de monitoreo de cámaras o VLC.

*🌍 Otros idiomas: [English](README.md) | [Português](README.pt-BR.md)*

## 🚀 Características

* **Captura de Múltiples Monitores**: Elige un monitor específico o captúralos todos simultáneamente.
* **Transmisión Dual (Dual Stream)**: Transmisión simultánea de un canal principal (Alta Resolución) y un canal extra muy ligero (Sub-stream), ideal para visualización en mosaico.
* **Aceleración por Hardware**: Soporte para codificación de video mediante tarjeta gráfica usando **NVIDIA (NVENC)**, **Intel (QSV)** o procesamiento por CPU.
* **Panel Web Remoto**: Controla el estado del servidor (Iniciar/Detener) y accede a las configuraciones desde tu teléfono celular u otra computadora en la red a través del puerto `8889`.
* **Programación Automática**: Establece una hora de inicio y fin. La aplicación inicia y detiene la cámara RTSP automáticamente sin intervención manual.
* **Modo Silencioso (Bandeja del Sistema)**: Puede iniciar automáticamente minimizado con Windows, sin saturar tu escritorio.
* **Seguridad**: Autenticación con usuario y contraseña en la cámara IP RTSP, restringiendo accesos no autorizados.
* **Interfaz Multilingüe**: Disponible en Inglés, Portugués y Español, cargado dinámicamente.

## 🛠️ Cómo Compilar (Para Desarrolladores)

La aplicación descargará automáticamente dos ejecutables de terceros (`ffmpeg.exe` y `mediamtx.exe`) la primera vez que se ejecute, obtenidos directamente desde los lanzamientos oficiales en GitHub. Esto mantiene el ejecutable principal extremadamente ligero.

Si tienes curiosidad sobre los binarios o quieres conocer su origen, se descargan dinámicamente de:
* **FFmpeg**: [Gyan.dev FFmpeg Builds](https://github.com/GyanD/codexffmpeg/releases/latest)
* **MediaMTX** (Servidor RTSP ligero): [bluenviron/mediamtx](https://github.com/bluenviron/mediamtx/releases/latest)

### Generando el Ejecutable
Para generar tu propio archivo `.exe` a partir del código fuente, simplemente haz doble clic en el script:

`build.bat`

Esto compilará el código C# en un único archivo `.exe` independiente.
El ejecutable resultante se guardará en la carpeta **`build`**, la cual se crea automáticamente.

## 📄 Licencia

Este proyecto es de código abierto y está licenciado bajo la Licencia MIT. Siéntete libre de modificar y contribuir.
