# Autonomous Colonists — RimWorld 1.7 — 0.4 experimental

Mod independiente con un director de propuestas y el gestor de prioridades de v0.3. El jugador decide los objetivos de la colonia; el gestor reparte cobertura y el director observa necesidades y señales de peligro. No cambia RimDroid, el APK, Waylith ni la IA de trabajos del juego.

**Compila con .NET SDK 10 contra las tres DLL privadas proporcionadas. La ejecución dentro de RimWorld y Android sigue pendiente.** El ZIP generado contiene solo el mod y su documentación/licencia; no contiene bibliotecas del juego.

## Autonomous Director v0.4

- Activación voluntaria por partida, independiente del gestor de trabajos. **Equilibrado** predeterminado, **observación** predeterminada. Una partida v0.3 conserva su gestor y recibe el director apagado.
- Conservador / Equilibrado / Avanzado cambian umbrales, estabilidad y frecuencia; mantienen las mismas restricciones manuales. Intervalos de perfil: 1250 / 750 / 250 ticks, configurables de 250 a 2500.
- Observación real de hambre, descanso, salud resumida, trabajo actual, orden forzada, reclutamiento, peligro/incendio del mapa y colono ardiendo. Niveles normal, creciente, importante y crítico.
- Resolución determinista de conflictos por actividad del colono, histéresis y conservación de la propuesta vigente ante mejoras marginales. Sin aprendizaje, servicios externos, Harmony ni dependencias nuevas.
- **Solo propuestas** de alimentación, descanso y seguridad; cero ejecución de estas acciones incluso con observación desmarcada. No se presupone disponibilidad de comida, cama o refugio. Salud y ropa quedan reservadas para fases posteriores.
- En observación activa, el director suspende **todas** las escrituras automáticas de prioridades, incluidas restauraciones y botones. Retiene el historial para reanudar/restaurar después. Sus diagnósticos y configuración sí se actualizan.
- Órdenes forzadas y reclutamiento también bloquean asignaciones y restauraciones del gestor; las restauraciones se aplazan sin perder propiedad. Exclusiones compartidas entre ambos sistemas.
- Estado mínimo por ID y mapa guardado con Scribe; observaciones, diario limitado a 64 entradas y contadores de sesión se reconstruyen. Se conserva intacta la identidad histórica `AutonomousColonists.AutonomyDirector` para v0.2.

Lee [ARQUITECTURA_DIRECTOR.md](ARQUITECTURA_DIRECTOR.md), [PRUEBAS.md](PRUEBAS.md) y [RENDIMIENTO.md](RENDIMIENTO.md) para políticas, evidencia y límites. **La compilación pasa; guardar/cargar una partida real y Android siguen pendientes.**

## Gestor de prioridades conservado desde v0.3

- Desactivado por defecto **en cada partida**; requiere activar prioridades numéricas en Trabajo y el gestor desde las opciones del mod.
- Puntúa habilidades (1,5 por nivel) y pasiones (+4 menor, +10 mayor), promediando las habilidades relevantes. Descuenta carga de especialidades y conserva titulares con un margen de estabilidad configurable (6 puntos por defecto).
- Un titular a prioridad 1, cobertura adicional a 2, un suplente a 3 y resto a 4. Limpieza/transporte: cobertura a 3 y apoyo a 4; tareas urgentes habilitadas a 1 y BasicWorker a 2. Trabajos desconocidos sin habilidades: respaldo a 4.
- Cobertura predeterminada según población del mapa, limitada a la capacidad disponible. Puede configurarse por trabajo: -1 automática, 0 no gestionar, 1–10 cobertura deseada. Los especialistas manuales cuentan para esa cobertura y para la carga.
- **Nunca habilita un 0**, ni asigna trabajos que el colono no puede realizar. Los cambios observables del jugador, incluidos habilitar un trabajo previamente a 0, bloquean esa celda hasta liberación explícita.
- Observa cada 250 ticks y antes de cualquier recálculo. Recalcula cada 1–24 horas de juego (3 por defecto), o al cambiar población, exclusiones, disponibilidad o bloqueos. Los otros ticks no recorren la colonia.
- Colonos reclutados, incapacitados, en estado mental o con ánimo/descanso muy bajos no reciben reasignaciones. La cobertura se reconsidera al perderse/recuperarse capacidad; umbrales separados evitan oscilación por estrés.
- Exclusiones y bloqueos siguen al colono entre mapas. Un solo registro por partida evita que dos mapas compitan por las mismas prioridades. Al salir de los mapas se intenta restaurar la tabla aún controlada.
- Al desactivar, excluir, pausar un trabajo o detectar `LordKuper.WorkManager`, solo restaura celdas cuyo valor todavía coincide con el último escrito por el mod. Los bloqueos manuales se conservan.
- Con prioridades numéricas apagadas, pausa la observación y restauración: `GetPriority` oculta las prioridades positivas como 3. Reactiva los números para restaurar antes de desinstalar.
- Un error pausa el gestor, intenta restaurar y evita ciclos automáticos de reintentos. La interfaz muestra estado, tiempo del último cálculo y número de escrituras.

Es una implementación original inspirada en funciones de Work Manager. No incorpora su código ni sus recursos y no depende de Harmony, Work Manager o LordKuper.Common. Alimentación, ropa, descanso, horarios, tratamientos y `selfTend` mantienen el comportamiento del juego/jugador.

## Instalación

Lee [INSTALAR.md](INSTALAR.md). Extrae el ZIP de `dist/` y copia `AutonomousColonists/` a la carpeta Mods del juego. No se instala como APK. Utiliza primero una copia de tu partida y consulta [PRUEBAS.md](PRUEBAS.md).

