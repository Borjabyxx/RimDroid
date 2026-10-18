# Slang real para Waylith: experimento Android ARM64

Esta opción elimina la limitación del stub de compilación; **no acredita todavía
que IsoRim funcione en Android**. Hace falta validar la ABI de las DLL de la
compilación concreta de RimWorld y ejecutar el juego con y sin el mod en un
dispositivo. No se cambian las funciones de audio ni se añaden exportaciones
ficticias. La compilación habitual conserva el modo de caché existente.

## Qué se ha comprobado en el código

- El stub solo exporta `spGetBuildTagString()`, con `2026.19`. No puede satisfacer
  `slang_createGlobalSession2`, ni crear sesiones, reflejar recursos o compilar.
- IsoRim `5da9e4b2e299bb04282533268f5c2eb055bc4af0` (`peren.isorim17`) contiene
  21 `ShaderDef` y 18 archivos GLSL. `R3DMaterials.ShaderOf` obtiene `ShaderDef.Shader`;
  no carga una biblioteca Slang propia ni un pack SPIR-V propio. Los materiales
  y el renderizador de Waylith se encargan del resto.
- Los shaders de IsoRim añaden recursos, constantes, texturas, estados de stencil
  y variantes `INSTANCED`/`USE_TEXTURE`. Copiar un `.spv` con el nombre del GLSL no
  demuestra que el motor encuentre su clave de caché ni su layout de recursos.
- Los shaders externos citados explícitamente son `LightOverlay.frag.glsl`,
  `Silhouette.frag.glsl`, `Transparent.vert.glsl` y `Transparent.frag.glsl`.
  Hay también etapas vertex por defecto. `Magenta.vert` es un shader interno
  mencionado en el diagnóstico del usuario, no un archivo de este mod.
- Sin `Assets.zip` ni el código IL del motor no se conoce la lista exacta de
  entradas ausentes. El comentario del stub describe un hash que incorpora la
  versión de Waylith y el tag Slang; el algoritmo completo, serialización,
  parámetros de compilación y reglas de invalidación no están en RimDroid.
  El fallo de compilación está explicado; la cadena exacta hasta el crash Vulkan
  necesita el log completo y la ejecución. No se atribuye un device-lost al shader
  únicamente por proximidad en un log.

## Precompilación antes de integrar una caché

`probe_shaders.py` lee un checkout local del mod, inventaría las definiciones y
explora las combinaciones de macros usadas en sus fuentes. Opcionalmente compila
con Slang y valida con SPIRV-Tools para Vulkan 1.1 / SPIR-V 1.3. **Sus resultados
son pruebas independientes del compilador, no entradas listas para la caché de
Waylith.** No inventa hashes, no escribe `Assets.zip` y no modifica el mod.

```bash
python3 tools/rimworld17/slang/probe_shaders.py /ruta/IsoRim-1.7 \
  --slangc "$SLANG_WORK_DIR/host/Release/bin/slangc" \
  --spirv-val "$SLANG_WORK_DIR/host/external/spirv-tools/tools/spirv-val" \
  --output /ruta/resultados-isorim
```

Para cerrar la vía de caché se necesitan, de **la misma instalación** del juego:

1. `Waylith.dll`: `RuntimeShaderCompiler`, claves, variantes, lectura/escritura,
   reflexión y llamadas nativas Slang. Si usa otra DLL de bindings, identificarla
   a partir de `RimWorld.deps.json` y facilitar esa DLL también.
2. `Assembly-CSharp.dll` y `UnityStubs.dll`: resolución de `ShaderDef`, valores
   por defecto y traducción del material a las etapas del motor.
3. `Assets.zip`: índice, shaders internos (incluido Magenta), includes y pack
   precompilado. Se analiza localmente; no se incorpora al repositorio ni al APK.
4. `RimWorld.deps.json`, `RimWorld.runtimeconfig.json` y número exacto de build:
   identificación de versiones. El `libslang.so` original permite comparar tag y
   exportaciones con los del port; no se redistribuye.
5. `dotnet.log`, log del juego y logcat del cierre, junto con modelo/GPU/driver,
   versión exacta de IsoRim y procedencia del APK que falla.

Un pack generado por la misma versión del motor en PC podría ser preferible a
compilar en el teléfono **si** se comprueba el formato, portabilidad de sus claves,
variantes y capacidad SPIR-V. No hay aún evidencia para descartarlo ni para
integrar un pack inventado.

## Compilación reproducible de la alternativa

Slang es código abierto. La versión oficial `v2026.19`, commit
`0fb6b7587a94fa33d88141ec9930766eedc6f59a`, documenta Android y exporta la API real.
Se usa esa versión porque coincide con el tag del stub, sin forzar ni falsear su
respuesta. Sus submódulos conservan los gitlinks oficiales. No se parchea Slang.

Prerrequisitos: entorno Android preparado (JDK completo 21, SDK 36, NDK
27.0.12077973, CMake 3.22.1), C/C++ de host, Python 3, Git y espacio para ambos
builds. `BUILD_JOBS` limita el paralelismo; por defecto son 4 procesos.

```bash
source /workspace/.setup/java-proxy.sh  # en el entorno preparado
export SLANG_WORK_DIR=/workspace/slang-build
export SLANG_SOURCE_DIR=/workspace/research/slang  # opcional: checkout ya descargado
bash tools/rimworld17/slang/build.sh
bash gradlew --max-workers=4 -PdotnetEngine=true -PruntimeSlang=true \
  :app:testDebugUnitTest :app:assembleDebug
```

