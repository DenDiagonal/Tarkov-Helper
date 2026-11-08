# Informe Técnico de Diagnóstico: Viewport, Escalado, Zoom y Sistemas de Coordenadas del Minimapa (OverlayMiniMap)

**Componente**: `TarkovHelper/Windows/OverlayMiniMapWindow.xaml` / `OverlayMiniMapWindow.xaml.cs`  
**Referencia**: `TarkovHelper/Pages/Map/MapPage.xaml(.cs)`, `TarkovHelper/Models/Map/MapConfig.cs`  
**Fecha**: 2026-10-21  
**Estado**: Diagnosticado y Plan de Corrección en Ejecución (Bloque 1)

---

## 1. Resumen Ejecutivo

El minimapa flotante (`OverlayMiniMapWindow`) de TarkovHelper presentaba anomalías graves de renderizado, escalado distorsionado, imposibilidad de navegar hacia extracciones en los extremos del mapa y conflictos de interacción con el ratón.

La auditoría técnica identificó cuatro causas raíz principales:

1. **Confusión Crítica de Sistemas de Coordenadas con `SvgBounds`**: En `CalculateBaseFitScale`, `FitMapToWindow` y `ClampMapOffset`, se utilizaban los valores del array `SvgBounds` (extremos del viewBox del documento SVG de la API de Tarkov Market) pasándolos a la función `GameToScreenForPlayer(x, z)` como si fuesen coordenadas de juego en metros. Dado que `GameToScreenForPlayer` aplica la matriz afín del jugador (`PlayerMarkerTransform`), el resultado era una caja delimitadora en píxeles totalmente errónea y comprimida. En mapas como Customs, el ancho útil calculado era de solo 1040 px en vez de los 4400 px reales del canvas, lo que multiplicaba la escala base por más de 4.2× y bloqueaba el desplazamiento hacia extracciones periféricas clave (Crossroads, Trailer Park, ZB-1011).
2. **Truncamiento por `Math.Max` (UniformToFill) en lugar de Ajuste Proporcional Seguro (`Math.Min`)**: `CalculateBaseFitScale` utilizaba `Math.Max(scaleX, scaleY)` para rellenar la ventana sin márgenes. En ventanas no cuadradas, este comportamiento cortaba hasta un 28% de la superficie útil del mapa fuera de los bordes de la ventana.
3. **Ausencia Absoluta de Soporte para la Rueda del Ratón (`MouseWheel`)**: No existía suscripción a eventos de rueda en `OverlayMiniMapWindow.xaml` ni en su code-behind, impidiendo al usuario acercar o alejar el mapa intuitivamente con el scroll.
4. **Conflicto de Interacción entre `DragMove()` y el Paneo del Mapa**: `MapContainer_MouseLeftButtonDown` comprobaba si el zoom era 1.0 (`isFitted`) o si el modo era `PlayerTracking` y ejecutaba `DragMove()`, moviendo la ventana del sistema operativo en lugar de permitir el paneo continuo del mapa.

---

## 2. Arquitectura de Sistemas de Coordenadas

Para comprender el origen de la corrupción, es indispensable delimitar los tres marcos de coordenadas que conviven en la aplicación:

| Sistema de Coordenadas | Unidad / Rango | Representación en Código | Métodos de Transformación |
|---|---|---|---|
| **Mundo de Juego (EFT World)** | Metros ($X, Y, Z$) donde $X$ = Este/Oeste, $Y$ = Altitud, $Z$ = Norte/Sur. | `EftPosition`, marcadores ($X, Z$) o ($X, Y, Z$). | Memoria del juego / Web API. |
| **Píxeles de Canvas del Mapa** | Píxeles $(0 \dots \text{ImageWidth}, 0 \dots \text{ImageHeight})$. P.ej. Customs: $4400 \times 3200$, Woods: $4800 \times 4800$. | `MapCanvas`, `MapSvg`, `MapMarkersContainer`. | **Directa**: `_currentMapConfig.GameToScreenForPlayer(x, z)`<br/>**Inversa**: `_currentMapConfig.ScreenToGameForPlayer(px, py)` |
| **Viewport / Ventana de Usuario** | Píxeles de pantalla $(0 \dots \text{viewWidth}, 0 \dots \text{viewHeight})$ en `MapContainer` (ancho $\times$ (alto - 28px título)). | Eventos de ratón (`e.GetPosition(MapContainer)`). | $\text{ViewportX} = \text{CanvasX} \times \text{Scale} + \text{OffsetX}$<br/>$\text{ViewportY} = \text{CanvasY} \times \text{Scale} + \text{OffsetY}$ |

