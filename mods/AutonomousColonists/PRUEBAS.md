# Validación — 0.4 experimental

## Resultado v0.4 (19/10/2026)

- **40/40 C# originales** de v0.3, con `Tests/Program.cs` intacto.
- **50/50 C# adicionales** del director, compilados con advertencias como errores. Incluyen perfiles/default voluntario, necesidades/umbrales, orden manual y reclutamiento, conflictos, críticas, histéresis, mejoras marginales, exclusiones, traslados/salidas, cambios de perfil, ticks atrasados, estado de valores, errores/recuperación, director apagado, permisos de escritura y observación sin ejecución.
- 400 escenarios aleatorios reproducibles (0–50 colonos, tres perfiles) verifican determinismo por permutación, veto manual, exclusión, disponibilidad, un ganador por recurso y cero ejecución. Escenario de 200 evaluaciones con ruido verifica estabilidad; diario acotado y ruta de tick no vencido sin asignaciones.
- **10/10 Python originales** sin cambios: entrada privada y distribución.
- Build real Release contra `Assembly-CSharp` 1.7.9786.4087, `UnityStubs` y `Waylith` proporcionados: **0 errores y 0 advertencias**, ILOnly, .NET 10. Sin dependencias nuevas.
- Benchmark puro del director 5/20/50 y benchmark original de trabajos conservado; [RENDIMIENTO.md](RENDIMIENTO.md). JSON separados con alcances explícitos.
- XML del gestor y del director con memoria sintética escrito mediante Scribe real. **Lectura bloqueada**, no superada: faltan `com.rlabrecque.steamworks.net.dll` 0.0.0.0 y `Brutal.Core.Logging.dll` 2026.9.0.0. Podrían faltar otras dependencias después. No se usan stubs; `scribe-smoke.json` informa `passed=false`.
- ZIP cerrado con cuatro archivos; solo DLL propia. SHA-256 e identidad de DLL en informes de build. Ninguna referencia privada distribuida.

Las pruebas de estado son del **modelo de valores JSON**, no una sustitución de Verse.Scribe. Las pruebas de permisos usan el núcleo compartido con las rutas de escritura; no ejecutan `Pawn_WorkSettings` ni una partida. La compilación comprueba firmas del adaptador real, pero no demuestra su conducta en el juego.

### Matriz de integración pendiente

| Escenario real | Estado |
| --- | --- |
| Cargar v0.3 con trabajos activos; director nuevo apagado y Equilibrado | Pendiente en juego |
| Importar guardado v0.2 conservando identidad histórica/bloqueos | Pendiente en juego |
| Guardar/cargar director, perfil, exclusiones, histéresis y suspensión | Bloqueado en ensayo aislado; pendiente en juego |
| Observación con gestor previamente activo: cero escrituras/restauraciones | Permiso probado en núcleo; pendiente con Verse |
| Orden forzada/reclutado con restauración pendiente y posterior liberación | Ledger/permiso probado; pendiente con Verse |
| Traslado de mapa, caravana, exclusión/reinclusión y carga de mapas | Modelo probado; pendiente con Verse |
| UI táctil, scroll, cambio de perfil, necesidades reales y alertas | Compila; pendiente en juego/Android |
| Fallo real, suspensión y desactivar/reactivar | Núcleo probado; pendiente con Verse |

Para empezar en Android, seguir INSTALAR.md y añadir a la lista inferior: activar solo director en observación, comprobar tabla intacta durante varios días, registrar hambre/descanso reales y motivos; probar los tres perfiles; cambiar mapa/excluir/guardar/cargar. Después probar el gestor por separado. Las propuestas no deben cancelar jobs ni cambiar horarios/políticas/zonas. El contador ejecutadas del nuevo director siempre debe ser cero.

El nuevo workflow `autonomous-director.yml` ejecuta la suite adicional y publica solo su JSON. Los workflows existentes no se modifican. Las ejecuciones remotas v0.4 se identifican en la entrega final; no se atribuyen los runs históricos inferiores al código nuevo.

## Evidencia y lista histórica conservadas de v0.3

## Ejecutado en el entorno de compilación

- [x] Fuente v0.2 original: build .NET SDK 10.0.402 contra las tres DLL proporcionadas; un aviso CS8600, cero errores.
- [x] Fuente actual: build Release con advertencias como errores; cero advertencias y errores.
- [x] 40 pruebas C# del asignador/registro/disponibilidad; todas pasan.
- [x] Invariantes de elegibilidad y orden determinista en 200 colonias generadas con semilla reproducible.
- [x] Estabilidad en 50 escenarios de 20 colonos durante cinco refrescos sin cambios.
- [x] 10 pruebas Python de entrada privada y distribución; todas pasan.
- [x] Benchmark local del asignador con 1, 5, 20 y 50 colonos y 20 trabajos, 500 muestras; memoria, mediana y p95 en core-tests.json. No mide Android.
- [x] Metadatos PE/.NET de referencias reales y DLL del mod; dependencias y arquitectura IL.
- [x] ZIP y suma SHA-256; solo DLL propia, About, instrucciones y licencia.