El script primero construye herramientas host y ejecuta `api_smoke.cpp`: tag,
`slang_createGlobalSession2`, sesión GLSL, reflexión, SPIR-V y rechazo de fuente
inválida. Después compila Android API 30/ARM64 con C++ compartido y alineación de
16 KB. Empaqueta `libslang.so`, `libslang-glslang-2026.19.so` y
`libslang-glsl-module-2026.19.so` en `app/build/runtime-slang`, junto con licencias
y procedencia. El CMake envolvente ajusta el nombre/SONAME del compilador al nombre
que importa Waylith; su implementación y API no cambian. Los módulos que Slang
carga dinámicamente conservan los nombres versionados esperados por upstream.

La selección es explícita; `runtimeSlang=true` exige `dotnetEngine=true` y los tres
binarios preparados. Sin la opción no se empaquetan los módulos ni se sustituye
el stub. El APK debug experimental tiene sufijo `-isorim-slang-experimental`.

La compilación box64 existente regenera archivos versionados. Conserva el estado
previo y restaura solo esos cambios generados tras compilar; no descartes trabajo
del usuario. Los artefactos Slang se crean fuera de los archivos versionados.

## Descargar el APK desde GitHub Actions

En GitHub abre **Actions → Build RimDroid APK → Run workflow** y selecciona
`fix/isorim-waylith-android` en **Use workflow from**. Esa ejecución manual usa
JDK 21, SDK 36, NDK 27.0.12077973 y CMake 3.22.1; exige al menos 20 GiB libres.
Compila el APK .NET habitual, ejecuta el smoke test host de Slang, compila con
`-PdotnetEngine=true -PruntimeSlang=true`, ejecuta los tests unitarios y las cuatro
pruebas de regresión de empaquetado, verifica el APK y comprueba su firma.

Al terminar correctamente, abre la ejecución y descarga en **Artifacts**
`rimdroid-isorim-slang-experimental-<run_id>`. Extrae el ZIP en Windows: contiene
`rimdroid-isorim-slang-experimental.apk`, `SHA256SUMS`, el informe ELF,
la procedencia Slang y el commit de origen. El artefacto caduca a los 14 días;
puedes volver a ejecutar el workflow. Los informes de tests se publican también
si la compilación falla. No necesita archivos del juego ni el APK anterior de
`/workspace/artifacts`.

Estas comprobaciones de CI no ejecutan Android ni acreditan que IsoRim/Waylith
funcione en un dispositivo. Sigue la validación de la sección siguiente.

## Verificación y prueba en Android

```bash
python3 tools/rimworld17/slang/verify_apk.py /ruta/experimental.apk \
  --runtime-slang --readelf "$ANDROID_HOME/ndk/27.0.12077973/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-readelf" \
  --report /ruta/experimental-elf.json
```

El verificador comprueba ZIP, ARM64, .NET/hooks, dependencias ELF, símbolos Slang
y audio, nombres, alineación y SHA-256 del manifiesto de las tres bibliotecas
nuevas. El script elimina los símbolos no necesarios antes de calcular los hashes
para que coincidan con los bytes que Gradle empaqueta. Reconoce SONAME de
bibliotecas existentes precargadas, como Mono. No sustituye la carga en Android.
Sin `--runtime-slang` comprueba que el APK .NET habitual conserve el stub.

Regresión de empaquetado (ambos builds reales, cuatro pruebas sin mocks):

```bash
python3 tools/rimworld17/slang/test_apks.py /ruta/baseline.apk /ruta/experimental.apk \
  "$ANDROID_HOME/ndk/27.0.12077973/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-readelf"
```

Antes del juego puede probarse la API Android sin archivos propietarios:

```bash
adb shell mkdir -p /data/local/tmp/rimdroid-slang-check
adb push "$SLANG_WORK_DIR/android/Release/bin/rimdroid-slang-smoke" /data/local/tmp/rimdroid-slang-check/
adb push app/build/runtime-slang/jniLibs/arm64-v8a/. /data/local/tmp/rimdroid-slang-check/
adb push "$ANDROID_HOME/ndk/27.0.12077973/toolchains/llvm/prebuilt/linux-x86_64/sysroot/usr/lib/aarch64-linux-android/libc++_shared.so" /data/local/tmp/rimdroid-slang-check/
adb shell 'cd /data/local/tmp/rimdroid-slang-check && chmod 755 rimdroid-slang-smoke && LD_LIBRARY_PATH=. ./rimdroid-slang-smoke'
```

Para probar el APK:

1. Exporta partidas/configuración y usa una copia de la instancia. Este APK debug
   conserva `com.rimdroid`; si la firma difiere, `adb install -r` fallará. No
   desinstales la aplicación original sin haber exportado sus datos.
2. Instala con `adb install -r /ruta/experimental.apk` cuando la firma lo permita.
3. Inicia RimWorld 1.7 sin IsoRim, carga una colonia y comprueba renderizado/audio.
4. Activa solo Harmony compatible e IsoRim para aislar la prueba. Carga la misma
   colonia; activa/desactiva 3D (tecla 0 o cubo), gira, prueba interiores,
   vegetación, techos, transparencias, agua y efectos. Reinicia y repite para
   comprobar el uso posterior de caché.
5. Conserva logs completos de ambas ejecuciones, build del juego, GPU/driver y
   SHA-256 del APK. No basta con que desaparezca una excepción: comprobar ausencia
   de magenta, shaders faltantes, device-lost y errores de audio.

## Audio, investigado por separado

`wa_sound_resident_bytes` ya existe desde `f513dba` (0.3.0v2 / build de juego 4981).
Calcula bytes PCM o del stream en memoria bajo el mismo bloqueo de sonidos y
devuelve cero para handles inexistentes o sonidos sin buffer residente. La
prueba del APK exige que el símbolo esté exportado. Una ausencia en el dispositivo
puede señalar un APK anterior o una biblioteca diferente; hace falta identificar
el binario cargado. Este cambio no altera ni declara reparado el audio del usuario.