### La Jerarquía Visual de `MapCanvas`
En `OverlayMiniMapWindow.xaml`, `MapSvg` (control `SharpVectors.SvgViewbox`) y todos los contenedores de marcadores son hijos directos de `MapCanvas`:
```csharp
MapSvg.Width = _currentMapConfig.ImageWidth;
MapSvg.Height = _currentMapConfig.ImageHeight;
MapCanvas.Width = _currentMapConfig.ImageWidth;
MapCanvas.Height = _currentMapConfig.ImageHeight;
```
Esto demuestra de manera matemática e inequívoca que:
- Las dimensiones canónicas del lienzo en el espacio no escalado son **estrictamente $[0 \dots \text{ImageWidth}]$ y $[0 \dots \text{ImageHeight}]$**.
- `GameToScreenForPlayer` transforma coordenadas de juego directamente a este espacio $[0 \dots \text{ImageWidth}, 0 \dots \text{ImageHeight}]$.
- Cualquier referencia a `SvgBounds` en el espacio de renderizado de `MapCanvas` es conceptualmente inválida.

---

## 3. Desglose del Fallo de `SvgBounds`

### 3.1 Origen de `SvgBounds`
`SvgBounds` fue introducido en `MapConfig` para convertir las coordenadas del documento SVG vectorial de la API de Tarkov Market al canvas de la imagen mediante interpolación lineal:
$$\text{normalizedX} = \frac{\text{svgX} - \text{SvgBounds}[0]}{\text{SvgBounds}[1] - \text{SvgBounds}[0]}$$
$$\text{screenX} = \text{normalizedX} \times \text{ImageWidth}$$
Donde:
- `SvgBounds[0]` = `minX` del SVG
- `SvgBounds[1]` = `maxX` del SVG
- `SvgBounds[2]` = `minY` del SVG
- `SvgBounds[3]` = `maxY` del SVG

### 3.2 El Error de Implementación en `OverlayMiniMapWindow`
En `CalculateBaseFitScale` (líneas 578–591), `FitMapToWindow` (líneas 617–623) y `ClampMapOffset` (líneas 1399–1407), el código hacía:
```csharp
var (minPixelX, minPixelY) = _currentMapConfig.GameToScreenForPlayer(_currentMapConfig.SvgBounds[0], _currentMapConfig.SvgBounds[2]);
var (maxPixelX, maxPixelY) = _currentMapConfig.GameToScreenForPlayer(_currentMapConfig.SvgBounds[1], _currentMapConfig.SvgBounds[3]);
```
`GameToScreenForPlayer` aplica la matriz afín del jugador:
$$\text{screenX} = A \cdot x + B \cdot z + C$$
$$\text{screenY} = D \cdot x + E \cdot z + F$$
Al introducir las dimensiones del archivo SVG como si fuesen coordenadas espaciales en metros dentro de Tarkov, se producían transformaciones matemáticas aberrantes.

### 3.3 Casos de Estudio por Mapa

#### 1. Customs (`ImageWidth=4400`, `ImageHeight=3200`)
- `PlayerMarkerTransform` = `[-2.0, 0, 0, 2.0, 2600, 1601.5]`
- `SvgBounds` = `[-280, 240, -360, 690]`
- Evaluación:
  - `GameToScreenForPlayer(-280, -360)` $\rightarrow$ $\text{screenX} = -2.0 \times (-280) + 2600 = 3160$, $\text{screenY} = 2.0 \times (-360) + 1601.5 = 881.5$
  - `GameToScreenForPlayer(240, 690)` $\rightarrow$ $\text{screenX} = -2.0 \times (240) + 2600 = 2120$, $\text{screenY} = 2.0 \times (690) + 1601.5 = 2981.5$
- **Resultado erróneo**:
  - `boundsWidth = |2120 - 3160| = 1040` (cuando el lienzo real mide **4400 px**).
  - `boundsHeight = |2981.5 - 881.5| = 2100` (cuando el lienzo real mide **3200 px**).
  - La escala base calculada resultaba ser **4.23 veces superior a la real**.
  - `ClampMapOffset` limitaba el eje $X$ exclusivamente al intervalo $[2120, 3160]$. Todo el sector izquierdo (Crossroads, Trailer Park, Big Red: píxeles $0 \dots 2120$) y todo el sector derecho (ZB-1011, Silos: píxeles $3160 \dots 4400$) quedaban físicamente inalcanzables fuera de la pantalla.