Las 40 pruebas de asignación/estado son puras y no ejecutan Verse.Scribe. Como ensayo adicional separado se ejecutó `tools/scribe_smoke.py`: escritura XML completada y lectura **bloqueada** por `com.rlabrecque.steamworks.net` 0.0.0.0 y `Brutal.Core.Logging` 2026.9.0.0 ausentes. No se cuenta como prueba superada ni sustituye la validación siguiente. Los detalles y límites están en AUDITORIA.md.

## Pendiente en RimWorld 1.7, primero PC y luego Android

Usar una partida de prueba o una copia nueva. Mantener una copia anterior intacta. Registrar versión de RimWorld/RimDroid, modelo/GPU, lista de mods y Player.log/registro equivalente.

1. **Carga y opciones:** Core + este mod; reiniciar, verificar ausencia de errores de carga, abrir opciones desde menú y desde partida. Probar orientación y tamaño de ventana, scroll, etiquetas largas y desplegables.
2. **Consentimiento por partida:** cargar una partida antigua sin el mod, esperar una jornada y comprobar tabla intacta. Activar solo en la copia. Otra partida nueva debe empezar con el gestor apagado.
3. **Prioridades numéricas:** con números apagados, activar el gestor no debe cambiar tabla, crear bloqueos ni descartar estado. Con números encendidos, habilitar los trabajos deseados y recalcular. Apagar/encender números durante gestión; verificar conservación y restauración después.
4. **1/5/20/50 colonos:** probar habilidades/pasiones distintas, incapacidad de trabajo, niños, todos incapaces, cero colonos, oficios sin habilidades y nuevos WorkTypeDef. Comprobar titulares, apoyos y limpieza/transporte; observar tiempo y escrituras del panel, sin atribuir los tiempos x64 a Android.
5. **Decisiones manuales:** cambiar a 0, 1, 2, 3 y 4 una celda gestionada; pulsar inmediatamente Recalcular y esperar varios intervalos. Habilitar una celda antes a 0 mientras está activo. Los cambios observables no deben sobrescribirse. Probar pausas por tipo de trabajo y liberación explícita de bloqueos.
6. **Exclusiones/restauración:** excluir/reincluir, desactivar/activar, ajustar cobertura a 0 y restaurar a automático. Comparar tabla previa y actual; solo las celdas aún controladas deben volver al valor inicial. Los bloqueos siguen existiendo hasta acción explícita.
7. **Órdenes y emergencias:** dar órdenes forzadas de trabajo/movimiento, reclutar y desreclutar, combate, incendios y pacientes. No cancelar órdenes forzadas ni tocar jobs, horarios, comida, sueño, ropa, medicinas o selfTend. Las tareas urgentes que el jugador haya desactivado a 0 deben permanecer en 0.
8. **Estrés/recuperación:** probar ánimo por debajo de 20% y descanso por debajo de 15%; cobertura alternativa. No reasignar al afectado hasta recuperar al menos 30% de ánimo y 25% de descanso. Probar estado mental, caída, recuperación y reclutamiento.
9. **Guardar/cargar:** verificar activación por partida, exclusiones, prioridades originales/aplicadas, bloqueos y estado de recuperación. Guardar con números apagados y restauración pendiente, cargar, encender números y restaurar. El tiempo guardado no debe impedir refrescos.
10. **Múltiples mapas:** trasladar colonos, caravanas, transporte, mapas temporales, abandono de mapa, nuevos colonos, muerte y retorno. No competir entre componentes ni borrar decisiones manuales. Restaurar pendientes al salir cuando hay referencias válidas.
11. **Migración v0.2:** solo si existe una partida creada con una compilación propia antigua; usar copia. Cargar historia por mapa, importar sin cambios durante Scribe, confirmar bloqueos/exclusiones y restauración. selfTend antiguo no tiene valor original recuperable.
12. **Conflictos:** habilitar Work Manager y comprobar suspensión y restauración condicional. Probar separadamente otros gestores; no se presume integración. No ejecutar dos gestores no reconocidos sobre las mismas celdas durante la validación principal.
13. **Recuperación de errores:** si aparece una excepción real, comprobar pausa y registro, conservar un guardado previo, desactivar/reactivar solo después de revisar la causa. No simular errores sobre la partida principal.
14. **Retirada:** con números encendidos, desactivar, comprobar restauración, guardar copia nueva y retirar el mod. Verificar que el juego siga cargando y conservar la copia previa.

## GitHub Actions

- [x] [Workflow remoto 39239475222](https://github.com/Borjabyxx/RimDroid/actions/runs/39239475222), commit `6a1d2a4`: Success; job público `tests` completado y reporte publicado, sin DLL privadas. Job `package` omitido.
- [ ] Configurar runner privado y variable de activación según README.
- [ ] Ejecutar build remoto en la rama de confianza y descargar el artefacto; revisar sus seis archivos de salida y el ZIP interior de cuatro archivos.
- [ ] Confirmar que las PR solo ejecuten las pruebas públicas, sin tocar el runner privado ni los workflows del APK.
