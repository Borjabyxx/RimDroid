# Auditoría de Autonomous Colonists v0.2 → 0.3 experimental

Fecha: 19 de octubre de 2026. Estado: compilación y pruebas locales verificadas; ejecución real en RimWorld/Android pendiente.

## Alcance y procedencia

El trabajo parte de `fix/isorim-waylith-android`, commit `98199cb15f7b7e4cfed246839515ca5980d3a97a`, en la rama exclusiva `feat/autonomous-colonists-1.7`. El directorio `mods/AutonomousColonists/` es autocontenido: puede separarse del repositorio y compilarse con SDK .NET 10. Se mantiene aquí para facilitar la revisión solicitada en el fork; no hay dependencia de código de RimDroid.

Solo se incorpora ese directorio y un workflow nuevo, `.github/workflows/autonomous-colonists.yml`. No se modifica el APK, Gradle, código Android/nativo/Waylith, master, shaders ni workflows existentes. No se ejecuta la compilación Android ni se añaden funciones de necesidades, ropa o medicina.

La única referencia a Work Manager es funcional y su ID de paquete para evitar competencia. No se ha copiado ni incorporado su código, binarios o recursos.

## Compilación real y APIs

Se instaló .NET SDK **10.0.402** en el entorno existente. El primer intento del instalador de canal recurrió a `ci.dot.net`, bloqueado por la política de red. Se utilizó la versión explícita oficial servida desde `builds.dotnet.microsoft.com`. Las rutas XDG/CLI de .NET se dirigieron a `/workspace` porque el HOME del sistema es de solo lectura; no se sustituyeron credenciales ni se modificó la política de red.

La fuente v0.2 original **sí compila** contra las DLL proporcionadas, con una advertencia CS8600 en `ModEntry.cs` al tratar un mapa anulable. No se encontró una incompatibilidad de firma en aquella compilación. La revisión actual compila con `TreatWarningsAsErrors=true`: **0 errores, 0 advertencias**.

Las tres referencias se inspeccionaron mediante metadatos PE; todas declaran `.NETCoreApp,Version=v10.0`:

| Referencia privada | Identidad de ensamblado | SHA-256 |
| --- | --- | --- |
| Assembly-CSharp.dll | Assembly-CSharp 1.7.9786.4087 | eb0826b85af430d13962e5c097ef53d869cf6a031656fb6a3673168a761c9f23 |
| UnityStubs.dll | UnityStubs 1.0.0.0 | b9a47e34d975a6b41fdf2af7a2effd8080a543d34d0153ee5d8614efbb019270 |
| Waylith.dll | Waylith 1.0.0.0 | 4b14bc007509ca1ed31924939ec6106ef05b32e9726905cd6e63fba91952df36 |

No se cargó ni ejecutó código del juego para esta inspección. La compilación valida las firmas utilizadas de GameComponent, MapComponent, Pawn_WorkSettings, WorkTypeDef, Scribe y la interfaz Unity/Verse contra esos archivos reales. Waylith se proporciona como referencia, aunque el mod final no necesita una dependencia directa: la interfaz utilizada se expone por UnityStubs/Verse.

El resultado es `AutonomousColonists` 0.3.0.0, ILOnly, sin requisito de 32 bits ni bibliotecas nativas propias. Sus referencias son System.Runtime, System.Collections y System.Linq 10.0, Assembly-CSharp 1.7.9786.4087 y UnityStubs 1.0. No hay Harmony, Work Manager ni LordKuper.Common. Esto comprueba la compilación y la estructura, **no demuestra carga ni comportamiento en Android**.

## Defectos y cambios justificados

