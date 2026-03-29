# TR Template for ATAS

Port de la plantilla de TradingView (Traders Reality style) a indicadores custom de ATAS.

[![Platform](https://img.shields.io/badge/Platform-ATAS-1f6feb)](https://atas.net/)
[![Language](https://img.shields.io/badge/Language-C%23-239120)](https://learn.microsoft.com/dotnet/csharp/)
[![Target](https://img.shields.io/badge/Target-net10.0--windows-512bd4)](https://dotnet.microsoft.com/)

---

## Resumen

Este repositorio genera **un solo DLL** (`CustomIndicatorsEma.dll`) que registra **dos indicadores** en ATAS:

| Indicador | Nombre en ATAS | Panel | Objetivo |
|---|---|---|---|
| Principal | `TR_Template` | Grafico principal | Estructura completa del template (EMAs, niveles, sesiones, profile, tabla) |
| Secundario | `PVA Volume` | Panel nuevo | Volumen estilo PVSRA + media movil |

---

## Modulos del indicador principal (`TR_Template`)

| Bloque | Incluye |
|---|---|
| EMAs | 5, 13, 50, 200, 800 + nube EMA50 |
| PVSRA | Coloreo de velas, alertas, Vector Candle Recovery (body/wick) |
| Niveles diarios | Yesterday Hi/Lo, ADR Hi/Lo, ATR Hi/Lo, Daily Open |
| Pivots | Mid pivots M0..M5 |
| Sesiones | Asia, NY, EU Brinks, US Brinks, London opcional |
| Psy semanal | Rangos psicologicos semanales |
| Volumen | VWAP + Volume Profile (histograma, POC, VAH/VAL, labels) |
| Metricas | Tabla TR Metrics (ADR, 3xADR, ADR used, distancias, candle time) |

---

## Modulos del indicador de panel (`PVA Volume`)

1. Histograma de volumen con colores PVSRA.
2. MA sobre volumen (50 por defecto).
3. Ajustes de colores y parametros de MA.

---

## Requisitos

1. Windows + ATAS instalado.
2. SDK de .NET compatible con `net10.0-windows`.
3. Referencias de ATAS disponibles en `C:\Program Files (x86)\ATAS Platform\`.

Referencias usadas por el proyecto:

1. `ATAS.Indicators.dll`
2. `ATAS.Indicators.Technical.dll`
3. `OFT.Attributes.dll`
4. `OFT.Rendering.dll`

Si ATAS esta instalado en otra ruta, actualiza los `HintPath` en [`CustomIndicators.csproj`](./CustomIndicators.csproj).

---

## Instalacion rapida

### 1) Compilar

```powershell
dotnet build .\CustomIndicators.csproj -c Release
```

Salida esperada:

```text
bin\Release\net10.0-windows\CustomIndicatorsEma.dll
```

### 2) Copiar el DLL a ATAS

Carpetas tipicas de indicadores en Windows:

1. `%APPDATA%\ATAS\Indicators`
2. `%USERPROFILE%\Documents\ATAS\Indicators`

Copia rapida:

```powershell
Copy-Item .\bin\Release\net10.0-windows\CustomIndicatorsEma.dll "$env:APPDATA\ATAS\Indicators\CustomIndicatorsEma.dll" -Force
```

Opcional para depuracion:

```powershell
Copy-Item .\bin\Release\net10.0-windows\CustomIndicatorsEma.pdb "$env:APPDATA\ATAS\Indicators\CustomIndicatorsEma.pdb" -Force
```

### 3) Cargar en ATAS

1. Reinicia ATAS.
2. Abre `Indicators`.
3. Anade `TR_Template` al grafico principal.
4. Anade `PVA Volume` si quieres el panel de volumen separado.

---

## Presets y flujo recomendado

`TR_Template` incluye presets en `00. Labels & Quick Settings`:

| Preset | Uso |
|---|---|
| `Full` | Configuracion completa (por defecto) |
| `Clean` | Vista limpia (capas no esenciales ocultas) |
| `Crypto` | Ajuste base para mercado crypto |
| `Forex` | Ajuste base para mercado forex |

Para cambios pesados del profile (`Profile History Days`, `Merge Groups`, `Extra Individual Days`) usa:

`>>> APPLY PROFILE CHANGES <<<`

Esto evita recalculos continuos y reduce lag al editar parametros de profile.

---

## Organizacion de parametros

| Grupo | Contenido |
|---|---|
| `00. Labels & Quick Settings` | preset, clean chart, labels, day shift |
| `01. EMAs` | lineas EMA + nube |
| `02. PVSRA, Alerts & Recovery` | velas, alertas, recovery zones, histograma PVSRA |
| `03. Levels: Yesterday Hi/Lo` | niveles del dia previo |
| `04. Levels: ADR` | ADR high/low |
| `05. Levels: ATR` | ATR high/low |
| `06. Levels: Daily Open` | apertura diaria |
| `07. Levels: Mid Pivots` | M levels |
| `08. Market Sessions` | cajas de sesiones + DST |
| `09. Weekly Psy` | psy ranges |
| `10. Volume, VWAP & Profile` | VWAP + profile |
| `11. TR Metrics Table` | tabla informativa |

---

## Estructura del repo

```text
.
|- CustomIndicators.csproj
|- TRSessionsCorePort.cs
|- tradingview_indicators/
|  |- tradingview_tr.txt
|  |- Redlynx_VProfile.txt
|- _verify/
|- _inspect/
```

---

## Troubleshooting

### El indicador no aparece en ATAS

1. Verifica que copiaste `CustomIndicatorsEma.dll` al path correcto.
2. Reinicia ATAS tras copiar el archivo.
3. Elimina DLLs antiguos con nombres distintos (`CustomIndicators.dll`) si generan conflicto visual.

### El grafico se refresca pero no dibuja

1. Revisa `Clean Chart` y los toggles `Show ...`.
2. Verifica opacidad/colores (pueden quedar casi invisibles segun tema del chart).
3. Confirma `Show Market Profile = true` si esperas ver profile.

### Error al compilar

1. Sin SDK: instala .NET SDK compatible con `net10.0-windows`.
2. Sin referencias ATAS: corrige los `HintPath` del `.csproj`.

---

## Disclaimer

Uso educativo y de analisis tecnico.
No es consejo financiero.
