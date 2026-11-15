# G Controller

App mínima y portable para el ratón **G305** (receptor LIGHTSPEED) y los auriculares **G733**, al estilo de G HUB. Un solo `.exe`, sin instalar.

## Qué hace

- **Dispositivos:** tarjetas con foto, batería y estado.
- **G733 · Botón G1:** dos modos, **Predeterminado** y **G-Shift**, con un switch para cambiar. Cada modo tiene su acción: Reproducir/Pausa, Silenciar micrófono, Siguiente/Anterior, Subir/Bajar volumen, Silenciar sonido o Ninguna. Al silenciar el micrófono sale un aviso en pantalla.
- **G733 · Apagado automático** tras 5, 10, 15 o 30 minutos sin sonido (o nunca). Se guarda en el auricular.
- **G733 · Micrófono y voz:** nivel del micrófono, prueba de grabación, preajustes (de fábrica y propios) y procesado de voz: ecualizador de 3 bandas con frecuencia editable (±15 dB), eliminador de ruido (20–400 Hz), reducción de ruido, expansor/bloqueador, eliminación de siseo, compresor (con proporción), limitador y nivel general de salida. La voz procesada se envía a la salida que elijas; para usarla en Discord u OBS hace falta un cable de audio virtual (p. ej. VB-CABLE).
- **G733 · Tono lateral** (0–100 %) e **iluminación** de las dos zonas: Fijo, Respiración, Ciclo de color o Apagado, con color.
- **G305 · DPI** de 200 a 12000 (pasos de 50) con atajos. La tasa de informe se muestra (el ratón la guarda en su memoria interna).
- **Bandeja:** clic derecho muestra cada aparato con `LIGHTSPEED` y su batería, más Dispositivos, Ajustes y Salir.
- **Ventana:** minimizar a la bandeja, maximizar con animación (botón o doble clic en la barra) y cerrar.
- **Ajustes:** iniciar con Windows, iniciar minimizado y buscar actualizaciones.
- **Actualizaciones:** al abrir y cada 6 h busca una release nueva. Si hay, sale un aviso; **Actualizar** baja el `.exe`, lo reemplaza y reinicia la app.

Cierra G HUB mientras usas esta app: si no, los dos controlan los aparatos y el G1 hace dos cosas. La app avisa si G HUB está abierto.

## Uso

1. Baja `GController.exe` de [Releases](../../releases).
2. Ábrelo.

Requiere .NET Framework 4.x (ya viene con Windows 10 y 11).

## Compilar

```bat
build.cmd
```

## Publicar una actualización

1. Cambia `Version` en `src/App.cs` (por ejemplo `1.0.1`).
2. Haz push a `main`.

GitHub Actions (`.github/workflows/build.yml`) compila el `.exe` y crea la release `v1.0.1`. Las apps abiertas la ven y muestran el aviso para actualizar.

## Protocolo

HID++ 2.0, reporte largo (`0x11`, 20 bytes): `[0x11, índice, feature, función<<4 | sw, datos…]`.

| Aparato | Interfaz | Features usadas |
|---|---|---|
| G733 (`046D:0AFE`) | colección `FF43/0202`, índice `0xFF` | `0x1F20` batería (mV, fn 0) y apagado automático (minutos, fn 1/2), `0x8010` G-keys, `0x8300` tono lateral, `0x8070` luces |
| G305 (receptor `046D:C53F`) | colección `FF00/0002`, índice 1–6 | `0x1000` batería, `0x2201` DPI, `0x8060` tasa, `0x0005` nombre |

- G1: `0x8010` función 2 con `1` hace que el botón llegue como evento (byte 0 bit 0 = pulsado) en vez de su función de fábrica. Al salir, la app lo devuelve con `0`.
- Batería del G733: voltaje a % con la curva de descarga que trae G HUB.

## Aviso

Proyecto no oficial. No tiene relación con Logitech. Logitech, G HUB, G305 y G733 son marcas de su dueño. Las fotos de los aparatos vienen de G HUB.