| Hallazgo en v0.2 | Solución actual |
| --- | --- |
| Automatización y AutoSelfTend activados por defecto; la escritura médica persistía al desactivarse e incluso podía ejecutarse con el gestor suspendido. | Activación voluntaria por partida y eliminación completa de escrituras médicas. No es posible reconstruir el valor previo de selfTend si otra compilación ya lo cambió. |
| El botón de recálculo comprobaba ediciones al escribir si KeepManualEdits estaba activo, pero ordenaba candidatos y consumía plazas antes de saber que la celda estaba bloqueada. | Observación previa a todo cálculo; exclusión de celdas bloqueadas del conjunto escribible. Su prioridad manual solo aporta cobertura si realmente es de especialista. |
| Se podía desactivar la protección manual; no se observaban trabajos que nunca habían sido gestionados. | Protección siempre activa, con registro también de valores observados sin propiedad. Un 0 habilitado manualmente después de observarlo se protege. |
| Excluir/reincluir o desactivar eliminaba manualLocks al limpiar el historial. | Se separan propiedad y bloqueos. Restaurar/liberar propiedad no borra decisiones manuales. Liberación de bloqueos solo mediante una acción explícita del usuario. |
| GetPriority devuelve 3 para todos los trabajos positivos si useWorkPriorities está apagado. Esto se comprobó en el cuerpo IL de la DLL privada. Notify_UseWorkPrioritiesChanged no reescribe esos valores internos. | Pausa sin observar, restaurar ni descartar el registro hasta que vuelvan los números. No cambia la opción del juego ni usa reflexión para acceder a datos privados. |
| Sin histéresis; pasión podía producir apoyos a prioridad 2 fuera del presupuesto de especialistas. | Margen de incumbencia configurable; roles y suplente explícitos, con apoyos a 3/4. Casos de estabilidad comprobados. |
| SpecialistCount recibía el número elegible para ese trabajo, no la población; la capacidad manual no se descontaba de la cobertura. | Población del mapa como base, cobertura deseada configurable y límite por candidatos disponibles. Los roles manuales cuentan para cobertura y carga. |
| Un registro por mapa podía competir por un colono trasladado; las salidas y los retornos no se trataban explícitamente. | Un GameComponent por partida con referencias guardadas a colonos, asignación por mapa y restauración condicional al salir. Bloqueos/exclusiones siguen al colono. |
| No había criterios explícitos de estrés y recuperación ni una política de fallo que suspendiera el recálculo. | Pausa de reasignaciones por reclutamiento/incapacidad/estado mental/ánimo o descanso bajos; umbrales de entrada y salida distintos. Un error pausa e intenta restaurar, con reactivación explícita. |
| Listas de bloqueos con búsqueda lineal y puntuación recalculada dentro de comparadores. | HashSet para bloqueos y recuperación, diccionarios de celdas y puntuación por candidato calculada antes de ordenar. |
| Estado guardado antiguo utilizaba el tipo AutonomousColonists.AutonomyDirector como MapComponent. Cambiar su herencia rompería esa identidad serializada. | Se conserva ese tipo como adaptador de lectura v0.2 y se incorpora un GameComponent nuevo. La importación no escribe prioridades durante Scribe. Validación de migración real pendiente. |
| Altura del contenido estimada; mapa actual posiblemente nulo; Begin/End podían quedar desequilibrados ante excepciones. | Nulabilidad explícita, desplazamiento con CurHeight real y cierre de la interfaz en finally. Visualización Android pendiente. |
| ZIP original omitía la licencia MIT del código distribuido. | Distribución cerrada con DLL propia, About, instrucciones y licencia. Verificación del ZIP, rechazo de bibliotecas del juego y de symlinks. |

La «demanda» actual es la cobertura solicitada por el jugador o una heurística conservadora según población. **No se afirma medir demanda real** de cosechas, facturas de producción, comida, materiales o investigación. Esta fase no inventa trabajo que no haya solicitado el jugador.

## Rendimiento comprobado y sus límites

En 249 de cada 250 ticks, el componente solo comprueba el contador. Cada 250 ticks observa celdas y disponibilidad; la asignación completa solo ocurre al vencerse el intervalo, pulsar el botón o cambiar población/disponibilidad/bloqueos/configuración.

La asignación pura es aproximadamente O(W × P log P), con memoria O(W × P). La observación es O(W × P), más colecciones de mapas/colonos y búsquedas de exclusiones. No se recorren objetos, reservas, animales ni encargos de todo el mapa. Las colecciones de definiciones ordenadas se almacenan en caché. Las colecciones temporales grandes se crean solo en observación/recálculo, no en cada tick.

Muestra local con 20 tipos de trabajo, 50 calentamientos y 500 mediciones por tamaño, **Linux x64, solo asignador puro**:

| Colonos | Mediana (ms) | p95 (ms) | Bytes asignados por llamada |
| --- | ---: | ---: | ---: |
| 1 | 0.0090 | 0.0119 | 8112 |
| 5 | 0.0237 | 0.0543 | 17512 |
| 20 | 0.0593 | 0.1387 | 34312 |
| 50 | 0.1210 | 0.1785 | 75400 |

Estos resultados no incluyen Pawn/Verse, observación, Scribe, UI ni renderizado. No son tiempos de Android. Cada build vuelve a medir y entrega su propia muestra en core-tests.json; pueden variar. La interfaz del mod incorpora Stopwatch y contador de escrituras para medir el adaptador real cuando se ejecute.

## Pruebas y distribución

- **40/40 pruebas C#**: elegibilidad, capacidades, prioridades cero, trabajos pausados, cobertura según población y objetivo, roles manuales, suplentes, reparto de carga, histéresis, trabajos desconocidos, bloqueos/restauración y representación de estado. Incluye 200 colonias aleatorias con invariantes/permutaciones y 50 escenarios × 5 refrescos de estabilidad.
- **10/10 pruebas Python**: referencias faltantes/duplicadas, extracción sin traversal, cabeceras/tamaño, lista cerrada de distribución, ZIP determinista, exclusión de archivos extra/propietarios, archivos faltantes y symlinks.
- Build Release real contra las DLL privadas, con advertencias como errores. Auditoría estática PE de referencias y resultado.
- ZIP con exactamente cuatro archivos: About/About.xml, 1.7/Assemblies/AutonomousColonists.dll, INSTALAR.md y LICENCIA.txt, bajo una única carpeta AutonomousColonists.
- Revisión del índice Git y de rutas afectadas para excluir DLL del juego, ZIP privado y resultados de compilación. Sin modificación de archivos existentes de RimDroid.

El script genera SHA256SUMS, informe del build con commit/estado/hashes de fuentes, auditoría de APIs/metadatos, resultados y métricas. Las salidas no se guardan en Git. El hash del ZIP entregado se obtiene de su SHA256SUMS, evitando mezclar binarios de distintas compilaciones.

## Actions y bloqueos demostrados

El workflow nuevo ejecuta pruebas puras sin las DLL. Su job de compilación privada está condicionado a `AUTONOMOUS_PRIVATE_BUILD=true`, runner `rimworld17-private`, rama de confianza y ausencia de evento pull_request. Subir las referencias a secretos/artefactos públicos o crear APIs ficticias no forma parte de la solución.

Se verificó la [ejecución remota 39239475222](https://github.com/Borjabyxx/RimDroid/actions/runs/39239475222) del commit `6a1d2a401fc7aa4201400cc2e6e3ad18c494f0b5`: resultado Success, job `tests` completado correctamente (22 s), artefacto `autonomous-colonists-tests` publicado y job `package` omitido. No se ha configurado un runner privado desde este entorno ni se ha observado un ZIP remoto. Para ese job siguen siendo necesarios el runner, RIMWORLD_MANAGED_DIR y la variable de activación. El ZIP entregado procede del build local real.

Se intentó además una prueba real aislada de `Verse.Scribe` con un componente, IDs sintéticos y sin referencias a colonos. La escritura del XML terminó; la lectura quedó bloqueada. La primera excepción de carga de tipos identifica `com.rlabrecque.steamworks.net, Version=0.0.0.0`; al registrar ese error también falta `Brutal.Core.Logging, Version=2026.9.0.0`. Las DLL originales correspondientes (`com.rlabrecque.steamworks.net.dll` y `Brutal.Core.Logging.dll`) son necesarias para continuar este ensayo, con sus dependencias si aparecen después. No son necesarias para el build del mod y no se proporcionan versiones alternativas ni stubs. El reproductor `tools/scribe_smoke.py` guarda un JSON con el resultado parcial y las identidades ausentes; sus copias privadas se eliminan al salir. Este ensayo **no pasa** y no valida el ciclo de guardar/cargar.

No hay dispositivo Android ni runtime completo del juego ejecutándose aquí. Faltan las pruebas de carga, UI, Scribe, migración v0.2, múltiples mapas y partidas reales de PRUEBAS.md. El sondeo no puede detectar una edición al mismo valor del mod o cambios que se reviertan entre observaciones. No puede garantizar compatibilidad con gestores de prioridades desconocidos; Work Manager se detecta por su ID conocido.
