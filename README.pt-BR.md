# Screen to RTSP

Servidor leve, prático e automatizado para capturar a tela do seu computador (Windows) e transmiti-la em tempo real via protocolo **RTSP**.
Ideal para integrar a visualização da tela do seu PC direto no seu DVR/NVR (Gravador de Vídeo em Rede), software de monitoramento de câmeras ou VLC.

*🌍 Outros idiomas: [English](README.md) | [Español](README.es-ES.md)*

## 🚀 Funcionalidades

* **Captura de Múltiplos Monitores**: Escolha um monitor específico ou capture todos eles simultaneamente.
* **Dual Stream**: Transmissão simultânea de um canal principal (Alta Resolução) e um canal extra super leve (Sub-stream), ideal para visualização em mosaico.
* **Aceleração de Hardware**: Suporte para codificação de vídeo via placa de vídeo usando **NVIDIA (NVENC)**, **Intel (QSV)** ou processamento por CPU.
* **Painel Web Remoto**: Controle o status do servidor (Iniciar/Parar) e acesse as configurações pelo celular ou outro computador na rede via porta `8889`.
* **Agendamento Automático**: Configure horários de início e término. O aplicativo inicia e encerra a câmera RTSP automaticamente sem intervenção manual.
* **Modo Silencioso (Bandeja do Sistema)**: Pode iniciar automaticamente minimizado com o Windows, sem poluir sua área de trabalho.
* **Segurança**: Autenticação com usuário e senha na câmera IP RTSP, restringindo acessos não autorizados.
* **Interface Multilíngue**: Disponível em Inglês, Português e Espanhol, carregado dinamicamente.

## 🛠️ Como Compilar (Para Desenvolvedores)

O aplicativo fará o download automático de dois executáveis de terceiros (`ffmpeg.exe` e `mediamtx.exe`) na primeira vez que for executado, buscando diretamente das releases oficiais no GitHub. Isso mantém o executável principal extremamente leve.

Se você está curioso sobre os binários ou quer saber a fonte deles, eles são baixados dinamicamente de:
* **FFmpeg**: [Gyan.dev FFmpeg Builds](https://github.com/GyanD/codexffmpeg/releases/latest)
* **MediaMTX** (Servidor RTSP leve): [bluenviron/mediamtx](https://github.com/bluenviron/mediamtx/releases/latest)

### Gerando o Executável
Para gerar o seu próprio arquivo `.exe` a partir do código-fonte, basta dar um clique duplo no script:

`build.bat`

Isso vai compilar o código em C# em um único arquivo `.exe` autocontido.
O executável resultante será salvo na pasta **`build`**, que é criada automaticamente.

## 📄 Licença

Este projeto é de código aberto e licenciado sob a Licença MIT. Sinta-se à vontade para modificar e contribuir.