## Compilación y pruebas

Requiere Python 3 y SDK .NET **10**. No necesitas el SDK Android ni reconstruir RimDroid.

```sh
# Pruebas sin archivos del juego:
dotnet run --project Tests/AutonomousColonists.Tests.csproj -c Release -- --report TestResults/core-tests.json
dotnet run --project Tests/Director/AutonomousColonists.Director.Tests.csproj -c Release -- --report TestResults/director-tests.json
python3 -m unittest discover -s tools -p 'test_*.py' -v

# Build real, pruebas, inspección de metadatos y ZIP instalable:
python3 tools/build_from_zip.py /ruta/privada/mods.zip

# Alternativa: referencias privadas ya instaladas, sin copiarlas:
python3 tools/build_from_zip.py --managed-dir /ruta/privada/RimWorld
```

Las referencias exactas son `Assembly-CSharp.dll`, `UnityStubs.dll` y `Waylith.dll` de la misma instalación 1.7. El ZIP privado se extrae a un directorio temporal que se elimina al terminar. `Private=false` impide que MSBuild copie las referencias al resultado. Los artefactos se generan mediante una lista cerrada y sus metadatos se inspeccionan sin ejecutar código del juego.

Salidas: `dist/AutonomousColonists-RimWorld-1.7.zip`, `SHA256SUMS`, `build-report.json`, `api-audit.json`, `core-tests.json`, `director-tests.json`, `packaging-tests.txt`. El informe registra commit, estado del árbol y hashes de las fuentes usadas. No publiques `mods.zip`, referencias privadas ni carpetas de compilación completas.

## GitHub Actions

El workflow existente **Autonomous Colonists mod** se conserva sin cambios. El nuevo **Autonomous Director core** ejecuta las pruebas adicionales y publica su informe sin referencias privadas. No se modifican ni invocan workflows del APK. No se configura runner privado en esta fase.

La generación automática del ZIP necesita referencias privadas. No caben razonablemente en secretos de GitHub, y este proyecto no las publica ni las descarga de terceros:

1. Prepara un runner privado de Linux x64, dedicado a código de confianza, con etiquetas `self-hosted`, `linux`, `x64`, `rimworld17-private`.
2. Instala las tres DLL fuera del checkout y de sus artefactos. Define `RIMWORLD_MANAGED_DIR` en el entorno del **servicio del runner**, apuntando a ese directorio privado.
3. En variables del repositorio, define `AUTONOMOUS_PRIVATE_BUILD=true`. Hasta entonces, el job `package` queda omitido; las pruebas públicas sí pueden ejecutarse.
4. Un push a `feat/autonomous-colonists-1.7` que afecte al mod/workflow, o una ejecución manual seleccionando esa rama, prueba, compila e incorpora únicamente ZIP, suma SHA-256 e informes permitidos al artefacto `AutonomousColonists-RimWorld-1.7`.
5. Descarga ese artefacto desde la ejecución de Actions y extrae el ZIP del mod. Las pull requests ejecutan solo pruebas en un runner público; nunca código de una PR en el runner con archivos privados.

La [primera ejecución remota](https://github.com/Borjabyxx/RimDroid/actions/runs/39239475222) del commit `6a1d2a4` terminó correctamente: job `tests` superado e informe publicado; job `package` omitido por su condición de activación privada. La compilación real del mod se ha ejecutado localmente; no se ha configurado desde aquí el runner privado ni se ha observado un ZIP generado remotamente. El YAML no puede crear las referencias privadas en los runners públicos.

## Límites conocidos

La cobertura es un objetivo configurable, no un cálculo de encargos, existencias, estaciones o stock de comida. No crea trabajo que el jugador no haya solicitado. Un cambio al mismo valor automático, o varios cambios que regresan a ese valor entre observaciones, no es detectable con sondeo; para control completo, excluye al colono. Los gestores no reconocidos pueden interferir: exclúyelos/desactívalos para probar. No hay integración especial con Work Tab u otros gestores.

El componente de mapa v0.2 se conserva para importar sus valores/bloqueos/exclusiones a la nueva estructura, sin escrituras durante la deserialización. La migración y `Verse.Scribe` requieren una prueba real de guardar/cargar; las pruebas puras solo comprueban la representación de valores del registro. El mod no puede deducir el valor previo de `selfTend` si alguna compilación antigua lo modificó.

Código original bajo MIT: [LICENCIA.txt](LICENCIA.txt). Consulta [AUDITORIA.md](AUDITORIA.md) para los defectos encontrados y el alcance real de la validación.

## Ensayo adicional de Scribe

```sh
python3 tools/scribe_smoke.py /ruta/privada/RimWorld --report dist/scribe-smoke.json
```

Compila y ejecuta un programa aislado contra las bibliotecas reales, con IDs sintéticos y sin colonos/partida. Sus copias de las referencias viven en un directorio temporal que se elimina al terminar. No se ejecuta en CI público ni es requisito artificial del empaquetado.

En este entorno se escribió el XML, pero el ciclo de lectura quedó **bloqueado**: faltan las dependencias originales `com.rlabrecque.steamworks.net.dll` (identidad 0.0.0.0, al inspeccionar tipos) y `Brutal.Core.Logging.dll` (2026.9.0.0, al registrar el error). No se crearon sustitutos. Estas bibliotecas no hacen falta para compilar el mod; sí para continuar este ensayo de runtime fuera del juego. Puede que, después de resolverlas, se requieran otras dependencias del runtime completo; no se da esa lista por cerrada. Los resultados parciales no prueban guardar/cargar una partida real.
