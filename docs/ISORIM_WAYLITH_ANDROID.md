# IsoRim Waylith en Android: informe de la fase experimental

Fecha: 18 de octubre de 2026. Base del fork: `5071c0f`.
Rama de trabajo: `fix/isorim-waylith-android`.

**Resultado:** APK ARM64 experimental construido con Slang real 2026.19.
Compilador, shaders públicos y empaquetado verificados. **El crash del juego no
se declara reparado:** no se dispone de los archivos del motor de la instalación
del usuario ni de un dispositivo Android en el que ejecutar el juego.

## Causa comprobada y límites del diagnóstico

El `libslang.so` habitual solo exporta `spGetBuildTagString()`. El APK de control
confirma la ausencia de `slang_createGlobalSession2` y del resto de funciones de
compilación. Por tanto, ese binario no puede atender la llamada indicada en el
`EntryPointNotFoundException`. No se trata de una función que deba implementarse
devolviendo éxito: el consumidor necesita objetos de sesión, su ABI, reflexión
de recursos y código compilado real.

IsoRim consultado: commit `5da9e4b2e299bb04282533268f5c2eb055bc4af0` de
[`peren717/IsoRim-1.7`](https://github.com/peren717/IsoRim-1.7).
Tiene 21 ShaderDefs y 18 GLSL, y usa `ShaderDef.Shader` mediante
`R3DMaterials.ShaderOf`. Su código y sus definiciones especifican etapas,
texturas, buffers, transparencias, stencil e instancing. No suministra un pack
SPIR-V propio. Las fuentes públicas incorporan las variantes `USE_TEXTURE` e
`INSTANCED` que deben respetarse.

`Magenta.vert` no es una fuente del mod; el error aportado corresponde a un shader
interno. No está probado qué variante del shader interno falta, qué provoca el
fallo inicial de caché ni la secuencia completa hasta `ERROR [Vulkan] !!!CRASH!!!`.
La ausencia de un compilador está demostrada; un fallo adicional de ABI, caché,
pipeline, dispositivo o controlador todavía requiere evidencia de ejecución.

## Precompilación: resultado y bloqueo

Se priorizó inventariar y compilar las fuentes públicas antes de integrar el
compilador Android. Las 18 fuentes, con sus combinaciones de macros, producen
**23 variantes válidas** con Slang 2026.19 y SPIRV-Tools para Vulkan 1.1/SPIR-V
1.3. Las capacidades declaradas en esos resultados son `Shader` y, en dos
variantes, `ImageQuery`.

Eso demuestra que la precompilación de estas fuentes es posible. **No demuestra
que esos archivos sean entradas utilizables por la caché de Waylith.** Faltan el
algoritmo exacto de claves, los parámetros/variantes efectivos del motor, el
formato serializado y sus datos de reflexión. El inventario deja la lista de
entradas de caché ausentes como desconocida, en vez de deducirla de los nombres.
No se han insertado SPIR-V con claves inventadas.

Se necesitan `Waylith.dll`, `Assembly-CSharp.dll`, `UnityStubs.dll`, `Assets.zip`,
`RimWorld.deps.json` y `RimWorld.runtimeconfig.json` de la misma build para cerrar
este análisis. Si los bindings Slang viven en otra DLL, `deps.json` y las
referencias de `Waylith.dll` permitirán identificarla. El `libslang.so` original
permite contrastar versión/exportaciones. Todo se analizaría localmente, sin
incorporar archivos propietarios al repositorio o al APK.

## Implementación experimental

La alternativa incorpora el código oficial de
[`shader-slang/slang` v2026.19](https://github.com/shader-slang/slang/tree/0fb6b7587a94fa33d88141ec9930766eedc6f59a),
commit `0fb6b7587a94fa33d88141ec9930766eedc6f59a`, con sus dependencias fijadas.
Upstream documenta Android en `docs/building.md`. El tag real devuelto por el
compilador coincide con `2026.19`, el valor que usa actualmente RimDroid.

Se construyen los generadores y el compilador host y después las bibliotecas
Android API 30/ARM64 con NDK 27.0.12077973:

- `libslang.so`: compilador real, 653 símbolos exportados en el binario preparado.
- `libslang-glslang-2026.19.so`: backend GLSL/SPIR-V.
- `libslang-glsl-module-2026.19.so`: módulo GLSL integrado y su tag.

El envolvente CMake adapta el nombre/SONAME del compilador al nombre que importa
Waylith. No cambia la API ni el código fuente de Slang. Los tres ELF usan páginas
de 16 KB y bibliotecas Android (`libc.so`, `libm.so`, `libdl.so`,
`libc++_shared.so`), sin dependencias glibc del host.

Gradle habilita esta ruta solo con `-PdotnetEngine=true -PruntimeSlang=true`.
Sin la opción conserva el stub y excluye los módulos experimentales. Se comprobó
también el build normal después de preparar los binarios nuevos. Las bibliotecas,
licencias y procedencia se generan bajo `app/build/runtime-slang`; no se suben
binarios al repositorio.

La ABI de las interfaces nativas upstream se conserva. **La concordancia con los
bindings administrados de la build del usuario está pendiente**; no puede
deducirse únicamente del nombre de un símbolo o del tag.

## Archivos cambiados

| Archivo | Finalidad |
| --- | --- |
| `app/build.gradle` | Opción experimental, requisito .NET, bibliotecas/assets y sufijo del APK debug. |
| `app/src/main/cpp/CMakeLists.txt` | Omitir el stub únicamente en el build experimental. |
| `app/src/main/cpp/slang_stub/slang_stub.c` | Corregir la descripción de Slang y documentar la limitación del stub. |
| `tools/rimworld17/slang/build.sh` | Compilación fijada, prueba API host, preparación de bibliotecas/licencias/procedencia. |
| `tools/rimworld17/slang/CMakeLists.txt` | Integración externa de Slang sin modificar upstream; nombres y alineación. |
| `tools/rimworld17/slang/api_smoke.cpp` | Prueba de sesión, GLSL, reflexión, SPIR-V y rechazo de fuente inválida. |
| `tools/rimworld17/slang/probe_shaders.py` | Inventario y compilación/validación de fuentes públicas del mod. |
| `tools/rimworld17/slang/verify_apk.py` | Estructura, arquitectura, dependencias, símbolos y alineación. |
| `tools/rimworld17/slang/test_apks.py` | Cuatro regresiones sobre los APK reales. |
| `tools/rimworld17/slang/README.md` | Reproducción, archivos necesarios y procedimiento Android. |
| `docs/ISORIM_WAYLITH_ANDROID.md` | Este informe. |

Los checkouts de IsoRim y Slang permanecen sin modificaciones. Los archivos que
regenera box64 durante la compilación se restauraron al estado previo. No se
modificó el código de audio, el motor de juego ni ningún archivo propietario.

## Pruebas ejecutadas

| Comprobación | Resultado |
| --- | --- |
| API real Slang en Linux x86_64 | Sesión GLSL, reflexión y 740 bytes SPIR-V; código inválido rechazado. |
| Fuentes de IsoRim | 23/23 compilaciones y validaciones SPIRV-Tools correctas. |
| Compilación Slang Android | Tres bibliotecas y ejecutable de prueba ARM64 generados correctamente. |
| Pruebas Java de RimDroid | 19 aprobadas; 0 fallidas, errores u omitidas. |
| APK de control .NET | Compilación correcta; 32 bibliotecas ARM64; stub conservado. |
| APK experimental .NET + Slang | Compilación correcta; 34 bibliotecas ARM64 y símbolos reales comprobados. |
| Regresión de APK | 4/4: ambos modos correctos y rechazo de cada APK bajo el modo contrario. |
| Guardia de configuración | `runtimeSlang=true` sin .NET falla explícitamente, como corresponde. |
| Dependencias ELF | Cubiertas por Android o bibliotecas empaquetadas, incluyendo aliases SONAME existentes. |
| Alineación de los 3 nuevos ELF | Todos los segmentos LOAD alineados a 16 KB. |
| Firma / ZIP APK experimental | `apksigner verify`: firma v2 válida; `zipalign -c -P 16 4`: correcto. |
| Audio | `wa_sound_resident_bytes` presente en ambos APK. |
| Ejecución Android y juego | **No ejecutada.** El ejecutable ARM64 de prueba queda disponible para dispositivo. |

La compilación conserva advertencias preexistentes de C/C++/Java y deprecaciones
Gradle. No se desactivaron comprobaciones para obtener resultados favorables.

## APK e instrucciones

Artefacto: `rimdroid-isorim-slang-experimental.apk`, 147.231.739 bytes.
Paquete `com.rimdroid`, versión `0.3.0v2-isorim-slang-experimental`, versionCode 37,
Android mínimo 30, ARM64. SHA-256:

```text
836deb9a49bf47729b299993d2ceac7a4d908958b3122d6b2221cc72bc417edd
```

Los artefactos se entregan con el informe de esta tarea; no están almacenados en
Git. Se incluye `slang-android-smoke.zip` para comprobar la API nativa sin el juego.

1. Exportar partidas y ajustes y preparar una copia de la instancia de juego.
2. Instalar el APK debug con `adb install -r`. Si la firma no coincide con la
   instalación actual, la actualización se rechazará; preservar los datos antes
   de decidir cómo sustituir esa instalación.
3. Probar primero RimWorld 1.7 sin IsoRim, cargando la colonia y comprobando audio.
4. Activar Harmony compatible e IsoRim; alternar 2D/3D, girar cámara y revisar
   interiores, techos, vegetación, transparencias, agua e instancing. Reiniciar y
   repetir para comprobar compilación inicial y reutilización de caché.
5. Entregar logs completos de ambas ejecuciones y las versiones/GPU/driver. La
   desaparición de `EntryPointNotFoundException` por sí sola no demuestra reparación.

Comandos detallados, prueba nativa y lista razonada de archivos requeridos:
[`tools/rimworld17/slang/README.md`](../tools/rimworld17/slang/README.md).

## Audio y pendientes

El fork ya incluye `wa_sound_resident_bytes` desde `f513dba` (0.3.0v2, adaptación
al build 4981). Su implementación calcula memoria PCM o del stream residente;
no es una función vacía. No se aplicó una corrección de audio adicional. Para
explicar su ausencia en el dispositivo hace falta identificar el APK y la
biblioteca efectivamente cargada.

Quedan pendientes: contrato real de caché y bindings de Waylith; versión exacta
del juego/mod que falla; ejecución de la API ARM64; regresión visual y de audio
sin IsoRim; renderizado con IsoRim en el dispositivo; consumo de memoria y latencia
de la compilación en el teléfono. La opción experimental permanece desactivada
por defecto hasta disponer de esa evidencia.
