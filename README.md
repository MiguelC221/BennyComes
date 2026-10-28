# BenyComes Music Player — v1.2.004 (Release Final)

Reproductor de música de escritorio para Windows, hecho en **C# / .NET 8 + WPF** (`net8.0-windows10.0.19041.0`) con **WPF-UI (Fluent Design 2)**. Está pensado para reproducción de alta fidelidad (**WASAPI Exclusive / Bit-Perfect**) y una interfaz inmersiva inspirada en Groove Music.

> [!IMPORTANT]
> ### Aviso sobre este proyecto
> - **Este proyecto está "vibecodeado":** fue construido casi por completo conversando con una IA, sin un proceso de ingeniería tradicional. Puede contener errores, código mejorable y casos límite sin probar.
> - **Es una única versión:** `v1.2.004` es la versión definitiva. **No habrá actualizaciones ni versiones posteriores**, ni soporte continuo. Se publica tal cual, sin garantías.
> - Siéntete libre de hacer un fork y modificarlo por tu cuenta.

---

## Características

### Motor de audio
- **WASAPI Exclusive (Bit-Perfect)** con `NAudio`, sincronización por eventos y baja latencia.
- Negociación del formato nativo con el hardware (`AudioClient.IsFormatSupported`), sin remuestreo.
- En modo exclusivo no se aplica atenuación digital de volumen, para conservar la señal hacia el DAC.
- Si el dispositivo está ocupado, cambia a **WASAPI Shared** automáticamente.
- Selector de dispositivo de salida (DACs USB, altavoces, auriculares).
- **Ecualizador** y **visualizador de audio** integrados.
- Formatos: **FLAC** (hasta 24-bit / 192 kHz), **OGG Vorbis**, **WAV**, **MP3**, **M4A**, **AAC**, **WMA**.

### Integración con Windows
- **Teclas multimedia** (Play/Pausa, Siguiente, Anterior) y **SMTC** (tarjeta multimedia de Windows con título, artista, álbum y carátula).
- Barra de título personalizada con efecto **Mica**.
- La app **recuerda el tamaño, la posición y si estaba maximizada** al cerrarse.
- Reanuda la última canción, la posición y la cola al reabrir.
- Soporta **arrastrar y soltar** archivos y carpetas para añadir música.

### Navegación
- **Inicio:** saludo según la hora, "Reanudar escucha", "Mix diario aleatorio" y **Mixes inteligentes** (Tus favoritos, Más escuchadas, Joyas olvidadas y mixes por género). Las tarjetas se adaptan al ancho de la ventana. Incluye estadísticas de escucha por mes, año o histórico.
- **Mi música:** pestañas de Canciones, Artistas y Álbumes, con filtros, orden y vistas de detalle.
- **Reproducciones recientes:** álbumes y pistas escuchados recientemente.
- **Búsqueda unificada:** canciones, artistas y álbumes en una sola página, sobre la base de datos ya indexada.
- **Reproducción en curso:** vista inmersiva con fondo desenfocado y *crossfade* entre canciones, carátula grande y datos técnicos en tiempo real (formato, frecuencia, bits, canales y modo de salida).

### Cola de reproducción
- Cola actual con menú contextual completo: reproducir ahora, reproducir siguiente, ir a artista, ir a álbum, ver información de la pista y añadir a lista.
- Al abrir la cola se desplaza a la canción que suena, y hay un botón **"Ir a la canción actual"**.
- Reordenar, quitar y limpiar la cola, o guardarla como lista de reproducción.
- Aleatorio y repetición (Off / Todo / Una).

### Listas de reproducción
- Crear, renombrar y eliminar listas, con descripción y **portada propia** o **collage automático** de carátulas.
- **Reordenar** pistas con botones ▲ / ▼ (el orden se guarda).
- Reproducir una lista (o una pista de ella) usa **solo las canciones de esa lista** como cola.
- **Exportar a `.m3u8` / `.m3u`** (UTF-8, con `#EXTINF` y rutas absolutas).
- Sección plegable de **canciones sugeridas** según artistas y géneros de la lista.
- Aviso si añades una pista **duplicada** (indica cuántas veces ya está).
- Al eliminar una lista, navega a la contigua o a Inicio.
- Menú **"Añadir a lista de reproducción"** disponible en todos los menús contextuales y de 3 puntos.

### Biblioteca e indexador
- **Administración de carpetas** de música: agregar, quitar (limpia sus canciones de la base de datos, con confirmación) y re-escanear todo.
- **Indexador inteligente de artistas** que separa artistas compuestos (`,`, `feat.`, `ft.`, `&`...).
  - Lista editable de **separadores** y de **excepciones** (como `AC/DC`), con buscador, alta y baja desde ventanas modales.
- Metadatos con **TagLibSharp** (ID3v2, Vorbis, FLAC) y carátulas embebidas o `cover.jpg` / `folder.jpg`.
- Carátula generada si la pista no tiene ninguna.
- Base de datos **SQLite** (modo WAL) en `%LocalAppData%\BenyComes\`.

### Interfaz
- Temas y color de acento configurables.
- Barra de reproducción inferior con seekbar y volumen de un solo clic.
- Diálogos modales y notificaciones flotantes con cierre automático.

---

## Estructura del proyecto

```
BenyComes/
├── BenyComes.csproj           # Versión 1.0.0, net8.0-windows10.0.19041.0
├── installer.iss              # Script de Inno Setup
├── MainWindow.xaml / .cs      # Ventana principal y navegación
├── Models/                    # Track, Playlist, AlbumModel, ArtistModel...
├── Data/                      # SQLite: DatabaseContext, LibraryDao, PlaylistDao
├── Services/
│   ├── Audio/                 # Motor WASAPI, decodificadores, EQ, visualizador, SMTC
│   ├── Library/               # Caché de biblioteca e indexador de artistas
│   └── Metadata/              # Lectura de etiquetas y carátulas
├── ViewModels/                # MVVM (CommunityToolkit.Mvvm)
├── Views/                     # Inicio, Mi música, Búsqueda, Listas, Ajustes...
├── Converters/                # Convertidores XAML
└── Helpers/                   # Utilidades (generador de audio de prueba)
```

---

## Compilar y ejecutar

**Requisitos:** Windows 10 (19041+) o Windows 11 y .NET 8 SDK.

```powershell
# Ejecutar en desarrollo
dotnet run -c Release

# Publicar (autocontenido, un solo archivo)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Crear el instalador
Con [Inno Setup](https://jrsoftware.org/isinfo.php) instalado:

```powershell
& "C:\Program Files\Inno Setup 7\ISCC.exe" installer.iss
```

El instalador se genera en `installer_output\BenyComesMusic_Installer_v1.2.004.exe`.

---

## Tecnologías

.NET 8 • WPF • WPF-UI (Fluent 2) • CommunityToolkit.Mvvm • NAudio / NAudio.Vorbis • CSCore • TagLibSharp • Microsoft.Data.Sqlite

---

## Licencia y garantía

Se distribuye **tal cual, sin garantía de ningún tipo** y sin soporte. Es una versión única (`v1.2.004`) y no recibirá mantenimiento.
