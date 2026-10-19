# Autonomous Colonists 0.5 experimental — RimWorld 1.7 / .NET 10

Mod independiente con Autonomous Director, perfiles Conservador/Equilibrado/Avanzado y acciones automáticas reales mediante los JobGiver, WorkGiver y JobTracker del juego. Equilibrado sigue predeterminado, con activación voluntaria por partida. Al actualizar desde v0.3/v0.4 no se habilitan acciones nuevas automáticamente.

Implementación actual: comer comida permitida, dormir en cama, atención médica/autocuidado autorizado, descanso médico, mejoras de ropa del optimizador nativo, rescate seguro, huida y apagar fuego propio. El gestor de trabajo de v0.3 conserva habilidades/pasiones, especialistas, suplentes, cobertura y bloqueos. Defensa armada, selección de armas y refugios estratégicos quedan pendientes; no hay opciones ficticias para habilitarlos.

**Compilación verificada contra DLL reales; pruebas puras superadas. Los jobs nativos, partidas y Android todavía necesitan validación real.** Las bibliotecas privadas, sus fuentes de inspección y los archivos del juego no se distribuyen.

- [Funciones, arquitectura, permisos y límites exactos](AUTONOMIA_0.5.md)
- [Pruebas ejecutadas y pendientes](PRUEBAS_0.5.md)
- [Rendimiento y alcance de los benchmarks](RENDIMIENTO_0.5.md)
- [Instalación en RimDroid](INSTALAR.md)

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

## Compilar y probar

Python 3 y SDK .NET 10; no requiere Gradle, SDK Android ni recompilar RimDroid. Desde este directorio:

```sh
dotnet run --project Tests/AutonomousColonists.Tests.csproj -c Release -- --report TestResults/core-tests.json
dotnet run --project Tests/Director/AutonomousColonists.Director.Tests.csproj -c Release -- --report TestResults/director-tests.json
dotnet run --project Tests/Actions/AutonomousColonists.Actions.Tests.csproj -c Release -- --report TestResults/action-tests.json
python3 -m unittest discover -s tools -p 'test_*.py' -v
python3 tools/build_from_zip.py --managed-dir /ruta/privada/RimWorld
# O, con el ZIP privado proporcionado legalmente:
python3 tools/build_from_zip.py /ruta/privada/mods.zip
```

Referencias: Assembly-CSharp.dll, UnityStubs.dll y Waylith.dll de la misma instalación 1.7. Private=false; nunca se copian al resultado. Los tests usan solo el núcleo original del mod, sin stubs de Verse ni dependencias nuevas.

El build ejecuta las tres suites C#, Python, compila con advertencias como errores, inspecciona PE/.NET y referencias a métodos reales, y genera el ZIP por lista cerrada, SHA256SUMS e informes con commit/árbol/hashes de fuentes. El ZIP contiene la DLL propia, About, licencia, instalación, documentación v0.5 y cinco informes de verificación. Los benchmarks sintéticos usan dobles de prueba en la frontera; no simulan que se haya ejecutado el juego.

Ensayo adicional de Scribe real:

```sh
python3 tools/scribe_smoke.py /ruta/privada/RimWorld --report dist/scribe-smoke.json
```

La escritura XML se comprueba; la lectura está bloqueada aquí por Steamworks/Brutal.Core.Logging ausentes. No cuenta como prueba superada de guardar/cargar.

## Actions y distribución

Se conservan sin cambios los workflows existentes. El nuevo autonomous-actions.yml prueba la frontera de ejecución y publica solo su JSON. Los tests públicos no necesitan DLL privadas. El workflow privado de compilación ya existente sigue desactivado por su condición; no se configura un runner privado en esta fase.

La distribución experimental se publica con una etiqueta nueva y el ZIP ya compilado/verificado en el entorno, sin recompilarlo en un runner público ni subir referencias propietarias. Los workflows de transferencia fijan hashes/commit y verifican los bytes descargados antes de publicar. No sobrescribir v0.3 ni v0.4.

Los informes históricos AUDITORIA.md, ARQUITECTURA_DIRECTOR.md, PRUEBAS.md y RENDIMIENTO.md describen v0.3/v0.4; consultar los documentos v0.5 enlazados arriba para esta versión. Código MIT, LICENCIA.txt.