#### 2. Woods (`ImageWidth=4800`, `ImageHeight=4800`)
- `PlayerMarkerTransform` = `[-2, 0, 0, 2, 2200, 2841.5]`
- `SvgBounds` = `[-900, 470, -770, 620]`
- Bounds calculados: $X \in [1260, 4000]$, ancho 2740 px frente a 4800 px reales.
- Escala inflada un 175%, bloqueando la visualización de las extracciones perimetrales (UN Roadblock, Outskirts).

#### 3. Factory (`ImageWidth=3600`, `ImageHeight=3600`)
- `PlayerMarkerTransform` con permutación de ejes (`[0, -10, -10, 0, 1800, 1851.5]`).
- Los límites calculados abarcaban únicamente $X \in [850, 2600]$, provocando que más del 52% del mapa quedara recortado.

#### 4. Shoreline (`ImageWidth=3700`, `ImageHeight=3100`)
- Límites calculados: ancho 1000 px frente a 3700 px reales.
- Más del 73% del mapa quedaba fuera del alcance de paneo, imposibilitando ver extracciones como Tunnel o Road to Customs.

---

## 4. Análisis de Relación de Aspecto: `Math.Max` vs. `Math.Min`

En `CalculateBaseFitScale`:
```csharp
// Código original defectuoso
return Math.Max(scaleX, scaleY);
```
- `Math.Max` equivale al modo `Stretch="UniformToFill"`. Ajusta la escala para que la dimensión menor de la imagen cubra toda la ventana, provocando que la dimensión mayor desborde los márgenes visibles.
- En un mapa panorámico como Customs ($4400 \times 3200$) en una ventana de $300 \times 272$:
  - $\text{scaleX} = 300 / 4400 = 0.06818$
  - $\text{scaleY} = 272 / 3200 = 0.08500$
  - $\text{Math.Max} = 0.08500 \rightarrow$ Ancho renderizado $= 4400 \times 0.08500 = 374\text{ px}$.
  - En una ventana de 300 px, **74 px (el 20% del mapa) se corta fuera de la ventana**.
- **Solución implementada**: Utilizar `Math.Min(scaleX, scaleY)` (equivalente a `Stretch="Uniform"`). La dimensión mayor determina el ajuste, asegurando que el 100% del mapa quede visible sin recortes, centrando el eje restante con márgenes seguros.

### Protección contra División por Cero, NaN e Invariantes Numéricos
Al inicializar la ventana o redimensionarla a tamaños mínimos:
- `MapContainer.ActualWidth` o `ActualHeight` pueden reportar $0$ antes del primer ciclo de composición de WPF.
- La deducción de la barra de título (`ActualHeight - 28`) puede producir valores negativos o cero.
- Se implementan salvaguardas estrictas con `Math.Max(50.0, ...)` y comprobaciones de `double.IsFinite(...)` para evitar la propagación de valores `NaN` o $\pm\infty$ hacia las propiedades `MapTranslate` y `MapScale`.

---

## 5. Control de Zoom y Paneo Ergonómico

### 5.1 Rueda del Ratón (`PreviewMouseWheel`)
Se registra el evento tunelizado `PreviewMouseWheel="MapContainer_PreviewMouseWheel"` en `MapContainer` para interceptar la rueda antes de que cualquier control secundario la absorba.

El algoritmo de zoom opera en dos modalidades adaptativas:

1. **Modo Libre / Fijo (`MiniMapViewMode.Fixed`)**:
   El zoom pivota alrededor de la posición exacta del cursor del ratón en el canvas:
   $$\text{canvasX} = \frac{\text{mousePos.X} - \text{MapTranslate.X}}{\text{oldScale}}$$
   $$\text{canvasY} = \frac{\text{mousePos.Y} - \text{MapTranslate.Y}}{\text{oldScale}}$$
   $$\text{newOffsetX} = \text{mousePos.X} - (\text{canvasX} \times \text{newScale})$$
   $$\text{newOffsetY} = \text{mousePos.Y} - (\text{canvasY} \times \text{newScale})$$

2. **Modo Seguimiento de Jugador (`MiniMapViewMode.PlayerTracking`)**:
   El zoom pivota manteniendo al jugador en el centro geométrico de la ventana:
   $$\text{newOffsetX} = \frac{\text{viewWidth}}{2} - (\text{playerPos.X} \times \text{newScale})$$
   $$\text{newOffsetY} = \frac{\text{viewHeight}}{2} - (\text{playerPos.Y} \times \text{newScale})$$

En ambos modos, los nuevos desplazamientos se filtran mediante `ClampMapOffset` y se acotan los límites de zoom entre `OverlayMiniMapSettings.MinZoom` (0.50) y `MaxZoom` (4.00).

### 5.2 Desacoplamiento de `DragMove()` y Paneo
En el código original, `MapContainer_MouseLeftButtonDown` ejecutaba `DragMove()` cuando el zoom estaba próximo a 1.0. Esto convertía cualquier clic en el mapa en un arrastre de la ventana de Windows.

**Corrección**:
- La barra de título (`TitleBar`) es la responsable única del arrastre de la ventana mediante `DragMove()`.
- El área de `MapContainer` responde de manera predecible al clic izquierdo iniciando siempre el paneo (`_isPanning = true`).
- Si el usuario arrastra el mapa mientras está en modo `PlayerTracking`, la vista cambia automáticamente a modo `Fixed` y activa el botón de recentrado (`BtnRecenter`), ofreciendo una experiencia fluida e intuitiva sin sacudidas de ventana.

---

## 6. Persistencia del Nivel de Zoom al Redimensionar la Ventana

Anteriormente, cada evento de redimensionado (`OnSizeChanged`) invocaba incondicionalmente `FitMapToWindow`, reseteando `_settings.ZoomLevel = 1.0` y anulando el zoom elegido por el usuario.

**Corrección**:
- En `OnSizeChanged`, si el usuario tiene un zoom activo ($\neq 1.0$), se preserva `_settings.ZoomLevel`.
- Se reajustan y limitan los desplazamientos `MapOffsetX` y `MapOffsetY` acordes a las nuevas dimensiones de la ventana con `ClampMapOffset`.
- Únicamente se fuerza el reajuste a 1.0 si la ventana ya se encontraba previamente en estado ajustado al 100%.

---

## 7. Matriz de Cambios y Plan de Implementación (Bloque 1)

| Función / Defecto | Comportamiento Previo | Causa Raíz | Solución Implementada |
|---|---|---|---|
| `CalculateBaseFitScale` | Escala inflada hasta 4.2×, mapa recortado | Uso de `SvgBounds` en `GameToScreenForPlayer` y `Math.Max` | Eliminación de `SvgBounds`, uso de `ImageWidth`/`Height` con `Math.Min` y guardas finitas. |
| `FitMapToWindow` | Centro desfasado, recortes perimetrales | Centroide calculado a partir de `SvgBounds` distorsionado | Centrado directo en `(ImageWidth / 2, ImageHeight / 2)`. |
| `ClampMapOffset` | Bloqueo de pan, extracciones inalcanzables | Clamping contra píxeles ficticios derivados de `SvgBounds` | Clamping contra $[0, \text{ImageWidth}]$ y $[0, \text{ImageHeight}]$ en escala efectiva. |
| Zoom Rueda Ratón | Inexistente en minimapa | Falta de evento `PreviewMouseWheel` | Implementado con zoom pivoteado (cursor en Fixed, jugador en Tracking). |
| Arrastre en Clic Izquierdo | Clic en mapa movía la ventana del sistema | `DragMove()` llamado en `MapContainer_MouseLeftButtonDown` | `DragMove()` eliminado del mapa; clic izquierdo asignado exclusivamente a paneo. |
| Redimensionado de Ventana | Zoom reiniciado a 1.0 al redimensionar | `OnSizeChanged` llamaba a `FitMapToWindow` sin verificar estado | Preserva `ZoomLevel` existente y recalcula límites con `ClampMapOffset`. |

---

## 8. Conclusión

Con la eliminación de la distorsión de `SvgBounds` y la unificación de los algoritmos de ajuste con `Math.Min`, el minimapa adquiere un sistema de proyección geométricamente exacto, proporcional y libre de desbordamientos. La adición del control de zoom fluido y la separación estricta entre el arrastre de la ventana y el paneo del mapa restauran la ergonomía y usabilidad requeridas para el uso táctico en juego.
